using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Core;

namespace LudoLegends.Integration.Tests
{
    /// <summary>
    /// A scripted multiplayer peer: its own TCP connection, its own guest account, its own
    /// socket. Four of these in a test are four independent clients, exactly like four
    /// phones talking to the server.
    /// </summary>
    public sealed class TestClient : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly BlockingCollection<ServerEnvelope> _inbox = new BlockingCollection<ServerEnvelope>();
        private Task _reader;

        private TestClient(string name, string token, string playerId)
        {
            Name = name;
            Token = token;
            PlayerId = playerId;
        }

        public string Name { get; }

        public string Token { get; }

        public string PlayerId { get; }

        public int Seat { get; private set; } = -1;

        public GameSnapshot Latest { get; private set; }

        public RoomSnapshot Room { get; private set; }

        /// <summary>Every envelope received, in order - useful for assertions after the fact.</summary>
        public ConcurrentQueue<ServerEnvelope> Received { get; } = new ConcurrentQueue<ServerEnvelope>();

        public static async Task<TestClient> ConnectAsync(string baseUrl, string name, string token, string playerId)
        {
            var client = new TestClient(name, token, playerId);
            string wsUrl = baseUrl.Replace("http://", "ws://").Replace("https://", "wss://") +
                           "/ws?token=" + Uri.EscapeDataString(token);
            await client._socket.ConnectAsync(new Uri(wsUrl), client._cancellation.Token);

            // Only start reading once the handshake succeeded: ReceiveAsync on an
            // unconnected ClientWebSocket throws and would silently kill the loop.
            client._reader = Task.Run(client.ReadLoopAsync);
            return client;
        }

        private async Task ReadLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            var message = new System.IO.MemoryStream();

            try
            {
                while (!_cancellation.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellation.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(message.ToArray());
                    message.SetLength(0);

                    ServerEnvelope envelope = JsonSerializer.Deserialize<ServerEnvelope>(json, LudoServerFixture.Json);
                    if (envelope == null)
                    {
                        continue;
                    }

                    if (envelope.State != null)
                    {
                        Latest = envelope.State;
                        if (Seat < 0 && envelope.State.Seats != null)
                        {
                            SeatSnapshot mine = envelope.State.Seats.FirstOrDefault(s => s.PlayerId == PlayerId);
                            if (mine != null)
                            {
                                Seat = mine.Index;
                            }
                        }
                    }

                    if (envelope.Room != null)
                    {
                        Room = envelope.Room;
                        if (Seat < 0)
                        {
                            RoomMemberSnapshot mine = envelope.Room.Members?.FirstOrDefault(m => m.PlayerId == PlayerId);
                            if (mine != null)
                            {
                                Seat = mine.Seat;
                            }
                        }
                    }

                    Received.Enqueue(envelope);
                    _inbox.Add(envelope);
                }
            }
            catch (OperationCanceledException)
            {
                // Disposed.
            }
            catch (WebSocketException)
            {
                // Peer closed / test tore the socket down.
            }
            catch (Exception)
            {
                // A closed inbox is normal during teardown.
            }
        }

        public Task SendAsync(object payload)
        {
            string json = JsonSerializer.Serialize(payload, LudoServerFixture.Json);
            return SendRawAsync(json);
        }

        private async Task SendRawAsync(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellation.Token);
        }

        public Task FindMatchAsync(int seats, long fee)
        {
            return SendAsync(new { type = MessageTypes.FindMatch, commandId = NewId(), seatCount = seats, entryFee = fee });
        }

        public Task CreateRoomAsync(int seats, long fee)
        {
            return SendAsync(new { type = MessageTypes.CreateRoom, commandId = NewId(), seatCount = seats, entryFee = fee });
        }

        public Task JoinRoomAsync(string code)
        {
            return SendAsync(new { type = MessageTypes.JoinRoom, commandId = NewId(), roomCode = code });
        }

        public Task StartGameAsync()
        {
            return SendAsync(new { type = MessageTypes.StartGame, commandId = NewId() });
        }

        public Task AddBotAsync()
        {
            return SendAsync(new { type = MessageTypes.AddBot, commandId = NewId() });
        }

        public Task CancelMatchAsync()
        {
            return SendAsync(new { type = MessageTypes.CancelMatch, commandId = NewId() });
        }

        public Task RollAsync(string commandId = null)
        {
            return SendAsync(new { type = MessageTypes.Roll, commandId = commandId ?? NewId() });
        }

        public Task MoveAsync(int tokenIndex, string commandId = null)
        {
            return SendAsync(new { type = MessageTypes.MoveToken, commandId = commandId ?? NewId(), tokenIndex = tokenIndex });
        }

        public Task ResyncAsync()
        {
            return SendAsync(new { type = MessageTypes.Resync, commandId = NewId() });
        }

        /// <summary>Waits for the next envelope matching a predicate, or fails after a timeout.</summary>
        public ServerEnvelope WaitFor(Func<ServerEnvelope, bool> predicate, int timeoutMs = 8000, string because = null)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                int remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                if (_inbox.TryTake(out ServerEnvelope envelope, remaining))
                {
                    if (predicate(envelope))
                    {
                        return envelope;
                    }
                }
            }

            string recent = string.Join("; ", Received.Reverse().Take(6).Reverse().Select(e =>
                e.Type + (e.ErrorCode != null ? "(" + e.ErrorCode + ")" : string.Empty) +
                (e.State != null ? " v" + e.State.Version + " phase" + e.State.Phase : string.Empty)));

            throw new TimeoutException(Name + " never received the expected message" +
                                       (because == null ? "." : ": " + because) +
                                       " | recent: [" + recent + "] | latest v" + (Latest?.Version ?? -1) +
                                       " seat " + Seat + " active " + (Latest?.ActiveSeat ?? -1));
        }

        public ServerEnvelope WaitForState(int afterVersion, int timeoutMs = 8000)
        {
            return WaitFor(e => e.Type == MessageTypes.GameStateMsg && e.State != null && e.State.Version > afterVersion,
                timeoutMs, "game state after version " + afterVersion);
        }

        /// <summary>Waits until this client's snapshot shows it is the active seat.</summary>
        public bool WaitForMyTurn(int timeoutMs = 15000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                GameSnapshot snapshot = Latest;
                if (snapshot != null && snapshot.ActiveSeat == Seat && snapshot.WinnerSeat < 0)
                {
                    return true;
                }

                Thread.Sleep(2);
            }

            return false;
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (Exception)
            {
                // Ignore.
            }

            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test done", timeout.Token);
                }
            }
            catch (Exception)
            {
                // Abort below.
            }

            _socket.Abort();
            _socket.Dispose();

            try
            {
                if (_reader != null)
                {
                    await _reader;
                }
            }
            catch (Exception)
            {
                // Ignore teardown races.
            }
        }

        /// <summary>Drops the socket without a clean close, simulating a lost connection.</summary>
        public void Kill()
        {
            _cancellation.Cancel();
            _socket.Abort();
        }
    }
}
