using System;
using System.Collections.Generic;

namespace LudoLegends.Core
{
    /// <summary>
    /// Wire protocol shared by the server and the Unity client.
    ///
    /// The transport is JSON over WebSocket. The server serializes with System.Text.Json
    /// (camelCase), the client with Newtonsoft.Json (camelCase) - so these are plain
    /// public fields with no attributes and no enum values, which keeps both
    /// serializers and IL2CPP happy.
    ///
    /// Design rule: clients send *intent* (roll, move this token). The server replies
    /// with a full authoritative <see cref="GameSnapshot"/> plus the events that led to
    /// it. A client never sends state, dice values or results.
    /// </summary>
    public static class MessageTypes
    {
        // ---- client -> server ----
        public const string Hello = "hello";
        public const string FindMatch = "findMatch";
        public const string CancelMatch = "cancelMatch";
        public const string CreateRoom = "createRoom";
        public const string JoinRoom = "joinRoom";
        public const string LeaveRoom = "leaveRoom";
        public const string StartGame = "startGame";
        public const string AddBot = "addBot";
        public const string Roll = "roll";
        public const string MoveToken = "moveToken";
        public const string Resync = "resync";
        public const string Ping = "ping";
        public const string SetReady = "setReady";
        public const string Emote = "emote";

        // ---- server -> client ----
        public const string Welcome = "welcome";
        public const string Pong = "pong";
        public const string Queued = "queued";
        public const string MatchFound = "matchFound";
        public const string RoomState = "roomState";
        public const string GameStateMsg = "gameState";
        public const string CommandRejected = "commandRejected";
        public const string Error = "error";
        public const string PlayerConnection = "playerConnection";
        public const string Rematch = "rematch";
        public const string MatchCancelled = "matchCancelled";
        public const string EmoteMsg = "emote";
    }

    /// <summary>
    /// Room lifecycle values carried in <see cref="RoomSnapshot.Status"/>. Kept as plain
    /// constants (not an enum) so the Unity client does not need the server assembly.
    /// </summary>
    public static class RoomStatusCodes
    {
        public const int Lobby = 0;
        public const int Playing = 1;
        public const int Finished = 2;
    }

    /// <summary>Stable error codes; the client maps these to localized strings.</summary>
    public static class ErrorCodes
    {
        public const string Unauthorized = "unauthorized";
        public const string InvalidPayload = "invalid_payload";
        public const string RateLimited = "rate_limited";
        public const string RoomNotFound = "room_not_found";
        public const string RoomFull = "room_full";
        public const string RoomAlreadyStarted = "room_already_started";
        public const string NotRoomHost = "not_room_host";
        public const string NotEnoughPlayers = "not_enough_players";
        public const string AlreadyInRoom = "already_in_room";
        public const string NotInRoom = "not_in_room";
        public const string InsufficientCoins = "insufficient_coins";
        public const string Internal = "internal_error";
        public const string NotImplementedInMode = "not_available";

        // Engine rejections, mirrored from CommandStatus. Clients switch on these codes,
        // so they must stay stable; the human-readable text travels in ServerEnvelope.Error.
        public const string NotYourTurn = "not_your_turn";
        public const string WrongPhase = "wrong_phase";
        public const string IllegalMove = "illegal_move";
        public const string MatchFinished = "match_finished";
    }

    /// <summary>
    /// Flat client message envelope. Optional fields are ignored unless the message type
    /// uses them; one type keeps the Unity parser and the server validator small.
    /// </summary>
    public sealed class ClientEnvelope
    {
        public string Type { get; set; }

        /// <summary>
        /// Client-generated idempotency key. The server deduplicates on it, so a retry
        /// after a network hiccup can never roll twice or move twice.
        /// </summary>
        public string CommandId { get; set; }

        public string RoomCode { get; set; }

        /// <summary>Desired seat count for matchmaking or room creation (2..4).</summary>
        public int SeatCount { get; set; }

        /// <summary>Desired table entry fee in coins (0 = free table).</summary>
        public long EntryFee { get; set; }

        /// <summary>Token index for a moveToken command.</summary>
        public int TokenIndex { get; set; }

        /// <summary>The game version the client believes is current (anti-stale-command).</summary>
        public int KnownVersion { get; set; }

        public bool Ready { get; set; }

