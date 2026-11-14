using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LudoLegends.Core;
using LudoLegends.Server.Config;
using LudoLegends.Server.Economy;
using Microsoft.Extensions.Logging;

namespace LudoLegends.Server.Rooms
{
    /// <summary>
    /// Owns every live room, private room codes and the matchmaking tables.
    ///
    /// Matchmaking is implemented as "open tables": a player asking for a 3-seat table is
    /// placed in an existing 3-seat lobby with a free seat, or a new one is created and
    /// other players join it. Tables auto-start when full. Private rooms are the same
    /// mechanism but only start when the host says so.
    /// </summary>
    public sealed class RoomManager
    {
        private readonly ConcurrentDictionary<string, GameRoom> _rooms = new ConcurrentDictionary<string, GameRoom>();
        private readonly object _createLock = new object();
        private readonly ServerOptions _options;
        private readonly ILogger<RoomManager> _log;

        public RoomManager(ServerOptions options, ILogger<RoomManager> log)
        {
            _options = options;
            _log = log;
        }

        public IReadOnlyCollection<GameRoom> Rooms
        {
            get { return _rooms.Values.ToList(); }
        }

        public int RoomCount
        {
            get { return _rooms.Count; }
        }

        public GameRoom FindRoomOf(string accountId)
        {
            foreach (GameRoom room in _rooms.Values)
            {
                if (room.IsMember(accountId))
                {
                    return room;
                }
            }

            return null;
        }

        public bool TryGetRoom(string code, out GameRoom room)
        {
            return _rooms.TryGetValue((code ?? string.Empty).Trim().ToUpperInvariant(), out room);
        }

        public GameRoom CreatePrivateRoom(RoomPlayerInfo host, int seatCount, long entryFee, out string error)
        {
            error = null;
            if (seatCount < 2 || seatCount > GameRoom.MaxPlayers)
            {
                error = "Seat count must be 2, 3 or 4.";
                return null;
            }

            if (!IsAllowedEntryFee(entryFee))
            {
                error = "Unsupported table fee.";
                return null;
            }

            if (FindRoomOf(host.AccountId) != null)
            {
                error = "You are already in a room.";
                return null;
            }

            GameRoom room = CreateRoomInternal(host, seatCount, entryFee, false);
            _log.LogInformation("Room {Code} created by {AccountId} ({Seats} seats, fee {Fee})",
                room.Code, host.AccountId, seatCount, entryFee);
            return room;
        }

        public (GameRoom room, string error) JoinPrivateRoom(string code, RoomPlayerInfo player)
        {
            GameRoom existing = FindRoomOf(player.AccountId);
            if (existing != null)
            {
                if (existing.Code == (code ?? string.Empty).Trim().ToUpperInvariant())
                {
                    return (existing, null);
                }

                return (null, "You are already in another room.");
            }

            if (!TryGetRoom(code, out GameRoom room))
            {
                return (null, "Room not found.");
            }

            (bool ok, string error) = room.AddPlayer(player);
            return ok ? (room, null) : (null, error);
        }

        /// <summary>
        /// Finds or creates a matchmade table. The caller starts the match when it fills.
        /// </summary>
        public (GameRoom room, string error) FindOrCreateMatch(RoomPlayerInfo player, int seatCount, long entryFee)
        {
            if (seatCount < 2 || seatCount > GameRoom.MaxPlayers)
            {
                return (null, "Seat count must be 2, 3 or 4.");
            }

            if (!IsAllowedEntryFee(entryFee))
            {
                return (null, "Unsupported table fee.");
            }

            GameRoom existing = FindRoomOf(player.AccountId);
            if (existing != null)
            {
                return (existing, null);
            }

            lock (_createLock)
            {
                GameRoom open = _rooms.Values
                    .Where(r => r.AutoStart &&
                                r.SeatCount == seatCount &&
                                r.EntryFee == entryFee &&
                                r.Status == RoomStatus.Lobby &&
                                r.Players.Count < r.SeatCount &&
                                r.Players.All(p => p.Connected || p.IsBot))
                    .OrderByDescending(r => r.Players.Count)
                    .FirstOrDefault();

                if (open != null)
                {
                    (bool ok, string error) = open.AddPlayer(player);
                    if (ok)
                    {
                        return (open, null);
                    }
                }

                GameRoom room = CreateRoomInternal(player, seatCount, entryFee, true);
                room.AutoStart = true;
                return (room, null);
            }
        }

