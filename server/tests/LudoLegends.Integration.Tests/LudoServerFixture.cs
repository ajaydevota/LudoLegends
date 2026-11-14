using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace LudoLegends.Integration.Tests
{
    /// <summary>
    /// Runs the actual server binary on a free loopback port with a throwaway SQLite file.
    ///
    /// This is deliberately a real process over real TCP sockets rather than an in-memory
    /// TestServer: the point of these tests is the multiplayer path a phone takes -
    /// WebSocket handshake, authentication, room broadcast, reconnect, settlement.
    /// </summary>
    public sealed class LudoServerFixture : IAsyncLifetime
    {
        private Process _process;
        private string _databasePath;

        public string BaseUrl { get; private set; }

        public int Port { get; private set; }

        public HttpClient Http { get; private set; }

        /// <summary>Recent server stdout/stderr, so a failing test can explain itself.</summary>
        public System.Collections.Concurrent.ConcurrentQueue<string> Log { get; } =
            new System.Collections.Concurrent.ConcurrentQueue<string>();

        public string LogTail(int lines = 15)
        {
            return string.Join("\n", Log.Reverse().Take(lines).Reverse());
        }

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        public async Task InitializeAsync()
        {
            Port = FreePort();
            BaseUrl = "http://127.0.0.1:" + Port;
            _databasePath = Path.Combine(Path.GetTempPath(), "ludo-it-" + Guid.NewGuid().ToString("N") + ".db");

            string dll = LocateServerAssembly();
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(dll),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(dll);
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
            startInfo.Environment["Ludo__TokenSigningKey"] = "integration-test-signing-key-0123456789abcdef";
            startInfo.Environment["Ludo__Database__Provider"] = "sqlite";
            startInfo.Environment["Ludo__Database__ConnectionString"] = "Data Source=" + _databasePath;
            // Values must satisfy the server's own configuration validation (10..180s turn
            // clock, 10..600s grace), so the fixture exercises the production rules.
            startInfo.Environment["Ludo__Game__TurnTimeoutSeconds"] = "30";
            // 30 s: long enough that a reconnect test is not racing the autopilot takeover.
            // The takeover behaviour itself is covered by the server unit tests.
            startInfo.Environment["Ludo__Game__DisconnectGraceSeconds"] = "30";
            startInfo.Environment["Ludo__Game__RoomIdleTimeoutMinutes"] = "2";
            // Scripted clients act far faster than a human; the limiter is tested separately.
            startInfo.Environment["Ludo__Game__MessagesPerSecond"] = "400";

            _process = Process.Start(startInfo);
            StringBuilder log = new StringBuilder();
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (log) log.AppendLine(e.Data); Log.Enqueue(e.Data); } };
            _process.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (log) log.AppendLine(e.Data); Log.Enqueue(e.Data); } };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(20) };

            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    lock (log) throw new InvalidOperationException("Server exited during startup:\n" + log);
                }

                try
                {
                    HttpResponseMessage response = await Http.GetAsync("/health");
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet.
                }

                await Task.Delay(250);
            }

            lock (log) throw new TimeoutException("Server did not become healthy:\n" + log);
        }

        /// <summary>Guest sign-in over REST, exactly like the Unity client does.</summary>
        public async Task<(string token, string playerId, long balance)> GuestAsync(string name)
        {
            var body = JsonSerializer.Serialize(new { deviceId = Guid.NewGuid().ToString("N"), displayName = name }, Json);
            HttpResponseMessage response = await Http.PostAsync("/api/auth/guest",
                new StringContent(body, Encoding.UTF8, "application/json"));
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Guest login failed: " + json);
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string token = root.GetProperty("token").GetString();
            JsonElement profile = root.GetProperty("profile");
            string id = profile.GetProperty("accountId").GetString();
            long balance = profile.GetProperty("coins").GetInt64();
            return (token, id, balance);
        }

        public async Task<long> BalanceAsync(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/wallet");
            request.Headers.Add("Authorization", "Bearer " + token);
            HttpResponseMessage response = await Http.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Wallet failed: " + json);
            }

            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("balance").GetInt64();
        }

        public async Task DisposeAsync()
        {
            if (Http != null)
            {
                Http.Dispose();
            }

            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill(true);
                    _process.WaitForExit(5000);
                }
                catch (Exception)
                {
                    // Best effort.
                }
            }

            try
            {
                if (_databasePath != null && File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                }
            }
            catch (IOException)
            {
                // The server may still hold the file; a temp file is not worth failing over.
            }

            await Task.CompletedTask;
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string LocateServerAssembly()
        {
            string directory = AppContext.BaseDirectory;
            string root = null;
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory, "server", "LudoLegends.sln")))
                {
                    root = directory;
                    break;
                }

                directory = Path.GetDirectoryName(directory);
            }

            if (root == null)
            {
                throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
            }

            string projectBin = Path.Combine(root, "server", "src", "LudoLegends.Server", "bin");
            if (!Directory.Exists(projectBin))
            {
                throw new InvalidOperationException("Server is not built: " + projectBin);
            }

            string dll = Directory.GetFiles(projectBin, "LudoLegends.Server.dll", SearchOption.AllDirectories)
                .Where(p => p.Contains(Path.DirectorySeparatorChar + "net8.0" + Path.DirectorySeparatorChar))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (dll == null)
            {
                throw new InvalidOperationException("LudoLegends.Server.dll not found under " + projectBin);
            }

            return dll;
        }
    }

    [CollectionDefinition("live server")]
    public sealed class LiveServerCollection : ICollectionFixture<LudoServerFixture>
    {
    }
}
