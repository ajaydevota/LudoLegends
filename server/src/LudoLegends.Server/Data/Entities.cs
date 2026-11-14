using System;
using System.Collections.Generic;

namespace LudoLegends.Server.Data
{
    /// <summary>
    /// A player account. Guests and Google accounts share one table; a guest can be
    /// upgraded in place when it links a Google subject, which preserves coins and stats.
    /// </summary>
    public sealed class Account
    {
        public string Id { get; set; }

        /// <summary>Short public code used for friend requests (no personal data).</summary>
        public string PlayerCode { get; set; }

        public string DisplayName { get; set; }

        /// <summary>Preset avatar id; the client renders it from its own atlas.</summary>
        public string AvatarId { get; set; } = "avatar_01";

        public bool IsGuest { get; set; }

        /// <summary>SHA-256 of the installation id. Raw device ids are never stored.</summary>
        public string DeviceHash { get; set; }

        /// <summary>Google account subject ("sub" claim).</summary>
        public string GoogleSubject { get; set; }

        /// <summary>Recovery email for Google accounts. Guests have none.</summary>
        public string Email { get; set; }

        public DateTime CreatedUtc { get; set; }

        public DateTime LastSeenUtc { get; set; }

        public DateTime? DeletedUtc { get; set; }

        public int Level { get; set; } = 1;

        public int Xp { get; set; }

        public int GamesPlayed { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public int Abandoned { get; set; }

        public int Trophies { get; set; }

        /// <summary>Permanently unlocked cosmetic item ids, comma separated.</summary>
        public string UnlockedItems { get; set; } = string.Empty;

        /// <summary>Removes ads once the client reports a verified purchase or coin unlock.</summary>
        public bool AdsRemoved { get; set; }
    }

    public sealed class Wallet
    {
        public string AccountId { get; set; }

        public long Balance { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>
    /// Append-only coin ledger. Every balance change writes exactly one row keyed by
    /// (AccountId, IdempotencyKey); the unique index makes duplicate rewards impossible
    /// even if a client retries a claim a hundred times.
    /// </summary>
    public sealed class CoinTransaction
    {
        public string Id { get; set; }

        public string AccountId { get; set; }

        public long Delta { get; set; }

        public long BalanceAfter { get; set; }

        public string Reason { get; set; }

        public string IdempotencyKey { get; set; }

        public string MatchId { get; set; }

        public DateTime CreatedUtc { get; set; }
    }

    public sealed class Friendship
    {
        public string Id { get; set; }

        public string RequesterId { get; set; }

        public string AddresseeId { get; set; }

        /// <summary>0 = pending, 1 = accepted, 2 = declined.</summary>
        public int Status { get; set; }

        public DateTime CreatedUtc { get; set; }

        public DateTime? RespondedUtc { get; set; }
    }

    public sealed class MatchRecord
    {
        public string Id { get; set; }

        public string RoomCode { get; set; }

        public int SeatCount { get; set; }

        public long EntryFee { get; set; }

        public long Pot { get; set; }

        public string WinnerAccountId { get; set; }

        /// <summary>0 = running, 1 = finished, 2 = aborted.</summary>
        public int Status { get; set; }

        public DateTime StartedUtc { get; set; }

        public DateTime? EndedUtc { get; set; }

        public List<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();
    }

    public sealed class MatchParticipant
    {
        public string Id { get; set; }

        public string MatchId { get; set; }

        public string AccountId { get; set; }

        public string DisplayName { get; set; }

        public int Seat { get; set; }

        public int Color { get; set; }

        public bool IsBot { get; set; }

        /// <summary>1-based finishing rank, 0 when the match did not finish.</summary>
        public int Rank { get; set; }

        public int TokensHome { get; set; }

        public int SixesRolled { get; set; }

        public long CoinsDelta { get; set; }

        public bool Disconnected { get; set; }
    }

    /// <summary>
    /// One row per claimed daily reward. The unique (AccountId, ClaimDate) index is what
    /// makes "claim once per day" enforceable regardless of client behaviour.
    /// </summary>
    public sealed class DailyClaim
    {
        public string Id { get; set; }

        public string AccountId { get; set; }

        /// <summary>UTC date as yyyy-MM-dd so the schema stays provider portable.</summary>
        public string ClaimDate { get; set; }

        public int StreakDay { get; set; }

        public long Coins { get; set; }

        public DateTime CreatedUtc { get; set; }
    }

    /// <summary>Progress for daily/weekly missions, keyed by period so it resets itself.</summary>
    public sealed class MissionProgress
    {
        public string Id { get; set; }

        public string AccountId { get; set; }

        public string MissionKey { get; set; }

        /// <summary>yyyy-MM-dd for daily missions, yyyy-Www for weekly missions.</summary>
        public string PeriodKey { get; set; }

        public int Progress { get; set; }

        public bool Claimed { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }

    public sealed class AchievementProgress
    {
        public string Id { get; set; }

        public string AccountId { get; set; }

        public string AchievementKey { get; set; }

        public DateTime UnlockedUtc { get; set; }

        public bool Claimed { get; set; }
    }

    /// <summary>One-time cosmetic / quality-of-life purchase paid with coins.</summary>
    public sealed class Purchase
    {
        public string Id { get; set; }

        public string AccountId { get; set; }

        public string ItemId { get; set; }

        public long CoinsSpent { get; set; }

        public DateTime CreatedUtc { get; set; }
    }

    public sealed class DeviceSession
    {
        public string Id { get; set; }

        public string AccountId { get; set; }

        /// <summary>SHA-256 of the refresh token; the token itself is never stored.</summary>
        public string TokenHash { get; set; }

        public DateTime CreatedUtc { get; set; }

        public DateTime ExpiresUtc { get; set; }
    }
}
