using Microsoft.EntityFrameworkCore;

namespace LudoLegends.Server.Data
{
    /// <summary>
    /// Database context. Provider is switched in Program.cs (SQLite for development and
    /// tests, PostgreSQL for production). Table and column names are explicit so the
    /// generated schema matches docs/database-schema.md and the plain-SQL file.
    /// </summary>
    public sealed class GameDbContext : DbContext
    {
        public GameDbContext(DbContextOptions<GameDbContext> options)
            : base(options)
        {
        }

        public DbSet<Account> Accounts { get; set; }

        public DbSet<Wallet> Wallets { get; set; }

        public DbSet<CoinTransaction> CoinTransactions { get; set; }

        public DbSet<Friendship> Friendships { get; set; }

        public DbSet<MatchRecord> Matches { get; set; }

        public DbSet<MatchParticipant> MatchParticipants { get; set; }

        public DbSet<DailyClaim> DailyClaims { get; set; }

        public DbSet<MissionProgress> MissionProgress { get; set; }

        public DbSet<AchievementProgress> Achievements { get; set; }

        public DbSet<Purchase> Purchases { get; set; }

        public DbSet<DeviceSession> DeviceSessions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>(e =>
            {
                e.ToTable("accounts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.PlayerCode).HasColumnName("player_code").HasMaxLength(12).IsRequired();
                e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(24).IsRequired();
                e.Property(x => x.AvatarId).HasColumnName("avatar_id").HasMaxLength(32);
                e.Property(x => x.IsGuest).HasColumnName("is_guest");
                e.Property(x => x.DeviceHash).HasColumnName("device_hash").HasMaxLength(64);
                e.Property(x => x.GoogleSubject).HasColumnName("google_subject").HasMaxLength(64);
                e.Property(x => x.Email).HasColumnName("email").HasMaxLength(160);
                e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
                e.Property(x => x.LastSeenUtc).HasColumnName("last_seen_utc");
                e.Property(x => x.DeletedUtc).HasColumnName("deleted_utc");
                e.Property(x => x.Level).HasColumnName("level");
                e.Property(x => x.Xp).HasColumnName("xp");
                e.Property(x => x.GamesPlayed).HasColumnName("games_played");
                e.Property(x => x.Wins).HasColumnName("wins");
                e.Property(x => x.Losses).HasColumnName("losses");
                e.Property(x => x.Abandoned).HasColumnName("abandoned");
                e.Property(x => x.Trophies).HasColumnName("trophies");
                e.Property(x => x.UnlockedItems).HasColumnName("unlocked_items").HasMaxLength(512);
                e.Property(x => x.AdsRemoved).HasColumnName("ads_removed");

                e.HasIndex(x => x.PlayerCode).IsUnique();
                e.HasIndex(x => x.DeviceHash).IsUnique();
                e.HasIndex(x => x.GoogleSubject).IsUnique();
                e.HasIndex(x => x.Trophies);
            });

            modelBuilder.Entity<Wallet>(e =>
            {
                e.ToTable("wallets");
                e.HasKey(x => x.AccountId);
                e.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(40);
                e.Property(x => x.Balance).HasColumnName("balance");
                e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            });

            modelBuilder.Entity<CoinTransaction>(e =>
            {
                e.ToTable("coin_transactions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.Delta).HasColumnName("delta");
                e.Property(x => x.BalanceAfter).HasColumnName("balance_after");
                e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(64).IsRequired();
                e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120).IsRequired();
                e.Property(x => x.MatchId).HasColumnName("match_id").HasMaxLength(40);
                e.Property(x => x.CreatedUtc).HasColumnName("created_utc");

                // The single most important constraint in the economy: one row per
                // (account, idempotency key) means a reward can never be granted twice.
                e.HasIndex(x => new { x.AccountId, x.IdempotencyKey }).IsUnique();
                e.HasIndex(x => new { x.AccountId, x.CreatedUtc });
            });

            modelBuilder.Entity<Friendship>(e =>
            {
                e.ToTable("friendships");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.RequesterId).HasColumnName("requester_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.AddresseeId).HasColumnName("addressee_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
                e.Property(x => x.RespondedUtc).HasColumnName("responded_utc");

                e.HasIndex(x => new { x.RequesterId, x.AddresseeId }).IsUnique();
                e.HasIndex(x => x.AddresseeId);
            });

            modelBuilder.Entity<MatchRecord>(e =>
            {
                e.ToTable("matches");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.RoomCode).HasColumnName("room_code").HasMaxLength(12);
                e.Property(x => x.SeatCount).HasColumnName("seat_count");
                e.Property(x => x.EntryFee).HasColumnName("entry_fee");
                e.Property(x => x.Pot).HasColumnName("pot");
                e.Property(x => x.WinnerAccountId).HasColumnName("winner_account_id").HasMaxLength(40);
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.StartedUtc).HasColumnName("started_utc");
                e.Property(x => x.EndedUtc).HasColumnName("ended_utc");

                e.HasIndex(x => x.StartedUtc);
                e.HasIndex(x => x.WinnerAccountId);
            });

