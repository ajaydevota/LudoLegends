using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Core;

namespace LudoLegends.Server.Net
{
    /// <summary>One live client socket, with a send lock (WebSocket writes are not concurrent).</summary>
    public sealed class ClientConnection
    {
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public ClientConnection(string accountId, WebSocket socket)
        {
            AccountId = accountId;
            Socket = socket;
            ConnectedUtc = DateTime.UtcNow;
        }

        public string AccountId { get; }

        public WebSocket Socket { get; }

        public DateTime ConnectedUtc { get; }

        public string DisplayName { get; set; }

        public long Seq { get; set; }

        public bool Closed { get; set; }

        public async Task SendAsync(string json, CancellationToken cancellationToken)
        {
            if (Closed || Socket.State != WebSocketState.Open)
            {
                return;
            }

            byte[] payload = Encoding.UTF8.GetBytes(json);

            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    await Socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, cancellationToken);
                }
            }
            catch (Exception)
            {
                Closed = true;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public Task SendAsync(ServerEnvelope envelope, JsonSerializerOptions options, CancellationToken cancellationToken)
        {
            envelope.Seq = ++Seq;
            envelope.ServerTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return SendAsync(JsonSerializer.Serialize(envelope, options), cancellationToken);
        }
    }

    /// <summary>
    /// Maps account ids to live sockets. One socket per account: a new login on another
    /// device replaces the old one, which also simplifies "reconnect" semantics.
    /// </summary>
    public sealed class ConnectionRegistry
    {
        private readonly ConcurrentDictionary<string, ClientConnection> _connections =
            new ConcurrentDictionary<string, ClientConnection>();

        public void Register(ClientConnection connection)
        {
            if (_connections.TryGetValue(connection.AccountId, out ClientConnection previous) &&
                !ReferenceEquals(previous, connection))
            {
                previous.Closed = true;
                try
                {
                    previous.Socket.Abort();
                }
                catch (Exception)
                {
                    // The socket is already gone; nothing to do.
                }
            }

            _connections[connection.AccountId] = connection;
        }

        public void Unregister(ClientConnection connection)
        {
            if (_connections.TryGetValue(connection.AccountId, out ClientConnection current) &&
                ReferenceEquals(current, connection))
            {
                _connections.TryRemove(connection.AccountId, out _);
            }
        }

        public bool TryGet(string accountId, out ClientConnection connection)
        {
            if (accountId != null && _connections.TryGetValue(accountId, out connection) && !connection.Closed)
            {
                return true;
            }

            connection = null;
            return false;
        }

        public System.Collections.Generic.ICollection<ClientConnection> All()
        {
            return _connections.Values;
        }
    }
}
