using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Core;
using LudoLegends.Server.Auth;
using LudoLegends.Server.Config;
using LudoLegends.Server.Data;
using LudoLegends.Server.Economy;
using LudoLegends.Server.Rooms;
using LudoLegends.Server.Social;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LudoLegends.Server.Net
{
    /// <summary>
    /// WebSocket entry point. Authenticates the session token, then routes intents to the
    /// room the player belongs to.
    ///
    /// Security properties enforced here:
    ///   * the account id comes from the verified token, never from the message;
    ///   * message size and rate are bounded per socket;
    ///   * every game action is validated by the room + engine, so a modified client can
    ///     only ask for legal things;
    ///   * dice are never accepted from the client at all.
    /// </summary>
    public sealed class GameHub
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly TokenValidator _tokens;
        private readonly ConnectionRegistry _registry;
        private readonly RoomManager _rooms;
        private readonly PresenceTracker _presence;
        private readonly ServerOptions _options;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<GameHub> _log;

        public GameHub(
            TokenValidator tokens,
            ConnectionRegistry registry,
            RoomManager rooms,
            PresenceTracker presence,
            ServerOptions options,
            IServiceScopeFactory scopes,
            ILogger<GameHub> log)
        {
            _tokens = tokens;
            _registry = registry;
            _rooms = rooms;
            _presence = presence;
            _options = options;
            _scopes = scopes;
            _log = log;
        }

        public async Task HandleAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.Response.WriteAsync("WebSocket endpoint. Connect with a session token.");
                return;
            }

            string token = context.Request.Query["token"];
            if (string.IsNullOrWhiteSpace(token))
            {
                string header = context.Request.Headers["Authorization"];
                if (!string.IsNullOrWhiteSpace(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = header.Substring(7).Trim();
                }
            }

            string accountId = await _tokens.ValidateAccountIdAsync(token);
            if (string.IsNullOrEmpty(accountId))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Invalid or expired session.");
                return;
            }

            Account account;
            using (IServiceScope scope = _scopes.CreateScope())
            {
                GameDbContext db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
                account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId && a.DeletedUtc == null);
            }

            if (account == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Account not found.");
                return;
            }

            WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
            var connection = new ClientConnection(accountId, socket) { DisplayName = account.DisplayName };
            _registry.Register(connection);
            _presence.Attach(accountId);

            var rate = new SocketRateLimiter(_options.Game.MessagesPerSecond);
            var cancellation = context.RequestAborted;

            try
            {
                await SendWelcomeAsync(connection, account, cancellation);

                // Reconnection: if the account still sits in a room, replay its state.
                GameRoom existingRoom = _rooms.FindRoomOf(accountId);
                if (existingRoom != null)
                {
                    foreach (ServerEnvelope envelope in existingRoom.Attach(accountId))
                    {
                        await connection.SendAsync(envelope, JsonOptions, cancellation);
                    }

                    await BroadcastRoomAsync(existingRoom, new[]
                    {
                        new ServerEnvelope
                        {
                            Type = MessageTypes.PlayerConnection,
                            Seat = existingRoom.Players.FirstOrDefault(p => p.AccountId == accountId)?.Seat ?? 0,
                            DisplayName = account.DisplayName,
                            Room = existingRoom.Snapshot()
                        }
                    }, cancellation);
                }

                await ReceiveLoopAsync(connection, rate, cancellation);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown or client closed the app.
            }
            catch (WebSocketException ex)
            {
                _log.LogInformation(ex, "Socket closed for {AccountId}", accountId);
            }
            finally
            {
                connection.Closed = true;
                _registry.Unregister(connection);
                _presence.Detach(accountId);

                // Only detach the seat when this socket really is the player's current
                // one. On a reconnect the registry already holds the new connection, and
                // the old socket's cleanup must not mark the player offline again.
                bool superseded = _registry.TryGet(accountId, out ClientConnection live) && !ReferenceEquals(live, connection);
                GameRoom room = superseded ? null : _rooms.FindRoomOf(accountId);
                if (room != null)
                {
                    await BroadcastRoomAsync(room, room.Detach(accountId), CancellationToken.None);
                }

                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    }
                    catch (Exception)
                    {
                        // Already closed.
                    }
                }

                socket.Dispose();
            }
        }

        private async Task SendWelcomeAsync(ClientConnection connection, Account account, CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopes.CreateScope();
            CoinService coins = scope.ServiceProvider.GetRequiredService<CoinService>();
            long balance = await coins.GetBalanceAsync(account.Id, cancellationToken);

            await connection.SendAsync(new ServerEnvelope
            {
                Type = MessageTypes.Welcome,
                DisplayName = account.DisplayName,
                Balance = balance,
                Room = null
            }, JsonOptions, cancellationToken);
        }

        private async Task ReceiveLoopAsync(ClientConnection connection, SocketRateLimiter rate, CancellationToken cancellationToken)
        {
            var buffer = new byte[Math.Max(1024, _options.Game.MaxMessageBytes)];
            var messageBuffer = new List<byte>(1024);

            while (!cancellationToken.IsCancellationRequested && connection.Socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await connection.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                if (result.Count > 0)
                {
                    messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
                    if (messageBuffer.Count > _options.Game.MaxMessageBytes)
                    {
                        await RejectAsync(connection, ErrorCodes.InvalidPayload, "Message too large.", null, cancellationToken);
                        return;
                    }
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                string json = System.Text.Encoding.UTF8.GetString(messageBuffer.ToArray());
                messageBuffer.Clear();

                if (!rate.TryAcquire())
                {
                    await RejectAsync(connection, ErrorCodes.RateLimited, "Slow down.", null, cancellationToken);
                    continue;
                }

                ClientEnvelope message = Parse(json);
                if (message == null)
                {
                    await RejectAsync(connection, ErrorCodes.InvalidPayload, "Malformed JSON.", null, cancellationToken);
                    continue;
                }

                try
                {
                    await RouteAsync(connection, message, cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to handle {Type} for {AccountId}", message.Type, connection.AccountId);
                    await RejectAsync(connection, ErrorCodes.Internal, "Server error.", message.CommandId, cancellationToken);
                }
            }
        }

        private static ClientEnvelope Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 8192)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ClientEnvelope>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task RouteAsync(ClientConnection connection, ClientEnvelope message, CancellationToken cancellationToken)
        {
            switch ((message.Type ?? string.Empty).Trim())
            {
                case MessageTypes.Ping:
                    await connection.SendAsync(new ServerEnvelope { Type = MessageTypes.Pong }, JsonOptions, cancellationToken);
                    return;

                case MessageTypes.CreateRoom:
                {
                    _rooms.ReleaseFromFinishedRoom(connection.AccountId);
                    var info = await LoadPlayerInfoAsync(connection.AccountId, cancellationToken);
                    GameRoom room = _rooms.CreatePrivateRoom(info, message.SeatCount, Math.Max(0, message.EntryFee), out string error);
                    if (room == null)
                    {
                        await RejectAsync(connection, ErrorCodes.InvalidPayload, error, message.CommandId, cancellationToken);
                        return;
                    }

                    await connection.SendAsync(new ServerEnvelope
                    {
                        Type = MessageTypes.RoomState,
                        Room = room.Snapshot()
                    }, JsonOptions, cancellationToken);
                    return;
                }

                case MessageTypes.JoinRoom:
                {
                    _rooms.ReleaseFromFinishedRoom(connection.AccountId);
                    var info = await LoadPlayerInfoAsync(connection.AccountId, cancellationToken);
                    (GameRoom room, string error) = _rooms.JoinPrivateRoom(message.RoomCode, info);
                    if (room == null)
                    {
                        await RejectAsync(connection, ErrorCodes.RoomNotFound, error, message.CommandId, cancellationToken);
                        return;
                    }

                    await BroadcastRoomAsync(room, new[]
                    {
                        new ServerEnvelope { Type = MessageTypes.RoomState, Room = room.Snapshot() }
                    }, cancellationToken);

                    return;
                }

                case MessageTypes.FindMatch:
                {
                    _rooms.ReleaseFromFinishedRoom(connection.AccountId);
                    var info = await LoadPlayerInfoAsync(connection.AccountId, cancellationToken);
                    (GameRoom room, string error) = _rooms.FindOrCreateMatch(info, message.SeatCount, Math.Max(0, message.EntryFee));
                    if (room == null)
                    {
                        await RejectAsync(connection, ErrorCodes.Internal, error, message.CommandId, cancellationToken);
                        return;
                    }

                    await BroadcastRoomAsync(room, new[]
                    {
                        new ServerEnvelope { Type = MessageTypes.Queued, Room = room.Snapshot() }
                    }, cancellationToken);

                    if (room.AutoStart && room.Status == RoomStatus.Lobby && room.Players.Count == room.SeatCount)
                    {
                        await StartRoomAsync(room, room.HostPlayerId, cancellationToken);
                    }

                    return;
                }

                case MessageTypes.CancelMatch:
                {
                    _rooms.CancelMatchmaking(connection.AccountId);
                    await connection.SendAsync(new ServerEnvelope
                    {
                        Type = MessageTypes.MatchCancelled,
                        Room = null
                    }, JsonOptions, cancellationToken);
                    return;
                }

                case MessageTypes.StartGame:
                {
                    GameRoom room = _rooms.FindRoomOf(connection.AccountId);
                    if (room == null)
                    {
                        await RejectAsync(connection, ErrorCodes.NotInRoom, "You are not in a room.", message.CommandId, cancellationToken);
                        return;
                    }

                    await StartRoomAsync(room, connection.AccountId, cancellationToken);
                    return;
                }

                case MessageTypes.AddBot:
                {
                    GameRoom room = _rooms.FindRoomOf(connection.AccountId);
                    if (room == null)
                    {
                        await RejectAsync(connection, ErrorCodes.NotInRoom, "You are not in a room.", message.CommandId, cancellationToken);
                        return;
                    }

                    (bool ok, string error) = room.AddBot(connection.AccountId);
                    if (!ok)
                    {
                        await RejectAsync(connection, ErrorCodes.InvalidPayload, error, message.CommandId, cancellationToken);
                        return;
                    }

                    await BroadcastRoomAsync(room, new[]
                    {
                        new ServerEnvelope { Type = MessageTypes.RoomState, Room = room.Snapshot() }
                    }, cancellationToken);
                    return;
                }

                case MessageTypes.SetReady:
                {
                    GameRoom room = _rooms.FindRoomOf(connection.AccountId);
                    if (room == null)
                    {
                        await RejectAsync(connection, ErrorCodes.NotInRoom, "You are not in a room.", message.CommandId, cancellationToken);
                        return;
                    }

                    room.SetReady(connection.AccountId, message.Ready);
                    await BroadcastRoomAsync(room, new[]
                    {
                        new ServerEnvelope { Type = MessageTypes.RoomState, Room = room.Snapshot() }
                    }, cancellationToken);
                    return;
                }

                case MessageTypes.LeaveRoom:
                {
                    GameRoom room = _rooms.FindRoomOf(connection.AccountId);
                    if (room == null)
                    {
                        return;
                    }

                    (bool ok, string error) = room.RemovePlayer(connection.AccountId);
                    if (!ok)
                    {
                        await RejectAsync(connection, ErrorCodes.NotInRoom, error, message.CommandId, cancellationToken);
                        return;
                    }

                    await BroadcastRoomAsync(room, new[]
                    {
                        new ServerEnvelope { Type = MessageTypes.RoomState, Room = room.Snapshot() }
                    }, cancellationToken);

                    if (room.Players.All(p => p.IsBot) || room.Players.Count == 0)
                    {
                        _rooms.RemoveRoom(room);
                    }

                    return;
                }

                case MessageTypes.Roll:
                case MessageTypes.MoveToken:
                case MessageTypes.Resync:
                case MessageTypes.Emote:
                {
                    GameRoom room = _rooms.FindRoomOf(connection.AccountId);
                    if (room == null)
                    {
                        await RejectAsync(connection, ErrorCodes.NotInRoom, "You are not in a room.", message.CommandId, cancellationToken);
                        return;
                    }

                    RoomCommandResult result = room.HandleCommand(connection.AccountId, message);
                    if (result.Direct != null)
                    {
                        await connection.SendAsync(result.Direct, JsonOptions, cancellationToken);
                    }

                    if (result.Broadcast.Count > 0)
                    {
                        bool finished = room.Status == RoomStatus.Finished;
                        await BroadcastRoomAsync(room, result.Broadcast, cancellationToken);
                        if (finished)
                        {
                            MatchOutcome outcome = room.DrainOutcome();
                            if (outcome != null)
                            {
                                await SettleAsync(outcome, cancellationToken);
                            }
                        }
                    }

                    return;
                }

                default:
                    await RejectAsync(connection, ErrorCodes.InvalidPayload, "Unknown message type.", message.CommandId, cancellationToken);
                    return;
            }
        }

        private async Task StartRoomAsync(GameRoom room, string requesterId, CancellationToken cancellationToken)
        {
            (bool ok, string error) = room.TryPrepareStart(requesterId);
            if (!ok)
            {
                await RejectTo(requesterId, ErrorCodes.NotEnoughPlayers, error, cancellationToken);
                return;
            }

            string matchId = Guid.NewGuid().ToString("N");
            using IServiceScope scope = _scopes.CreateScope();
            MatchService matchService = scope.ServiceProvider.GetRequiredService<MatchService>();

            var charged = new List<RoomPlayer>();
            foreach (RoomPlayer player in room.HumanPlayers())
            {
                LedgerResult charge = await matchService.ChargeEntryAsync(matchId, player.AccountId, room.EntryFee, cancellationToken);
                if (!charge.Applied && !charge.Duplicate)
                {
                    foreach (RoomPlayer refund in charged)
                    {
                        await matchService.RefundEntryAsync(matchId, refund.AccountId, room.EntryFee, cancellationToken);
                    }

                    room.CancelStart();
                    await RejectTo(player.AccountId, ErrorCodes.InsufficientCoins, "Not enough coins for this table.", cancellationToken);
                    return;
                }

                charged.Add(player);
            }

            // Write a running match row first: if the process dies mid-match, startup
            // recovery refunds these fees instead of silently keeping them.
            PlayerColor[] colors = BoardTopology.ColorsForSeatCount(room.SeatCount);
            await matchService.BeginAsync(new MatchOutcome
            {
                MatchId = matchId,
                RoomCode = room.Code,
                SeatCount = room.SeatCount,
                EntryFee = room.EntryFee,
                Seats = room.Players.Select(p => new MatchSeatOutcome
                {
                    AccountId = p.IsBot ? null : p.AccountId,
                    DisplayName = p.DisplayName,
                    Seat = p.Seat,
                    Color = (int)colors[p.Seat],
                    IsBot = p.IsBot
                }).ToList()
            }, cancellationToken);

            List<ServerEnvelope> envelopes = room.StartMatch(matchId);
            await BroadcastRoomAsync(room, envelopes, cancellationToken);
            _log.LogInformation("Match {MatchId} started in room {Code}", matchId, room.Code);
        }

        private async Task SettleAsync(MatchOutcome outcome, CancellationToken cancellationToken)
        {
            try
            {
                using IServiceScope scope = _scopes.CreateScope();
                MatchService matchService = scope.ServiceProvider.GetRequiredService<MatchService>();
                await matchService.RecordAsync(outcome, cancellationToken);
            }
            catch (Exception ex)
            {
                // Settlement errors must never kill the socket loop; the running match row
                // stays status=running and startup recovery refunds it.
                _log.LogError(ex, "Failed to settle match {MatchId}", outcome?.MatchId);
            }
        }

        private async Task<RoomPlayerInfo> LoadPlayerInfoAsync(string accountId, CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopes.CreateScope();
            GameDbContext db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
            Account account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

            return new RoomPlayerInfo
            {
                AccountId = accountId,
                DisplayName = account?.DisplayName ?? "Player",
                Level = account?.Level ?? 1
            };
        }

        public async Task BroadcastRoomAsync(GameRoom room, IEnumerable<ServerEnvelope> envelopes, CancellationToken cancellationToken)
        {
            List<ServerEnvelope> list = envelopes?.ToList() ?? new List<ServerEnvelope>();
            if (list.Count == 0)
            {
                return;
            }

            foreach (RoomPlayer player in room.Players)
            {
                if (player.IsBot)
                {
                    continue;
                }

                if (!_registry.TryGet(player.AccountId, out ClientConnection connection))
                {
                    continue;
                }

                foreach (ServerEnvelope envelope in list)
                {
                    await connection.SendAsync(envelope, JsonOptions, cancellationToken);
                }
            }
        }

        private async Task RejectAsync(ClientConnection connection, string code, string message, string commandId, CancellationToken cancellationToken)
        {
            await connection.SendAsync(new ServerEnvelope
            {
                Type = MessageTypes.CommandRejected,
                ErrorCode = code,
                Error = message,
                CommandId = commandId
            }, JsonOptions, cancellationToken);
        }

        private async Task RejectTo(string accountId, string code, string message, CancellationToken cancellationToken)
        {
            if (_registry.TryGet(accountId, out ClientConnection connection))
            {
                await RejectAsync(connection, code, message, null, cancellationToken);
            }
        }
    }

    /// <summary>Token bucket: sustained rate with a small burst allowance.</summary>
    public sealed class SocketRateLimiter
    {
        private readonly double _perSecond;
        private readonly double _capacity;
        private double _tokens;
        private DateTime _lastRefill;

        public SocketRateLimiter(int messagesPerSecond)
        {
            _perSecond = Math.Max(1, messagesPerSecond);
            _capacity = _perSecond * 4;
            _tokens = _capacity;
            _lastRefill = DateTime.UtcNow;
        }

        public bool TryAcquire()
        {
            DateTime now = DateTime.UtcNow;
            double elapsed = (now - _lastRefill).TotalSeconds;
            if (elapsed > 0)
            {
                _tokens = Math.Min(_capacity, _tokens + elapsed * _perSecond);
                _lastRefill = now;
            }

            if (_tokens < 1)
            {
                return false;
            }

            _tokens -= 1;
            return true;
        }
    }
}
