using System;
using System.Collections.Generic;
using System.Linq;
using LudoLegends.Core;
using LudoLegends.Server.Economy;

namespace LudoLegends.Server.Rooms
{
    public enum RoomStatus
    {
        Lobby = 0,
        Playing = 1,
        Finished = 2
    }

    public sealed class RoomPlayer
    {
        public string AccountId { get; set; }
        public string DisplayName { get; set; }
        public int Seat { get; set; }
        public bool IsBot { get; set; }
        public bool Connected { get; set; }
        public int Level { get; set; }
        public bool Ready { get; set; }
        public long DisconnectedAtMs { get; set; }
        public int Timeouts { get; set; }
        public bool AutoPilot { get; set; }
    }

    public sealed class RoomPlayerInfo
    {
        public string AccountId { get; set; }
        public string DisplayName { get; set; }
        public int Level { get; set; }
        public bool IsBot { get; set; }
    }

    /// <summary>Result of handling one client message.</summary>
    public sealed class RoomCommandResult
    {
        public ServerEnvelope Direct { get; set; }
        public List<ServerEnvelope> Broadcast { get; } = new List<ServerEnvelope>();
        public bool Accepted { get; set; }
    }

    /// <summary>
    /// One authoritative game room.
    ///
    /// Everything that can change game state goes through <see cref="HandleCommand"/>,
    /// under a lock, through the shared <see cref="LudoGame"/> engine. Dice values,
    /// turn ownership, captures and results are therefore decided here and nowhere else.
    /// Clients only ever send "roll" or "move token N" intents.
    ///
    /// The whole room is a single in-memory object guarded by one lock: a Ludo turn takes
    /// microseconds, so this scales to thousands of rooms per instance.
    /// </summary>
    public sealed class GameRoom
    {
        public const int MaxPlayers = 4;
        public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly object _gate = new object();
        private readonly List<RoomPlayer> _players = new List<RoomPlayer>();
        private readonly GameRules _rules;
        private readonly long _turnTimeoutMs;
        private readonly long _graceMs;
        private readonly Func<long> _clock;
        private readonly int[] _sixesBySeat = new int[MaxPlayers];
        private readonly IRandomSource _random;

        private LudoGame _game;
        private RoomStatus _status = RoomStatus.Lobby;
        private long _turnDeadlineMs;
        private long _lastActivityMs;
        private long _seq;
        private MatchOutcome _pendingOutcome;
        private bool _startPending;

        public GameRoom(
            string code,
            int seatCount,
            long entryFee,
            string hostPlayerId,
            GameRules rules,
            IRandomSource random,
            Func<long> clock,
            int turnTimeoutSeconds,
            int disconnectGraceSeconds)
        {
            if (seatCount < 2 || seatCount > MaxPlayers)
            {
                throw new ArgumentOutOfRangeException(nameof(seatCount), "Seat count must be 2..4.");
            }

            Code = code;
            SeatCount = seatCount;
            EntryFee = entryFee;
            HostPlayerId = hostPlayerId;
            _rules = rules ?? GameRules.Standard();
            _random = random ?? new CryptoRandomSource();
            _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _turnTimeoutMs = Math.Max(5, turnTimeoutSeconds) * 1000L;
            _graceMs = Math.Max(5, disconnectGraceSeconds) * 1000L;
            _lastActivityMs = _clock();
        }

        public string Code { get; }

        /// <summary>True for matchmade tables that start as soon as every seat is filled.</summary>
        public bool AutoStart { get; set; }

        public int SeatCount { get; }

        public long EntryFee { get; }

        public string HostPlayerId { get; private set; }

        public string MatchId { get; private set; }

        public RoomStatus Status
        {
            get
            {
                lock (_gate)
                {
                    return _status;
                }
            }
        }

        public GameState GameState
        {
            get
            {
                lock (_gate)
                {
                    return _game?.State;
                }
            }
        }

        public LudoGame Game
        {
            get
            {
                lock (_gate)
                {
                    return _game;
                }
            }
        }

        public IReadOnlyList<RoomPlayer> Players
        {
            get
            {
                lock (_gate)
                {
                    return _players.ToList();
                }
            }
        }

        public bool IsMember(string accountId)
        {
            lock (_gate)
            {
                return _players.Any(p => p.AccountId == accountId);
            }
        }

