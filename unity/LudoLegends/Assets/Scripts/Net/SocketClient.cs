using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace LudoLegends.Client
{
    /// <summary>
    /// WebSocket transport for the authoritative server.
    ///
    /// Design notes
    /// ------------
    /// * Every intent carries a fresh CommandId, so retries after a dropped connection are
    ///   deduplicated by the server instead of rolling a second die.
    /// * The client never decides game state: it sends "roll"/"moveToken" and renders the
    ///   GameSnapshot that comes back.
    /// * Reconnection is automatic with exponential backoff. On every reconnect the client
    ///   sends a resync request, so a match survives app backgrounding, network changes and
    ///   server restarts.
    /// * Parsing happens on the Unity main thread (Update) so handlers can touch GameObjects.
    /// </summary>
    public sealed class SocketClient : MonoBehaviour
    {
        /// <summary>
        /// Newtonsoft settings (the project uses Newtonsoft on the client, System.Text.Json
        /// on the server). camelCase keeps the wire format identical to what the server emits.
        /// </summary>
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        /// <summary>Raised on the main thread for every server envelope.</summary>
        public event Action<ServerEnvelope> EnvelopeReceived;

        /// <summary>"connecting", "open", "reconnecting", "closed".</summary>
        public event Action<string> StatusChanged;

        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outbox = new ConcurrentQueue<string>();
        private readonly object _socketLock = new object();

        private ClientWebSocket _socket;
        private CancellationTokenSource _cancellation;
        private string _url;
        private string _token;
        private int _attempt;
        private volatile string _status = "closed";

        public string Status
        {
            get { return _status; }
        }

        public bool IsOpen
        {
            get
            {
                ClientWebSocket socket = _socket;
                return socket != null && socket.State == WebSocketState.Open;
            }
        }

        public void Connect(string url, string sessionToken)
        {
            Disconnect();
            _url = url;
            _token = sessionToken;
            _cancellation = new CancellationTokenSource();
            _attempt = 0;

            CancellationToken token = _cancellation.Token;
            _ = Task.Run(() => RunAsync(token));
        }

        public void Disconnect()
        {
            CancellationTokenSource cancellation = _cancellation;
            _cancellation = null;
            if (cancellation != null)
            {
                try
                {
                    cancellation.Cancel();
                }
                catch (Exception)
                {
                    // Already disposed.
                }
            }

            lock (_socketLock)
            {
                if (_socket != null)
                {
                    try
                    {
                        _socket.Abort();
                        _socket.Dispose();
                    }
                    catch (Exception)
                    {
                        // Socket already gone.
                    }

                    _socket = null;
                }
            }

            SetStatus("closed");
        }

        // ------------------------------------------------------------- sending

        public string SendRoll()
        {
            return SendRaw(MessageTypes.Roll, null, 0);
        }

        public string SendMove(int tokenIndex)
        {
            return SendRaw(MessageTypes.MoveToken, null, tokenIndex);
        }

        public string CancelMatch()
        {
            return SendRaw(MessageTypes.CancelMatch, null, 0);
        }

        public string SendResync()
        {
            return SendRaw(MessageTypes.Resync, null, 0);
        }

        public string FindMatch(int seatCount, long entryFee)
        {
            return SendRaw(MessageTypes.FindMatch, null, 0, envelope =>
            {
                envelope.SeatCount = seatCount;
                envelope.EntryFee = entryFee;
            });
        }

        public string CreateRoom(int seatCount, long entryFee)
        {
            return SendRaw(MessageTypes.CreateRoom, null, 0, envelope =>
            {
                envelope.SeatCount = seatCount;
                envelope.EntryFee = entryFee;
            });
        }

        public string JoinRoom(string roomCode)
        {
            return SendRaw(MessageTypes.JoinRoom, roomCode, 0);
        }

        public string StartGame()
        {
            return SendRaw(MessageTypes.StartGame, null, 0);
        }

        public string AddBot()
        {
            return SendRaw(MessageTypes.AddBot, null, 0);
        }

        public string LeaveRoom()
        {
            return SendRaw(MessageTypes.LeaveRoom, null, 0);
        }

        public string SendReady(bool ready)
        {
            return SendRaw(MessageTypes.SetReady, null, 0, envelope => envelope.Ready = ready);
        }

        public string SendEmote(string emoteId)
        {
            return SendRaw(MessageTypes.Emote, null, 0, envelope => envelope.EmoteId = emoteId);
        }

        public string SendPing()
        {
            return SendRaw(MessageTypes.Ping, null, 0);
        }

        private string SendRaw(string type, string roomCode, int tokenIndex, Action<ClientEnvelope> configure = null)
        {
            var envelope = new ClientEnvelope
            {
                Type = type,
                CommandId = Guid.NewGuid().ToString("N"),
                RoomCode = roomCode ?? string.Empty,
                TokenIndex = tokenIndex
            };

            configure?.Invoke(envelope);

            string json = JsonConvert.SerializeObject(envelope, Settings);
            _outbox.Enqueue(json);
            return envelope.CommandId;
        }

        // ------------------------------------------------------------- loop

        private void Update()
        {
            while (_inbox.TryDequeue(out string json))
            {
                ServerEnvelope envelope;
                try
                {
                    envelope = JsonConvert.DeserializeObject<ServerEnvelope>(json, Settings);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Bad envelope: " + ex.Message);
                    continue;
                }

                if (envelope == null)
                {
                    continue;
                }

                try
                {
                    EnvelopeReceived?.Invoke(envelope);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Envelope handler threw: " + ex);
                }
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                try
                {
                    SetStatus(_attempt == 0 ? "connecting" : "reconnecting");
                    string uri = _url + (_url.Contains("?") ? "&" : "?") + "token=" + Uri.EscapeDataString(_token ?? string.Empty);
                    await socket.ConnectAsync(new Uri(uri), cancellationToken);

                    lock (_socketLock)
                    {
                        _socket = socket;
                    }

                    _attempt = 0;
                    SetStatus("open");

                    // The server replays room + game state on every reconnect, but asking
                    // explicitly makes the intent obvious and covers the login window.
                    _outbox.Enqueue(JsonConvert.SerializeObject(new ClientEnvelope
                    {
                        Type = MessageTypes.Resync,
                        CommandId = Guid.NewGuid().ToString("N")
                    }, Settings));

                    await PumpAsync(socket, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Socket error: " + ex.Message);
                }
                finally
                {
                    lock (_socketLock)
                    {
                        if (ReferenceEquals(_socket, socket))
                        {
                            _socket = null;
                        }
                    }

                    try
                    {
                        socket.Dispose();
                    }
                    catch (Exception)
                    {
                        // Ignore.
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                SetStatus("reconnecting");
                _attempt++;
                double delay = Math.Min(15.0, Math.Pow(1.7, _attempt));
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            SetStatus("closed");
        }

        private async Task PumpAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            Task sendLoop = SendLoopAsync(socket, cancellationToken);
            var buffer = new byte[8192];
            var message = new MemoryStream(8192);

            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try
                        {
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken);
                        }
                        catch (Exception)
                        {
                            // Peer closed already.
                        }

                        break;
                    }

                    if (result.Count > 0)
                    {
                        message.Write(buffer, 0, result.Count);
                    }

                    if (message.Length > 256 * 1024)
                    {
                        // Defensive: never let a hostile peer grow our memory.
                        message.SetLength(0);
                        continue;
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(message.ToArray());
                    message.SetLength(0);
                    _inbox.Enqueue(json);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            finally
            {
                await Task.WhenAny(sendLoop, Task.Delay(50));
            }
        }

        private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    if (_outbox.TryDequeue(out string json))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(json);
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(16, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Send loop stopped: " + ex.Message);
            }
        }

        private void SetStatus(string status)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
            try
            {
                StatusChanged?.Invoke(status);
            }
            catch (Exception ex)
            {
                Debug.LogError("Status handler threw: " + ex);
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}