        /// <summary>
        /// Table stakes are a closed set. Without this a client could pick any fee it liked
        /// (harmless to itself, but it pollutes the economy and the leaderboard tiers).
        /// Keep this list in sync with the fee buttons in the Unity matchmaking screen.
        /// </summary>
        public static bool IsAllowedEntryFee(long fee)
        {
            return fee == 0 || fee == 100 || fee == 250;
        }

        /// <summary>
        /// Leaves a matchmaking queue. Only open, auto-start tables are affected, so a
        /// stray cancel can never eject someone from a private room they are hosting.
        /// </summary>
        public void CancelMatchmaking(string accountId)
        {
            GameRoom room = FindRoomOf(accountId);
            if (room == null || room.Status != RoomStatus.Lobby || !room.AutoStart)
            {
                return;
            }

            room.RemovePlayer(accountId);
            if (room.Players.Count == 0 || room.Players.All(p => p.IsBot))
            {
                RemoveRoom(room);
            }
        }

        /// <summary>
        /// Drops a player out of a room whose match is already over.
        ///
        /// Without this, a finished room keeps its members forever, and every later
        /// "find match" would hand the player back the dead room instead of a new table.
        /// </summary>
        public void ReleaseFromFinishedRoom(string accountId)
        {
            GameRoom room = FindRoomOf(accountId);
            if (room == null || room.Status != RoomStatus.Finished)
            {
                return;
            }

            room.RemovePlayer(accountId);
            if (room.Players.Count == 0 || room.Players.All(p => p.IsBot))
            {
                RemoveRoom(room);
            }
        }

        public void RemoveRoom(GameRoom room)
        {
            if (room != null)
            {
                _rooms.TryRemove(room.Code, out _);
            }
        }

        /// <summary>
        /// Ticks every room. Returns the envelopes that must be broadcast, and the
        /// settlements that must be persisted. Rooms that nobody is in are cleaned up.
        /// </summary>
        public TickReport Tick(DateTime utcNow)
        {
            var report = new TickReport();
            long nowMs = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();

            foreach (GameRoom room in _rooms.Values.ToList())
            {
                List<ServerEnvelope> envelopes = room.Tick();
                if (envelopes.Count > 0)
                {
                    report.Broadcasts.Add((room, envelopes));
                }

                MatchOutcome outcome = room.DrainOutcome();
                if (outcome != null)
                {
                    report.Outcomes.Add(outcome);
                }

                if (room.ShouldBeCleanedUp(nowMs, _options.Game.RoomIdleTimeoutMinutes))
                {
                    MatchOutcome aborted = room.Abort();
                    if (aborted != null && room.Status == RoomStatus.Finished && !string.IsNullOrEmpty(room.MatchId))
                    {
                        report.Outcomes.Add(aborted);
                    }

                    _rooms.TryRemove(room.Code, out _);
                    _log.LogInformation("Room {Code} cleaned up", room.Code);
                }
            }

            return report;
        }

        private GameRoom CreateRoomInternal(RoomPlayerInfo host, int seatCount, long entryFee, bool autoStart)
        {
            GameRoom room = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                string code = GenerateCode();
                var candidate = new GameRoom(
                    code,
                    seatCount,
                    entryFee,
                    host.AccountId,
                    GameRules.Standard(),
                    new CryptoRandomSource(),
                    () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    _options.Game.TurnTimeoutSeconds,
                    _options.Game.DisconnectGraceSeconds)
                {
                    AutoStart = autoStart
                };

                if (_rooms.TryAdd(code, candidate))
                {
                    room = candidate;
                    break;
                }
            }

            if (room == null)
            {
                throw new InvalidOperationException("Could not allocate a room code.");
            }

            (bool ok, string error) = room.AddPlayer(host);
            if (!ok)
            {
                _rooms.TryRemove(room.Code, out _);
                throw new InvalidOperationException("Could not seat the host: " + error);
            }

            return room;
        }

        private static string GenerateCode()
        {
            var builder = new StringBuilder(6);
            for (int i = 0; i < 6; i++)
            {
                builder.Append(GameRoom.CodeAlphabet[RandomNumberGenerator.GetInt32(GameRoom.CodeAlphabet.Length)]);
            }

            return builder.ToString();
        }
    }

    public sealed class TickReport
    {
        public List<(GameRoom room, List<ServerEnvelope> envelopes)> Broadcasts { get; } =
            new List<(GameRoom, List<ServerEnvelope>)>();

        public List<MatchOutcome> Outcomes { get; } = new List<MatchOutcome>();
    }
}
