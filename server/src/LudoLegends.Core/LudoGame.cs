using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace LudoLegends.Core
{
    /// <summary>
    /// The authoritative Ludo rules engine.
    ///
    /// Every mutation goes through <see cref="RollWithDie"/> or <see cref="MoveToken"/>,
    /// both of which validate the acting seat, the phase, the die value and the target
    /// token before touching state. The engine never reads a die value from a client in
    /// production: the server provides the bytes via <see cref="IRandomSource"/>.
    ///
    /// The engine is deliberately transport-agnostic and allocation-light. The server
    /// calls it under a per-room lock; the Unity client uses the very same class for
    /// offline games, which guarantees identical rules online and offline.
    /// </summary>
    public sealed class LudoGame
    {
        /// <summary>How many recent command ids are remembered for idempotency.</summary>
        public const int CommandWindow = 64;

        private readonly IRandomSource _random;

        public LudoGame(GameState state, GameRules rules, IRandomSource random)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _random = random;
        }

        public GameState State { get; }

        public GameRules Rules { get; }

        /// <summary>
        /// Creates a fresh game. Seat 0 moves first; the caller decides seat order.
        /// </summary>
        public static LudoGame Start(GameRules rules, IReadOnlyList<SeatDefinition> seats, IRandomSource random)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            if (seats == null || seats.Count < 2 || seats.Count > 4)
            {
                throw new ArgumentException("A game needs 2 to 4 seats.", nameof(seats));
            }

            var state = new GameState
            {
                Version = 1,
                Phase = GamePhase.AwaitingRoll,
                ActiveSeat = 0,
                LastDie = 0,
                ConsecutiveSixes = 0,
                WinnerSeat = -1,
                FinishedCount = 0,
                Seats = new SeatState[seats.Count],
                FinishOrder = new List<int>(),
                RecentCommands = new List<string>(CommandWindow)
            };

            for (int i = 0; i < seats.Count; i++)
            {
                SeatDefinition definition = seats[i];
                var tokens = new int[rules.TokensPerPlayer];
                for (int t = 0; t < tokens.Length; t++)
                {
                    tokens[t] = -1;
                }

                state.Seats[i] = new SeatState
                {
                    Index = i,
                    Color = definition.Color,
                    PlayerId = definition.PlayerId ?? string.Empty,
                    DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? "Player " + (i + 1) : definition.DisplayName,
                    IsBot = definition.IsBot,
                    Tokens = tokens,
                    Finished = false,
                    Rank = 0
                };
            }

            return new LudoGame(state, rules, random);
        }

        /// <summary>
        /// Rolls the die for the active seat using the injected random source.
        /// </summary>
        public CommandResult Roll(int seatIndex, string commandId)
        {
            if (_random == null)
            {
                return Error(seatIndex, CommandStatus.InvalidValue, "No random source configured.");
            }

            return RollWithDie(seatIndex, _random.NextDie(), commandId);
        }

        /// <summary>
        /// Rolls a specific value. Used by tests and by the offline client, which shares
        /// the engine but generates dice locally because it owns the whole match.
        /// </summary>
        public CommandResult RollWithDie(int seatIndex, int die, string commandId)
        {
            if (IsDuplicate(commandId))
            {
                return Duplicate(seatIndex);
            }

            if (State.Phase == GamePhase.Finished)
            {
                return Error(seatIndex, CommandStatus.GameFinished, "The game is already over.");
            }

            if (seatIndex < 0 || seatIndex >= State.SeatCount)
            {
                return Error(seatIndex, CommandStatus.InvalidSeat, "No such seat.");
            }

            if (seatIndex != State.ActiveSeat)
            {
                return Error(seatIndex, CommandStatus.InvalidTurn, "It is not this seat's turn.");
            }

            if (State.Phase != GamePhase.AwaitingRoll)
            {
                return Error(seatIndex, CommandStatus.InvalidPhase, "A token move is pending.");
            }

            if (die < 1 || die > 6)
            {
                return Error(seatIndex, CommandStatus.InvalidValue, "Die must be between 1 and 6.");
            }

            var result = new CommandResult { Seat = seatIndex, Die = die };
            Remember(commandId);

            State.LastDie = die;
            result.Events.Add(new GameEvent { Kind = GameEventKind.DiceRolled, Seat = seatIndex, Die = die });

            if (die == 6)
            {
                State.ConsecutiveSixes++;
            }
            else
            {
                State.ConsecutiveSixes = 0;
            }

            if (die == 6 && State.ConsecutiveSixes >= Rules.MaxConsecutiveSixes)
            {
                result.Events.Add(new GameEvent
                {
                    Kind = GameEventKind.TurnForfeited,
                    Seat = seatIndex,
                    Die = die,
                    Message = "Three sixes in a row - turn forfeited."
                });
                AdvanceTurn(result);
                return Commit(result, CommandStatus.Ok);
            }

            List<MoveOption> moves = ComputeMoves(seatIndex, die);
            if (moves.Count == 0)
            {
                result.Events.Add(new GameEvent
                {
                    Kind = GameEventKind.NoLegalMoves,
                    Seat = seatIndex,
                    Die = die,
                    Message = "No legal move with this roll."
                });
                AdvanceTurn(result);
                return Commit(result, CommandStatus.Ok);
            }

            State.Phase = GamePhase.AwaitingMove;
            for (int i = 0; i < moves.Count; i++)
            {
                result.LegalTokens.Add(moves[i].TokenIndex);
            }

            return Commit(result, CommandStatus.Ok);
        }

        /// <summary>
        /// Moves a token for the active seat using the pending die value.
        /// </summary>
        public CommandResult MoveToken(int seatIndex, int tokenIndex, string commandId)
        {
            if (IsDuplicate(commandId))
            {
                return Duplicate(seatIndex);
            }

            if (State.Phase == GamePhase.Finished)
            {
                return Error(seatIndex, CommandStatus.GameFinished, "The game is already over.");
            }

            if (seatIndex < 0 || seatIndex >= State.SeatCount)
            {
                return Error(seatIndex, CommandStatus.InvalidSeat, "No such seat.");
            }

            if (seatIndex != State.ActiveSeat)
            {
                return Error(seatIndex, CommandStatus.InvalidTurn, "It is not this seat's turn.");
            }

            if (State.Phase != GamePhase.AwaitingMove)
            {
                return Error(seatIndex, CommandStatus.InvalidPhase, "Roll the die first.");
            }

            List<MoveOption> moves = LegalMoves(seatIndex);
            MoveOption option = null;
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].TokenIndex == tokenIndex)
                {
                    option = moves[i];
                    break;
                }
            }

            if (option == null)
            {
                return Error(seatIndex, CommandStatus.InvalidToken, "That token cannot move with this roll.");
            }

            Remember(commandId);

            var result = new CommandResult { Seat = seatIndex, Die = State.LastDie };
            SeatState seat = State.Seats[seatIndex];
            int from = seat.Tokens[tokenIndex];
            int to = option.Destination;

            seat.Tokens[tokenIndex] = to;
            State.LastDie = 0;
            State.Phase = GamePhase.AwaitingRoll;

            var captured = new List<string>();
            int captureCount = 0;

            if (to <= BoardTopology.LastTrackProgress)
            {
                int destinationCell = BoardTopology.AbsoluteCell(seat.Color, to);
                if (!BoardTopology.IsSafeCell(destinationCell))
                {
                    for (int s = 0; s < State.SeatCount; s++)
                    {
                        if (s == seatIndex)
                        {
                            continue;
                        }

                        SeatState other = State.Seats[s];
                        for (int t = 0; t < other.Tokens.Length; t++)
                        {
                            int progress = other.Tokens[t];
                            if (progress < 0 || progress > BoardTopology.LastTrackProgress)
                            {
                                continue;
                            }

                            if (BoardTopology.AbsoluteCell(other.Color, progress) != destinationCell)
                            {
                                continue;
                            }

                            other.Tokens[t] = -1;
                            captured.Add(s + ":" + t);
                            captureCount++;
                            result.Events.Add(new GameEvent
                            {
                                Kind = GameEventKind.TokenCaptured,
                                Seat = s,
                                TokenIndex = t,
                                From = progress,
                                To = -1,
                                Message = "Token sent back to base."
                            });
                        }
                    }
                }
            }

            var moveEvent = new GameEvent
            {
                Kind = GameEventKind.TokenMoved,
                Seat = seatIndex,
                TokenIndex = tokenIndex,
                From = from,
                To = to,
                TrackPath = option.TrackPath,
                Captured = captured.ToArray()
            };
            result.Events.Add(moveEvent);

            bool reachedHome = to == BoardTopology.ProgressHome;
            if (reachedHome)
            {
                result.Events.Add(new GameEvent
                {
                    Kind = GameEventKind.TokenHome,
                    Seat = seatIndex,
                    TokenIndex = tokenIndex,
                    To = to
                });
            }

            bool justFinished = false;
            if (!seat.Finished && seat.TokensHome() == seat.Tokens.Length)
            {
                seat.Finished = true;
                justFinished = true;
                State.FinishedCount++;
                seat.Rank = State.FinishedCount;
                State.FinishOrder.Add(seatIndex);
                result.Events.Add(new GameEvent
                {
                    Kind = GameEventKind.PlayerFinished,
                    Seat = seatIndex,
                    Message = "All four tokens are home."
                });
            }

            // The game ends when only one player is left unfinished.
            if (State.FinishedCount >= State.SeatCount - 1)
            {
                if (State.WinnerSeat < 0 && State.FinishOrder.Count > 0)
                {
                    State.WinnerSeat = State.FinishOrder[0];
                }

                if (State.FinishedCount >= State.SeatCount - 1)
                {
                    State.Phase = GamePhase.Finished;
                    State.ActiveSeat = -1;
                    result.Events.Add(new GameEvent
                    {
                        Kind = GameEventKind.GameFinished,
                        Seat = State.WinnerSeat,
                        Message = "Game over."
                    });
                    return Commit(result, CommandStatus.Ok);
                }
            }

            bool extraTurn =
                (Rules.ExtraTurnOnSix && result.Die == 6) ||
                (Rules.ExtraTurnOnCapture && captureCount > 0) ||
                (Rules.ExtraTurnOnHome && reachedHome);

            if (extraTurn && !justFinished && State.Phase != GamePhase.Finished)
            {
                State.Phase = GamePhase.AwaitingRoll;
                State.LastDie = 0;
                result.Events.Add(new GameEvent
                {
                    Kind = GameEventKind.TurnChanged,
                    Seat = seatIndex,
                    Message = "Extra turn."
                });
                return Commit(result, CommandStatus.Ok);
            }

            AdvanceTurn(result);
            return Commit(result, CommandStatus.Ok);
        }

        /// <summary>
        /// All legal moves for a seat with the current pending die value.
        /// Returns an empty list when no die is pending.
        /// </summary>
        public List<MoveOption> LegalMoves(int seatIndex)
        {
            var options = new List<MoveOption>(4);
            if (seatIndex < 0 || seatIndex >= State.SeatCount)
            {
                return options;
            }

            if (State.Phase != GamePhase.AwaitingMove || State.LastDie < 1)
            {
                return options;
            }

            return ComputeMoves(seatIndex, State.LastDie);
        }

        /// <summary>
        /// Legal moves for a seat with a specific die value, ignoring the current phase.
        /// Used by the roll itself (which evaluates moves before entering AwaitingMove).
        /// </summary>
        private List<MoveOption> ComputeMoves(int seatIndex, int die)
        {
            var options = new List<MoveOption>(4);
            if (seatIndex < 0 || seatIndex >= State.SeatCount || die < 1 || die > 6)
            {
                return options;
            }

            SeatState seat = State.Seats[seatIndex];
            for (int token = 0; token < seat.Tokens.Length; token++)
            {
                MoveOption option = BuildMove(seat, token, die);
                if (option != null)
                {
                    options.Add(option);
                }
            }

            return options;
        }

        /// <summary>
        /// Builds the move for one token, or null when it is illegal.
        /// This is the single place where movement legality is decided.
        /// </summary>
        private MoveOption BuildMove(SeatState seat, int tokenIndex, int die)
        {
            int progress = seat.Tokens[tokenIndex];
            var option = new MoveOption { TokenIndex = tokenIndex, From = progress };

            if (progress < 0)
            {
                if (Rules.MustRollSixToLeaveBase && die != 6)
                {
                    return null;
                }

                int startCell = BoardTopology.AbsoluteCell(seat.Color, 0);
                if (IsBlockedByOpponent(seat.Index, startCell))
                {
                    return null;
                }

                option.Destination = 0;
                option.LeavesBase = true;
                option.TrackPath = new[] { startCell };
                option.Captures = CountCapturableAt(seat.Index, startCell) > 0;
                return option;
            }

            int target = progress + die;
            if (Rules.ExactRollToHome && target > BoardTopology.ProgressHome)
            {
                return null;
            }

            if (target > BoardTopology.ProgressHome)
            {
                target = BoardTopology.ProgressHome;
            }

            var path = new List<int>();
            int firstTrackStep = Math.Max(progress + 1, 0);
            for (int step = firstTrackStep; step <= target && step <= BoardTopology.LastTrackProgress; step++)
            {
                int cell = BoardTopology.AbsoluteCell(seat.Color, step);
                if (IsBlockedByOpponent(seat.Index, cell))
                {
                    return null;
                }
                path.Add(cell);
            }

            option.Destination = target;
            option.TrackPath = path.ToArray();
            option.EntersHomeColumn = progress <= BoardTopology.LastTrackProgress && target > BoardTopology.LastTrackProgress;
            option.ReachesHome = target == BoardTopology.ProgressHome;

            if (target <= BoardTopology.LastTrackProgress)
            {
                int destinationCell = BoardTopology.AbsoluteCell(seat.Color, target);
                option.Captures = !BoardTopology.IsSafeCell(destinationCell) &&
                                  CountCapturableAt(seat.Index, destinationCell) > 0;
            }

            return option;
        }

        /// <summary>Number of opponent tokens standing on a ring cell that could be captured.</summary>
        private int CountCapturableAt(int seatIndex, int absoluteCell)
        {
            int count = 0;
            for (int s = 0; s < State.SeatCount; s++)
            {
                if (s == seatIndex)
                {
                    continue;
                }

                SeatState other = State.Seats[s];
                for (int t = 0; t < other.Tokens.Length; t++)
                {
                    int progress = other.Tokens[t];
                    if (progress < 0 || progress > BoardTopology.LastTrackProgress)
                    {
                        continue;
                    }

                    if (BoardTopology.AbsoluteCell(other.Color, progress) == absoluteCell)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// True when an opposing stack of two or more tokens occupies the cell.
        /// Stacks block both passing and landing (standard Ludo rule).
        /// </summary>
        private bool IsBlockedByOpponent(int seatIndex, int absoluteCell)
        {
            if (!Rules.BlockingEnabled)
            {
                return false;
            }

            for (int s = 0; s < State.SeatCount; s++)
            {
                if (s == seatIndex)
                {
                    continue;
                }

                SeatState other = State.Seats[s];
                int onCell = 0;
                for (int t = 0; t < other.Tokens.Length; t++)
                {
                    int progress = other.Tokens[t];
                    if (progress < 0 || progress > BoardTopology.LastTrackProgress)
                    {
                        continue;
                    }

                    if (BoardTopology.AbsoluteCell(other.Color, progress) == absoluteCell)
                    {
                        onCell++;
                    }
                }

                if (onCell >= 2)
                {
                    return true;
                }
            }

            return false;
        }

        private void AdvanceTurn(CommandResult result)
        {
            int start = State.ActiveSeat < 0 ? 0 : State.ActiveSeat;
            for (int i = 1; i <= State.SeatCount; i++)
            {
                int next = (start + i) % State.SeatCount;
                if (State.Seats[next].Finished)
                {
                    continue;
                }

                State.ActiveSeat = next;
                State.Phase = GamePhase.AwaitingRoll;
                State.LastDie = 0;
                State.ConsecutiveSixes = 0;
                result.Events.Add(new GameEvent
                {
                    Kind = GameEventKind.TurnChanged,
                    Seat = next,
                    Message = "Turn passed."
                });
                return;
            }

            State.Phase = GamePhase.Finished;
            State.ActiveSeat = -1;
        }

        private bool IsDuplicate(string commandId)
        {
            if (string.IsNullOrEmpty(commandId))
            {
                return false;
            }

            for (int i = 0; i < State.RecentCommands.Count; i++)
            {
                if (string.Equals(State.RecentCommands[i], commandId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void Remember(string commandId)
        {
            if (string.IsNullOrEmpty(commandId))
            {
                return;
            }

            State.RecentCommands.Add(commandId);
            while (State.RecentCommands.Count > CommandWindow)
            {
                State.RecentCommands.RemoveAt(0);
            }
        }

        private CommandResult Duplicate(int seatIndex)
        {
            return new CommandResult
            {
                Status = CommandStatus.Duplicate,
                Seat = seatIndex,
                Message = "Command already applied.",
                Version = State.Version
            };
        }

        private CommandResult Error(int seatIndex, CommandStatus status, string message)
        {
            return new CommandResult
            {
                Status = status,
                Seat = seatIndex,
                Message = message,
                Version = State.Version
            };
        }

        private CommandResult Commit(CommandResult result, CommandStatus status)
        {
            State.Version++;
            result.Status = status;
            result.Version = State.Version;
            return result;
        }

        /// <summary>Creates a detached copy of the state (used for resync and persistence).</summary>
        public GameSnapshot Snapshot()
        {
            return GameSnapshot.From(State, LegalMoves(State.ActiveSeat));
        }
    }

    /// <summary>
    /// Cryptographically strong die source. Dice are generated on the server only; the
    /// client never supplies them (except in single-device offline games, where it owns
    /// the entire match and there is nobody to cheat).
    /// </summary>
    public sealed class CryptoRandomSource : IRandomSource
    {
        public int NextDie()
        {
            // Rejection sampling keeps the distribution uniform (256 is not a multiple of 6).
            var buffer = new byte[1];
            while (true)
            {
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(buffer);
                }

                int value = buffer[0];
                if (value < 252)
                {
                    return (value % 6) + 1;
                }
            }
        }
    }
}