        public string EmoteId { get; set; }
    }

    /// <summary>Server message envelope. Only the fields relevant to <see cref="Type"/> are set.</summary>
    public sealed class ServerEnvelope
    {
        public string Type { get; set; }

        /// <summary>Monotonic per-connection sequence number; clients can detect gaps.</summary>
        public long Seq { get; set; }

        public long ServerTimeMs { get; set; }

        public GameSnapshot State { get; set; }

        public RoomSnapshot Room { get; set; }

        public GameEvent[] Events { get; set; }

        public string Error { get; set; }

        public string ErrorCode { get; set; }

        /// <summary>Milliseconds left for the active seat before the server auto-plays.</summary>
        public int TurnDeadlineMs { get; set; }

        /// <summary>Rejected command id, echoed so the client can clear its optimistic UI.</summary>
        public string CommandId { get; set; }

        public int Seat { get; set; }

        public int Die { get; set; }

        public string EmoteId { get; set; }

        public string DisplayName { get; set; }

        /// <summary>Wallet balance for the welcome message, so the HUD is correct immediately.</summary>
        public long Balance { get; set; }
    }

    /// <summary>Serializable, transport-safe view of the authoritative game state.</summary>
    public sealed class GameSnapshot
    {
        public int Version { get; set; }
        public int Phase { get; set; }
        public int ActiveSeat { get; set; }
        public int LastDie { get; set; }
        public int WinnerSeat { get; set; }
        public int FinishedCount { get; set; }
        public SeatSnapshot[] Seats { get; set; }
        public int[] FinishOrder { get; set; }

        /// <summary>Tokens the active seat may move right now (empty unless Phase == AwaitingMove).</summary>
        public int[] LegalTokens { get; set; }

        public static GameSnapshot From(GameState state, IReadOnlyList<MoveOption> legalMoves)
        {
            if (state == null)
            {
                return null;
            }

            var seats = new SeatSnapshot[state.SeatCount];
            for (int i = 0; i < seats.Length; i++)
            {
                SeatState seat = state.Seats[i];
                seats[i] = new SeatSnapshot
                {
                    Index = seat.Index,
                    Color = (int)seat.Color,
                    PlayerId = seat.PlayerId,
                    DisplayName = seat.DisplayName,
                    IsBot = seat.IsBot,
                    Tokens = (int[])seat.Tokens.Clone(),
                    Finished = seat.Finished,
                    Rank = seat.Rank,
                    TokensHome = seat.TokensHome()
                };
            }

            var legal = new List<int>();
            if (legalMoves != null)
            {
                for (int i = 0; i < legalMoves.Count; i++)
                {
                    legal.Add(legalMoves[i].TokenIndex);
                }
            }

            return new GameSnapshot
            {
                Version = state.Version,
                Phase = (int)state.Phase,
                ActiveSeat = state.ActiveSeat,
                LastDie = state.LastDie,
                WinnerSeat = state.WinnerSeat,
                FinishedCount = state.FinishedCount,
                Seats = seats,
                FinishOrder = state.FinishOrder.ToArray(),
                LegalTokens = legal.ToArray()
            };
        }
    }

    public sealed class SeatSnapshot
    {
        public int Index { get; set; }
        public int Color { get; set; }
        public string PlayerId { get; set; }
        public string DisplayName { get; set; }
        public bool IsBot { get; set; }

        /// <summary>Progress per token: -1 base, 0..51 ring, 52..56 home column, 57 home.</summary>
        public int[] Tokens { get; set; }

        public bool Finished { get; set; }
        public int Rank { get; set; }
        public int TokensHome { get; set; }
    }

    /// <summary>Lobby state for a private or matchmade room.</summary>
    public sealed class RoomSnapshot
    {
        public string Code { get; set; }
        public string HostPlayerId { get; set; }
        public int SeatCount { get; set; }
        public int Status { get; set; }
        public long EntryFee { get; set; }
        public bool IsRanked { get; set; }
        public RoomMemberSnapshot[] Members { get; set; }
    }

    public sealed class RoomMemberSnapshot
    {
        public string PlayerId { get; set; }
        public string DisplayName { get; set; }
        public int Seat { get; set; }
        public bool Connected { get; set; }
        public int Level { get; set; }
        public bool IsBot { get; set; }
        public bool Ready { get; set; }
    }
}