        public long LastActivityMs
        {
            get
            {
                lock (_gate)
                {
                    return _lastActivityMs;
                }
            }
        }

        public RoomSnapshot Snapshot()
        {
            lock (_gate)
            {
                return BuildRoomSnapshotLocked();
            }
        }

        private RoomSnapshot BuildRoomSnapshotLocked()
        {
            return new RoomSnapshot
            {
                Code = Code,
                HostPlayerId = HostPlayerId,
                SeatCount = SeatCount,
                Status = (int)_status,
                EntryFee = EntryFee,
                IsRanked = EntryFee > 0,
                Members = _players.Select(p => new RoomMemberSnapshot
                {
                    PlayerId = p.AccountId,
                    DisplayName = p.DisplayName,
                    Seat = p.Seat,
                    Connected = p.Connected,
                    Level = p.Level,
                    IsBot = p.IsBot,
                    Ready = p.Ready
                }).ToArray()
            };
        }

        // ---------------------------------------------------------------- lobby

        public (bool ok, string error) AddPlayer(RoomPlayerInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.AccountId))
            {
                return (false, "A player id is required.");
            }

            lock (_gate)
            {
                if (_status != RoomStatus.Lobby)
                {
                    return (false, "The match has already started.");
                }

                if (_players.Count >= SeatCount)
                {
                    return (false, "The room is full.");
                }

                if (_players.Any(p => p.AccountId == info.AccountId))
                {
                    return (false, "You are already in this room.");
                }

                _players.Add(new RoomPlayer
                {
                    AccountId = info.AccountId,
                    DisplayName = NormalizeName(info.DisplayName, _players.Count + 1),
                    Seat = _players.Count,
                    IsBot = info.IsBot,
                    Connected = !info.IsBot,
                    Level = Math.Max(1, info.Level),
                    Ready = info.IsBot
                });

                _lastActivityMs = _clock();
                return (true, null);
            }
        }

        public (bool ok, string error) AddBot(string requesterId)
        {
            lock (_gate)
            {
                if (requesterId != HostPlayerId)
                {
                    return (false, "Only the host can add bots.");
                }

                if (_status != RoomStatus.Lobby)
                {
                    return (false, "The match has already started.");
                }

                if (_players.Count >= SeatCount)
                {
                    return (false, "The room is full.");
                }

                int index = _players.Count;
                _players.Add(new RoomPlayer
                {
                    AccountId = "bot:" + Code + ":" + index,
                    DisplayName = "Bot " + (index + 1),
                    Seat = index,
                    IsBot = true,
                    Connected = true,
                    Level = 1,
                    Ready = true,
                    AutoPilot = true
                });

                _lastActivityMs = _clock();
                return (true, null);
            }
        }

        public (bool ok, string error) RemovePlayer(string accountId)
        {
            lock (_gate)
            {
                RoomPlayer player = _players.FirstOrDefault(p => p.AccountId == accountId);
                if (player == null)
                {
                    return (false, "You are not in this room.");
                }

                if (_status == RoomStatus.Playing)
                {
                    // Mid-match: keep the seat so the game stays valid; the server plays
                    // for the seat until it finishes. Leaving never hands an advantage to
                    // opponents by deleting tokens.
                    player.AutoPilot = true;
                    player.Connected = false;
                    player.DisconnectedAtMs = _clock();
                    _lastActivityMs = _clock();
                    return (true, null);
                }

                _players.Remove(player);
                for (int i = 0; i < _players.Count; i++)
                {
                    _players[i].Seat = i;
                }

                if (player.AccountId == HostPlayerId)
                {
                    HostPlayerId = _players.FirstOrDefault(p => !p.IsBot)?.AccountId ?? _players.FirstOrDefault()?.AccountId;
                }

                _lastActivityMs = _clock();
                return (true, null);
            }
        }

        public (bool ok, string error) SetReady(string accountId, bool ready)
        {
            lock (_gate)
            {
                RoomPlayer player = _players.FirstOrDefault(p => p.AccountId == accountId);
                if (player == null)
                {
                    return (false, "You are not in this room.");
                }

                player.Ready = ready;
                _lastActivityMs = _clock();
                return (true, null);
            }
        }

        /// <summary>
        /// Validates that a start can happen and marks the room as starting so two
        /// simultaneous start requests cannot both charge entry fees.
        /// </summary>
        public (bool ok, string error) TryPrepareStart(string requesterId)
        {
            lock (_gate)
            {
                if (_status != RoomStatus.Lobby)
                {
                    return (false, "The match has already started.");
                }

                if (_startPending)
                {
                    return (false, "The match is starting.");
                }

                RoomPlayer requester = _players.FirstOrDefault(p => p.AccountId == requesterId);
                if (requester == null)
                {
                    return (false, "You are not in this room.");
                }

                if (requesterId != HostPlayerId && !_players.All(p => p.Ready))
                {
                    return (false, "Only the host can start the match.");
                }

                if (_players.Count != SeatCount)
                {
                    return (false, "Waiting for " + (SeatCount - _players.Count) + " more player(s).");
                }

                if (_players.Any(p => !p.IsBot && !p.Connected))
                {
                    return (false, "A player is disconnected.");
                }

                _startPending = true;
                _lastActivityMs = _clock();
                return (true, null);
            }
        }

        public void CancelStart()
        {
            lock (_gate)
            {
                _startPending = false;
            }
        }

        public List<RoomPlayer> HumanPlayers()
        {
            lock (_gate)
            {
                return _players.Where(p => !p.IsBot).ToList();
            }
        }

        /// <summary>Creates the authoritative game. Call after entry fees were charged.</summary>
        public List<ServerEnvelope> StartMatch(string matchId)
        {
            lock (_gate)
            {
                if (_status != RoomStatus.Lobby || _players.Count != SeatCount)
                {
                    _startPending = false;
                    return new List<ServerEnvelope>();
                }

                MatchId = matchId;
                PlayerColor[] colors = BoardTopology.ColorsForSeatCount(_players.Count);

                var seats = _players
                    .OrderBy(p => p.Seat)
                    .Select(p => new SeatDefinition(p.AccountId, p.DisplayName, colors[p.Seat], p.IsBot))
                    .ToList();

                _game = LudoGame.Start(_rules, seats, _random);
                _status = RoomStatus.Playing;
                _startPending = false;
                _lastActivityMs = _clock();
                _turnDeadlineMs = _clock() + _turnTimeoutMs;

                var envelopes = new List<ServerEnvelope>
                {
                    new ServerEnvelope
                    {
                        Type = MessageTypes.RoomState,
                        Seq = ++_seq,
                        ServerTimeMs = _clock(),
                        Room = BuildRoomSnapshotLocked()
                    },
                    BuildStateEnvelopeLocked(Array.Empty<GameEvent>())
                };

                return envelopes;
            }
        }

        // ---------------------------------------------------------------- commands

        public RoomCommandResult HandleCommand(string accountId, ClientEnvelope message)
        {
            var result = new RoomCommandResult();

            if (message == null || string.IsNullOrWhiteSpace(message.Type))
            {
                result.Direct = Reject(accountId, ErrorCodes.InvalidPayload, "Malformed message.", null);
                return result;
            }

            lock (_gate)
            {
                RoomPlayer player = _players.FirstOrDefault(p => p.AccountId == accountId);
                if (player == null)
                {
                    result.Direct = Reject(accountId, ErrorCodes.NotInRoom, "You are not in this room.", message.CommandId);
                    return result;
                }

                _lastActivityMs = _clock();

                switch (message.Type)
                {
                    case MessageTypes.Resync:
                        if (_game != null)
                        {
                            result.Direct = BuildStateEnvelopeLocked(Array.Empty<GameEvent>());
                        }
                        else
                        {
                            result.Direct = new ServerEnvelope
                            {
                                Type = MessageTypes.RoomState,
                                Seq = ++_seq,
                                ServerTimeMs = _clock(),
                                Room = BuildRoomSnapshotLocked()
                            };
                        }
                        result.Accepted = true;
                        return result;

                    case MessageTypes.Roll:
                        if (_status != RoomStatus.Playing)
                        {
                            result.Direct = Reject(accountId, ErrorCodes.NotImplementedInMode, "The match is not running.", message.CommandId);
                            return result;
                        }

                        return ApplyEngineCommandLocked(player, message, () => _game.Roll(player.Seat, message.CommandId), result);

                    case MessageTypes.MoveToken:
                        if (_status != RoomStatus.Playing)
                        {
                            result.Direct = Reject(accountId, ErrorCodes.NotImplementedInMode, "The match is not running.", message.CommandId);
                            return result;
                        }

                        if (message.TokenIndex < 0 || message.TokenIndex >= _rules.TokensPerPlayer)
                        {
                            result.Direct = Reject(accountId, ErrorCodes.InvalidPayload, "Token index out of range.", message.CommandId);
                            return result;
                        }

                        return ApplyEngineCommandLocked(player, message, () => _game.MoveToken(player.Seat, message.TokenIndex, message.CommandId), result);

                    case MessageTypes.Emote:
                        result.Accepted = true;
                        result.Broadcast.Add(new ServerEnvelope
                        {
                            Type = MessageTypes.EmoteMsg,
                            Seq = ++_seq,
                            ServerTimeMs = _clock(),
                            Seat = player.Seat,
                            EmoteId = SanitizeEmote(message.EmoteId),
                            DisplayName = player.DisplayName
                        });
                        return result;

                    default:
                        result.Direct = Reject(accountId, ErrorCodes.InvalidPayload, "Unknown message type.", message.CommandId);
                        return result;
                }
            }
        }

        private RoomCommandResult ApplyEngineCommandLocked(RoomPlayer player, ClientEnvelope message, Func<CommandResult> action, RoomCommandResult result)
        {
            CommandResult command = action();

            if (command.Status == CommandStatus.Duplicate)
            {
                // Retried command after a flaky network: no state change, just resync.
                result.Accepted = true;
                result.Direct = BuildStateEnvelopeLocked(Array.Empty<GameEvent>());
                return result;
            }

            if (!command.Accepted)
            {
                result.Direct = Reject(player.AccountId, MapStatus(command.Status), command.Message, message.CommandId);
                return result;
            }

            player.Timeouts = 0;
            // A player who just acted is clearly present, so hand control back.
            player.AutoPilot = player.IsBot;

            foreach (GameEvent evt in command.Events)
            {
                if (evt.Kind == GameEventKind.DiceRolled && evt.Die == 6 && evt.Seat >= 0 && evt.Seat < _sixesBySeat.Length)
                {
                    _sixesBySeat[evt.Seat]++;
                }
            }

            if (_game.State.IsOver)
            {
                _status = RoomStatus.Finished;
                _pendingOutcome = BuildOutcomeLocked();
                result.Broadcast.Add(BuildStateEnvelopeLocked(command.Events));
                result.Broadcast.Add(new ServerEnvelope
                {
                    Type = MessageTypes.RoomState,
                    Seq = ++_seq,
                    ServerTimeMs = _clock(),
                    Room = BuildRoomSnapshotLocked()
                });
            }
            else
            {
                _turnDeadlineMs = _clock() + _turnTimeoutMs;
                result.Broadcast.Add(BuildStateEnvelopeLocked(command.Events));
            }

            result.Accepted = true;
            return result;
        }

        private ServerEnvelope BuildStateEnvelopeLocked(System.Collections.Generic.IEnumerable<GameEvent> events)
        {
            return new ServerEnvelope
            {
                Type = MessageTypes.GameStateMsg,
                Seq = ++_seq,
                ServerTimeMs = _clock(),
                State = GameSnapshot.From(_game.State, _game.LegalMoves(_game.State.ActiveSeat)),
                Events = events == null ? Array.Empty<GameEvent>() : System.Linq.Enumerable.ToArray(events),
                TurnDeadlineMs = RemainingMsLocked(),
                Die = _game.State.LastDie,
                Seat = _game.State.ActiveSeat
            };
        }

        private int RemainingMsLocked()
        {
            if (_status != RoomStatus.Playing)
            {
                return 0;
            }

            long remaining = _turnDeadlineMs - _clock();
            return remaining <= 0 ? 0 : (int)remaining;
        }

        private static string MapStatus(CommandStatus status)
        {
            switch (status)
            {
                case CommandStatus.InvalidTurn:
                    return ErrorCodes.NotYourTurn;
                case CommandStatus.InvalidPhase:
                    return ErrorCodes.WrongPhase;
                case CommandStatus.InvalidToken:
                    return ErrorCodes.IllegalMove;
                case CommandStatus.GameFinished:
                    return ErrorCodes.MatchFinished;
                default:
                    return ErrorCodes.InvalidPayload;
            }
        }

        private ServerEnvelope Reject(string accountId, string code, string message, string commandId)
        {
            return new ServerEnvelope
            {
                Type = MessageTypes.CommandRejected,
                Seq = ++_seq,
                ServerTimeMs = _clock(),
                ErrorCode = code,
                Error = message,
                CommandId = commandId
            };
        }

        private static string SanitizeEmote(string emoteId)
        {
            if (string.IsNullOrWhiteSpace(emoteId) || emoteId.Length > 24)
            {
                return "emote_01";
            }

            foreach (char c in emoteId)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return "emote_01";
                }
            }

            return emoteId;
        }

        // ---------------------------------------------------------------- connection

        public List<ServerEnvelope> Attach(string accountId)
        {
            lock (_gate)
            {
                RoomPlayer player = _players.FirstOrDefault(p => p.AccountId == accountId);
                if (player == null)
                {
                    return new List<ServerEnvelope>();
                }

                player.Connected = true;
                player.DisconnectedAtMs = 0;
                player.AutoPilot = player.IsBot;
                player.Timeouts = 0;
                _lastActivityMs = _clock();

                var envelopes = new List<ServerEnvelope>
                {
                    new ServerEnvelope
                    {
                        Type = MessageTypes.RoomState,
                        Seq = ++_seq,
                        ServerTimeMs = _clock(),
                        Room = BuildRoomSnapshotLocked()
                    }
                };

                if (_game != null && _status != RoomStatus.Lobby)
                {
                    envelopes.Add(BuildStateEnvelopeLocked(Array.Empty<GameEvent>()));
                }

                return envelopes;
            }
        }

        public List<ServerEnvelope> Detach(string accountId)
        {
            lock (_gate)
            {
                RoomPlayer player = _players.FirstOrDefault(p => p.AccountId == accountId);
                if (player == null || player.IsBot)
                {
                    return new List<ServerEnvelope>();
                }

                player.Connected = false;
                player.DisconnectedAtMs = _clock();
                _lastActivityMs = _clock();

                return new List<ServerEnvelope>
                {
                    new ServerEnvelope
                    {
                        Type = MessageTypes.PlayerConnection,
                        Seq = ++_seq,
                        ServerTimeMs = _clock(),
                        Seat = player.Seat,
                        DisplayName = player.DisplayName,
                        Die = 0,
                        Room = BuildRoomSnapshotLocked()
                    }
                };
            }
        }

        // ---------------------------------------------------------------- ticking

        /// <summary>
        /// Advances time-based behaviour: auto-plays the active seat after the turn
        /// deadline or when its player is disconnected past the grace period. This is what
        /// keeps a four-player match moving when someone puts the phone down.
        /// </summary>
        public List<ServerEnvelope> Tick()
        {
            lock (_gate)
            {
                var envelopes = new List<ServerEnvelope>();
                if (_status != RoomStatus.Playing || _game == null || _game.State.IsOver)
                {
                    return envelopes;
                }

                long now = _clock();
                RoomPlayer active = _players.FirstOrDefault(p => p.Seat == _game.State.ActiveSeat);
                if (active == null)
                {
                    return envelopes;
                }

                bool turnExpired = now >= _turnDeadlineMs;
                bool disconnectedTooLong = !active.Connected && active.DisconnectedAtMs > 0 && now - active.DisconnectedAtMs >= _graceMs;

                if (!active.AutoPilot && !turnExpired && !disconnectedTooLong)
                {
                    return envelopes;
                }

                if (disconnectedTooLong)
                {
                    active.AutoPilot = true;
                }

                if (turnExpired)
                {
                    active.Timeouts++;
                    if (active.Timeouts >= 2 && !active.Connected)
                    {
                        active.AutoPilot = true;
                    }
                }

                CommandResult autoRoll = _game.Roll(active.Seat, "auto-roll:" + _game.State.Version);
                var events = new List<GameEvent>(autoRoll.Events);

                if (_game.State.Phase == GamePhase.AwaitingMove)
                {
                    MoveOption move = LudoAi.ChooseMove(_game);
                    if (move != null)
                    {
                        CommandResult autoMove = _game.MoveToken(active.Seat, move.TokenIndex, "auto-move:" + _game.State.Version);
                        events.AddRange(autoMove.Events);
                    }
                }

                foreach (GameEvent evt in events)
                {
                    if (evt.Kind == GameEventKind.DiceRolled && evt.Die == 6 && evt.Seat >= 0 && evt.Seat < _sixesBySeat.Length)
                    {
                        _sixesBySeat[evt.Seat]++;
                    }
                }

                if (_game.State.IsOver)
                {
                    _status = RoomStatus.Finished;
                    _pendingOutcome = BuildOutcomeLocked();
                }
                else
                {
                    _turnDeadlineMs = _clock() + _turnTimeoutMs;
                }

                envelopes.Add(BuildStateEnvelopeLocked(events.ToArray()));
                if (_game.State.IsOver)
                {
                    envelopes.Add(new ServerEnvelope
                    {
                        Type = MessageTypes.RoomState,
                        Seq = ++_seq,
                        ServerTimeMs = _clock(),
                        Room = BuildRoomSnapshotLocked()
                    });
                }

                _lastActivityMs = now;
                return envelopes;
            }
        }

        /// <summary>Returns and clears the settlement payload exactly once.</summary>
        public MatchOutcome DrainOutcome()
        {
            lock (_gate)
            {
                MatchOutcome outcome = _pendingOutcome;
                _pendingOutcome = null;
                return outcome;
            }
        }

        private MatchOutcome BuildOutcomeLocked()
        {
            var seats = new List<MatchSeatOutcome>();
            var finished = _game.State.FinishOrder.ToList();
            var unfinished = _players
                .Where(p => !finished.Contains(p.Seat))
                .OrderByDescending(p => _game.State.Seats[p.Seat].TokensHome())
                .ThenBy(p => p.Seat)
                .Select(p => p.Seat)
                .ToList();

            var ordered = finished.Concat(unfinished).ToList();

            foreach (RoomPlayer player in _players)
            {
                int rank = ordered.IndexOf(player.Seat) + 1;
                seats.Add(new MatchSeatOutcome
                {
                    AccountId = player.IsBot ? null : player.AccountId,
                    DisplayName = player.DisplayName,
                    Seat = player.Seat,
                    Color = (int)_game.State.Seats[player.Seat].Color,
                    IsBot = player.IsBot,
                    Rank = rank,
                    TokensHome = _game.State.Seats[player.Seat].TokensHome(),
                    SixesRolled = _sixesBySeat[player.Seat],
                    Disconnected = !player.Connected && !player.IsBot
                });
            }

            int winnerSeat = _game.State.WinnerSeat;
            string winnerAccountId = winnerSeat >= 0 && winnerSeat < _players.Count && !_players[winnerSeat].IsBot
                ? _players[winnerSeat].AccountId
                : null;

            return new MatchOutcome
            {
                MatchId = MatchId,
                RoomCode = Code,
                SeatCount = SeatCount,
                EntryFee = EntryFee,
                WinnerAccountId = winnerAccountId,
                Aborted = false,
                Seats = seats
            };
        }

        /// <summary>
        /// Aborts a match that never completed (server shutdown, everyone gone). Produces a
        /// refundable outcome so coins charged as entry fees go back to the players.
        /// </summary>
        public MatchOutcome Abort()
        {
            lock (_gate)
            {
                if (_status == RoomStatus.Finished)
                {
                    return null;
                }

                _status = RoomStatus.Finished;
                var outcome = BuildOutcomeLocked();
                if (outcome != null)
                {
                    outcome.Aborted = true;
                    outcome.WinnerAccountId = null;
                }

                return outcome;
            }
        }

        public bool ShouldBeCleanedUp(long nowMs, int idleTimeoutMinutes)
        {
            lock (_gate)
            {
                if (_status == RoomStatus.Lobby)
                {
                    return _players.Count == 0 || nowMs - _lastActivityMs > idleTimeoutMinutes * 60_000L;
                }

                bool anyConnected = _players.Any(p => p.Connected && !p.IsBot);
                if (!anyConnected && nowMs - _lastActivityMs > idleTimeoutMinutes * 60_000L)
                {
                    return true;
                }

                return _status == RoomStatus.Finished && nowMs - _lastActivityMs > 120_000L;
            }
        }

        private static string NormalizeName(string name, int seat)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Player " + seat;
            }

            string trimmed = name.Trim();
            return trimmed.Length > 16 ? trimmed.Substring(0, 16) : trimmed;
        }
    }
}