            modelBuilder.Entity<MatchParticipant>(e =>
            {
                e.ToTable("match_participants");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.MatchId).HasColumnName("match_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(40);
                e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(24);
                e.Property(x => x.Seat).HasColumnName("seat");
                e.Property(x => x.Color).HasColumnName("color");
                e.Property(x => x.IsBot).HasColumnName("is_bot");
                e.Property(x => x.Rank).HasColumnName("rank");
                e.Property(x => x.TokensHome).HasColumnName("tokens_home");
                e.Property(x => x.SixesRolled).HasColumnName("sixes_rolled");
                e.Property(x => x.CoinsDelta).HasColumnName("coins_delta");
                e.Property(x => x.Disconnected).HasColumnName("disconnected");

                e.HasIndex(x => x.MatchId);
                e.HasIndex(x => x.AccountId);
            });

            modelBuilder.Entity<DailyClaim>(e =>
            {
                e.ToTable("daily_claims");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.ClaimDate).HasColumnName("claim_date").HasMaxLength(10).IsRequired();
                e.Property(x => x.StreakDay).HasColumnName("streak_day");
                e.Property(x => x.Coins).HasColumnName("coins");
                e.Property(x => x.CreatedUtc).HasColumnName("created_utc");

                e.HasIndex(x => new { x.AccountId, x.ClaimDate }).IsUnique();
            });

            modelBuilder.Entity<MissionProgress>(e =>
            {
                e.ToTable("mission_progress");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.MissionKey).HasColumnName("mission_key").HasMaxLength(40).IsRequired();
                e.Property(x => x.PeriodKey).HasColumnName("period_key").HasMaxLength(12).IsRequired();
                e.Property(x => x.Progress).HasColumnName("progress");
                e.Property(x => x.Claimed).HasColumnName("claimed");
                e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");

                e.HasIndex(x => new { x.AccountId, x.MissionKey, x.PeriodKey }).IsUnique();
            });

            modelBuilder.Entity<AchievementProgress>(e =>
            {
                e.ToTable("achievements");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.AchievementKey).HasColumnName("achievement_key").HasMaxLength(40).IsRequired();
                e.Property(x => x.UnlockedUtc).HasColumnName("unlocked_utc");
                e.Property(x => x.Claimed).HasColumnName("claimed");

                e.HasIndex(x => new { x.AccountId, x.AchievementKey }).IsUnique();
            });

            modelBuilder.Entity<Purchase>(e =>
            {
                e.ToTable("purchases");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.ItemId).HasColumnName("item_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.CoinsSpent).HasColumnName("coins_spent");
                e.Property(x => x.CreatedUtc).HasColumnName("created_utc");

                e.HasIndex(x => new { x.AccountId, x.ItemId }).IsUnique();
            });

            modelBuilder.Entity<DeviceSession>(e =>
            {
                e.ToTable("device_sessions");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasColumnName("id").HasMaxLength(40);
                e.Property(x => x.AccountId).HasColumnName("account_id").HasMaxLength(40).IsRequired();
                e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
                e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
                e.Property(x => x.ExpiresUtc).HasColumnName("expires_utc");

                e.HasIndex(x => x.TokenHash).IsUnique();
                e.HasIndex(x => x.AccountId);
            });
        }
    }
}
