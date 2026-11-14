using System;
using System.Collections.Generic;

namespace LudoLegends.Core
{
    /// <summary>
    /// Player colours, in board order (clockwise starting top-left).
    /// The numeric value indexes into <see cref="BoardTopology"/>.
    /// </summary>
    public enum PlayerColor
    {
        Red = 0,
        Blue = 1,
        Green = 2,
        Yellow = 3
    }

    /// <summary>
    /// Authoritative phase of the game state machine.
    /// </summary>
    public enum GamePhase
    {
        /// <summary>The active seat must roll the die.</summary>
        AwaitingRoll = 0,

        /// <summary>A die value has been rolled and a token must be selected.</summary>
        AwaitingMove = 1,

        /// <summary>The game has ended (winner recorded).</summary>
        Finished = 2
    }

    /// <summary>
    /// Result of a command applied to the game. The server maps these to protocol errors.
    /// </summary>
    public enum CommandStatus
    {
        Ok = 0,

        /// <summary>The same commandId was already applied; nothing changed.</summary>
        Duplicate = 1,

        /// <summary>The command came from a seat that is not active.</summary>
        InvalidTurn = 2,

        /// <summary>The command does not match the current phase (e.g. rolling twice).</summary>
        InvalidPhase = 3,

        /// <summary>The selected token has no legal move with the current die value.</summary>
        InvalidToken = 4,

        /// <summary>A value was out of range (die not 1..6, unknown seat).</summary>
        InvalidValue = 5,

        /// <summary>The game is already over.</summary>
        GameFinished = 6,

        /// <summary>No such seat exists in this game.</summary>
        InvalidSeat = 7
    }

    /// <summary>
    /// Rule switches. Defaults are the standard Ludo rules used by the game.
    /// The server owns the rules; clients receive them read-only in the snapshot.
    /// </summary>
    public sealed class GameRules
    {
        public int TokensPerPlayer { get; set; } = 4;

        /// <summary>Three sixes in a row forfeits the turn.</summary>
        public int MaxConsecutiveSixes { get; set; } = 3;

        public bool ExtraTurnOnSix { get; set; } = true;
        public bool ExtraTurnOnCapture { get; set; } = true;
        public bool ExtraTurnOnHome { get; set; } = true;

        /// <summary>Two or more tokens of one player block opponents from passing and landing.</summary>
        public bool BlockingEnabled { get; set; } = true;

        /// <summary>A token must land exactly on home; overshooting is not allowed.</summary>
        public bool ExactRollToHome { get; set; } = true;

        public bool MustRollSixToLeaveBase { get; set; } = true;

        public static GameRules Standard()
        {
            return new GameRules();
        }

        public GameRules Clone()
        {
            return (GameRules)MemberwiseClone();
        }
    }

    /// <summary>
    /// Immutable description of a seat, supplied when a game starts.
    /// </summary>
    public sealed class SeatDefinition
    {
        public SeatDefinition()
        {
        }

        public SeatDefinition(string playerId, string displayName, PlayerColor color, bool isBot)
        {
            PlayerId = playerId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;
            Color = color;
            IsBot = isBot;
        }

        public string PlayerId { get; set; }
        public string DisplayName { get; set; }
        public PlayerColor Color { get; set; }
        public bool IsBot { get; set; }
    }

    /// <summary>
    /// Mutable per-seat state. Token values are progress values:
    /// -1 = in base, 0..51 = main ring, 52..56 = home column, 57 = home.
    /// </summary>
    public sealed class SeatState
    {
        public int Index { get; set; }
        public PlayerColor Color { get; set; }
        public string PlayerId { get; set; }
        public string DisplayName { get; set; }
        public bool IsBot { get; set; }
        public int[] Tokens { get; set; }

        /// <summary>True once all of this seat's tokens have reached home.</summary>
        public bool Finished { get; set; }

        /// <summary>1-based finishing position, 0 while still playing.</summary>
        public int Rank { get; set; }

        public int TokensHome()
        {
            int count = 0;
            for (int i = 0; i < Tokens.Length; i++)
            {
                if (Tokens[i] == BoardTopology.ProgressHome)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// Complete authoritative game state. Small enough (a few hundred bytes) to send
    /// to every client after every accepted command, which makes reconnection trivial.
    /// </summary>
    public sealed class GameState
    {
        /// <summary>Monotonic version, incremented on every accepted command.</summary>
        public int Version { get; set; }

        public GamePhase Phase { get; set; }

        /// <summary>Index into <see cref="Seats"/> of the seat that must act.</summary>
        public int ActiveSeat { get; set; }

        /// <summary>Last rolled die value, 0 when no roll is pending.</summary>
        public int LastDie { get; set; }

        /// <summary>Sixes rolled consecutively by the current seat.</summary>
        public int ConsecutiveSixes { get; set; }

        /// <summary>-1 while the game is running; otherwise the winning seat index.</summary>
        public int WinnerSeat { get; set; } = -1;

        public int FinishedCount { get; set; }

        public SeatState[] Seats { get; set; } = Array.Empty<SeatState>();

        /// <summary>Seat indexes in the order they finished.</summary>
        public List<int> FinishOrder { get; set; } = new List<int>();

        /// <summary>
        /// Recently applied command ids (bounded ring buffer). Used by the server to make
        /// retried commands idempotent, so a duplicate "roll" over a flaky network can
        /// never produce two dice results.
        /// </summary>
        public List<string> RecentCommands { get; set; } = new List<string>();

        public int SeatCount
        {
            get { return Seats == null ? 0 : Seats.Length; }
        }

        public bool IsOver
        {
            get { return Phase == GamePhase.Finished; }
        }
    }

    /// <summary>Event kinds emitted by the engine (serialized as strings).</summary>
    public static class GameEventKind
    {
        public const string DiceRolled = "diceRolled";
        public const string TokenMoved = "tokenMoved";
        public const string TokenCaptured = "tokenCaptured";
        public const string TokenHome = "tokenHome";
        public const string NoLegalMoves = "noLegalMoves";
        public const string TurnForfeited = "turnForfeited";
        public const string TurnChanged = "turnChanged";
        public const string PlayerFinished = "playerFinished";
        public const string GameFinished = "gameFinished";
    }

    /// <summary>
    /// A single authoritative event. Clients use these only to drive animations and
    /// sound; the snapshot they receive alongside is always the truth.
    /// </summary>
    public sealed class GameEvent
    {
        public string Kind { get; set; }
        public int Seat { get; set; }
        public int Die { get; set; }
        public int TokenIndex { get; set; } = -1;
        public int From { get; set; } = -1;
        public int To { get; set; } = -1;

        /// <summary>Absolute ring cells (0..51) covered on the main loop, for animation.</summary>
        public int[] TrackPath { get; set; } = Array.Empty<int>();

        /// <summary>Captured tokens as "seat:token" pairs, e.g. "2:1".</summary>
        public string[] Captured { get; set; } = Array.Empty<string>();

        public string Message { get; set; }
    }

    /// <summary>A legal move for a token, pre-computed by the engine.</summary>
    public sealed class MoveOption
    {
        public int TokenIndex { get; set; }

        /// <summary>Current progress (-1 when in base).</summary>
        public int From { get; set; }

        /// <summary>Progress after the move.</summary>
        public int Destination { get; set; }

        /// <summary>Absolute ring cells covered (empty when the move stays inside the home column).</summary>
        public int[] TrackPath { get; set; } = Array.Empty<int>();

        public bool LeavesBase { get; set; }
        public bool EntersHomeColumn { get; set; }
        public bool ReachesHome { get; set; }
        public bool Captures { get; set; }
    }

    /// <summary>Outcome of applying a command.</summary>
    public sealed class CommandResult
    {
        public CommandStatus Status { get; set; }
        public string Message { get; set; }
        public int Seat { get; set; }
        public int Die { get; set; }
        public int Version { get; set; }
        public List<GameEvent> Events { get; set; } = new List<GameEvent>();

        /// <summary>Token indexes that may be moved now (only populated after a roll).</summary>
        public List<int> LegalTokens { get; set; } = new List<int>();

        public bool Accepted
        {
            get { return Status == CommandStatus.Ok || Status == CommandStatus.Duplicate; }
        }
    }

    /// <summary>Source of die values. The server supplies a cryptographic implementation.</summary>
    public interface IRandomSource
    {
        /// <summary>Returns a value in the inclusive range 1..6.</summary>
        int NextDie();
    }
}
