using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Server.Auth;
using LudoLegends.Server.Config;
using LudoLegends.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LudoLegends.Server.Economy
{
    public sealed class LedgerResult
    {
        public bool Applied { get; set; }
        public bool Duplicate { get; set; }
        public bool InsufficientFunds { get; set; }
        public long Balance { get; set; }
        public string Error { get; set; }
    }

    public sealed class MissionDefinition
    {
        public string Key { get; set; }
        public string Period { get; set; }
        public string Title { get; set; }
        public string Metric { get; set; }
        public int Target { get; set; }
        public long Reward { get; set; }
    }

    public sealed class AchievementDefinition
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Metric { get; set; }
        public int Threshold { get; set; }
        public long Reward { get; set; }
    }

    public sealed class ShopItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Kind { get; set; }
        public long PriceCoins { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// The coin economy. Every balance change is a ledger row with an idempotency key and
    /// a unique index behind it, so rewards cannot be doubled by retries, replays or
    /// concurrent requests. Balances are only ever read from the database; the client's
    /// number is a display value.
    ///
    /// Coins are a closed virtual currency with no cash value and no withdrawal path.
    /// </summary>
    public sealed class CoinService
    {
        public static readonly ShopItem[] Catalogue =
        {
            new ShopItem { Id = "avatar_pack_1", Title = "Coin Pirate avatar", Kind = "avatar", PriceCoins = 1200, Description = "Six extra profile avatars." },
            new ShopItem { Id = "theme_neon", Title = "Neon Nights board", Kind = "theme", PriceCoins = 2500, Description = "High-contrast board theme." },
            new ShopItem { Id = "theme_royal", Title = "Royal Carpet board", Kind = "theme", PriceCoins = 2500, Description = "Warm velvet board theme." },
            new ShopItem { Id = "remove_ads", Title = "Remove ads (coins)", Kind = "upgrade", PriceCoins = 5000, Description = "Permanently hides banner ads." }
        };

        public static readonly MissionDefinition[] Missions =
        {
            new MissionDefinition { Key = "d_play3", Period = "daily", Title = "Play 3 matches", Metric = "games", Target = 3, Reward = 50 },
            new MissionDefinition { Key = "d_win1", Period = "daily", Title = "Win 1 match", Metric = "wins", Target = 1, Reward = 100 },
            new MissionDefinition { Key = "d_six10", Period = "daily", Title = "Roll 10 sixes", Metric = "sixes", Target = 10, Reward = 30 },
            new MissionDefinition { Key = "w_play25", Period = "weekly", Title = "Play 25 matches", Metric = "games", Target = 25, Reward = 300 },
            new MissionDefinition { Key = "w_win10", Period = "weekly", Title = "Win 10 matches", Metric = "wins", Target = 10, Reward = 500 }
        };

        public static readonly AchievementDefinition[] Achievements =
        {
            new AchievementDefinition { Key = "first_win", Title = "First blood", Metric = "wins", Threshold = 1, Reward = 100 },
            new AchievementDefinition { Key = "wins_10", Title = "Ten victories", Metric = "wins", Threshold = 10, Reward = 500 },
            new AchievementDefinition { Key = "wins_100", Title = "Centurion", Metric = "wins", Threshold = 100, Reward = 5000 },
            new AchievementDefinition { Key = "games_100", Title = "Regular", Metric = "games", Threshold = 100, Reward = 1000 },
            new AchievementDefinition { Key = "level_10", Title = "Level 10", Metric = "level", Threshold = 10, Reward = 800 }
        };

        private readonly GameDbContext _db;
        private readonly ServerOptions _options;
        private readonly ILogger<CoinService> _log;

        public CoinService(GameDbContext db, ServerOptions options, ILogger<CoinService> log)
        {
            _db = db;
            _options = options;
            _log = log;
        }

        private static readonly long[] DefaultDailyLadder = { 50, 75, 100, 125, 150, 200, 300 };

        /// <summary>Configured daily ladder, falling back to the built-in seven day ladder.</summary>
        private long[] DailyLadder
        {
            get
            {
                long[] configured = _options.Economy.DailyRewards;
                return configured != null && configured.Length > 0 ? configured : DefaultDailyLadder;
            }
        }

        public static string PeriodKey(string period, DateTime utcNow)
        {
            if (period == "weekly")
            {
                // ISO week: yyyy-Www
                return utcNow.Year.ToString("D4", CultureInfo.InvariantCulture) + "-W" +
                       ISOWeek.GetWeekOfYear(utcNow).ToString("D2", CultureInfo.InvariantCulture);
            }

            return utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public async Task<long> GetBalanceAsync(string accountId, CancellationToken cancellationToken)
        {
            Wallet wallet = await EnsureWalletAsync(accountId, cancellationToken);
            return wallet.Balance;
        }

        /// <summary>
        /// Returns a *detached* snapshot of the wallet. Balances are mutated with set-based
        /// updates (so concurrent credits cannot lose each other), and EF's identity map
        /// would otherwise hand back a stale tracked copy.
        /// </summary>
        public async Task<Wallet> EnsureWalletAsync(string accountId, CancellationToken cancellationToken)
        {
            Wallet wallet = await _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.AccountId == accountId, cancellationToken);
            if (wallet != null)
            {
                return wallet;
            }

            return await InTransactionAsync(async () =>
            {
                Wallet existing = await _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.AccountId == accountId, cancellationToken);
                if (existing != null)
                {
                    return existing;
                }

                var created = new Wallet
                {
                    AccountId = accountId,
                    Balance = _options.Economy.StartingCoins,
                    UpdatedUtc = DateTime.UtcNow
                };
                _db.Wallets.Add(created);

                if (_options.Economy.StartingCoins > 0)
                {
                    _db.CoinTransactions.Add(new CoinTransaction
                    {
                        Id = AuthService.NewId(),
                        AccountId = accountId,
                        Delta = _options.Economy.StartingCoins,
                        BalanceAfter = _options.Economy.StartingCoins,
                        Reason = "welcome_bonus",
                        IdempotencyKey = "welcome:" + accountId,
                        CreatedUtc = DateTime.UtcNow
                    });
                }

                await _db.SaveChangesAsync(cancellationToken);

                // Detach so callers never see a stale tracked copy after set-based updates.
                _db.Entry(created).State = EntityState.Detached;
                created.Balance = _options.Economy.StartingCoins;
                return created;
            }, cancellationToken);
        }

        /// <summary>Adds coins. Idempotent per (accountId, idempotencyKey).</summary>
        public async Task<LedgerResult> CreditAsync(
            string accountId,
            long amount,
            string reason,
            string idempotencyKey,
            string matchId,
            CancellationToken cancellationToken)
        {
            if (amount <= 0)
            {
                return new LedgerResult { Error = "Credit amount must be positive." };
            }

            return await ApplyAsync(accountId, amount, reason, idempotencyKey, matchId, cancellationToken);
        }

        /// <summary>Removes coins, failing atomically when the balance is too low.</summary>
        public async Task<LedgerResult> DebitAsync(
            string accountId,
            long amount,
            string reason,
            string idempotencyKey,
            string matchId,
            CancellationToken cancellationToken)
        {
            if (amount <= 0)
            {
                return new LedgerResult { Error = "Debit amount must be positive." };
            }

            return await ApplyAsync(accountId, -amount, reason, idempotencyKey, matchId, cancellationToken);
        }

        private async Task<LedgerResult> ApplyAsync(
            string accountId,
            long delta,
            string reason,
            string idempotencyKey,
            string matchId,
            CancellationToken cancellationToken)
        {
            await EnsureWalletAsync(accountId, cancellationToken);

            try
            {
                return await InTransactionAsync(async () =>
                {
                    CoinTransaction existing = await _db.CoinTransactions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.AccountId == accountId && t.IdempotencyKey == idempotencyKey, cancellationToken);

                    if (existing != null)
                    {
                        return new LedgerResult { Duplicate = true, Balance = existing.BalanceAfter };
                    }

                    long balance;
                    if (delta > 0)
                    {
                        await _db.Wallets
                            .Where(w => w.AccountId == accountId)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(w => w.Balance, w => w.Balance + delta)
                                .SetProperty(w => w.UpdatedUtc, DateTime.UtcNow), cancellationToken);
                        balance = await _db.Wallets.Where(w => w.AccountId == accountId).Select(w => w.Balance).FirstAsync(cancellationToken);
                    }
                    else
                    {
                        long cost = -delta;
                        int affected = await _db.Wallets
                            .Where(w => w.AccountId == accountId && w.Balance >= cost)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(w => w.Balance, w => w.Balance - cost)
                                .SetProperty(w => w.UpdatedUtc, DateTime.UtcNow), cancellationToken);

                        if (affected == 0)
                        {
                            long current = await _db.Wallets.Where(w => w.AccountId == accountId).Select(w => w.Balance).FirstAsync(cancellationToken);
                            return new LedgerResult { InsufficientFunds = true, Balance = current, Error = "Not enough coins." };
                        }

                        balance = await _db.Wallets.Where(w => w.AccountId == accountId).Select(w => w.Balance).FirstAsync(cancellationToken);
                    }

                    var transaction = new CoinTransaction
                    {
                        Id = AuthService.NewId(),
                        AccountId = accountId,
                        Delta = delta,
                        BalanceAfter = balance,
                        Reason = reason,
                        IdempotencyKey = idempotencyKey,
                        MatchId = matchId,
                        CreatedUtc = DateTime.UtcNow
                    };
                    _db.CoinTransactions.Add(transaction);
                    await _db.SaveChangesAsync(cancellationToken);

                    return new LedgerResult { Applied = true, Balance = balance };
                }, cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // A concurrent request inserted the same idempotency key first. The whole
                // transaction rolled back, so returning "duplicate" is the correct answer.
                _db.ChangeTracker.Clear();
                _log.LogWarning(ex, "Duplicate ledger key {Key} for {AccountId}", idempotencyKey, accountId);
                CoinTransaction existing = await _db.CoinTransactions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.AccountId == accountId && t.IdempotencyKey == idempotencyKey, cancellationToken);
                return new LedgerResult { Duplicate = true, Balance = existing?.BalanceAfter ?? 0 };
            }
        }

        public async Task<DailyStatus> GetDailyStatusAsync(string accountId, CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string yesterday = now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            DailyClaim last = await _db.DailyClaims
                .AsNoTracking()
                .Where(c => c.AccountId == accountId)
                .OrderByDescending(c => c.ClaimDate)
                .FirstOrDefaultAsync(cancellationToken);

            bool claimedToday = last != null && last.ClaimDate == today;
            int nextStreak = 1;
            if (last != null && last.ClaimDate == yesterday)
            {
                nextStreak = last.StreakDay >= DailyLadder.Length ? 1 : last.StreakDay + 1;
            }

            long reward = DailyLadder[Math.Clamp(nextStreak - 1, 0, DailyLadder.Length - 1)];

            return new DailyStatus
            {
                ClaimedToday = claimedToday,
                NextStreakDay = claimedToday ? last.StreakDay : nextStreak,
                NextReward = claimedToday ? 0 : reward,
                Rewards = DailyLadder,
                Balance = await GetBalanceAsync(accountId, cancellationToken)
            };
        }

        public async Task<DailyStatus> ClaimDailyAsync(string accountId, CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string yesterday = now.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            DailyClaim yesterdayClaim = await _db.DailyClaims
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.AccountId == accountId && c.ClaimDate == yesterday, cancellationToken);

            int streakDay = 1;
            if (yesterdayClaim != null)
            {
                streakDay = yesterdayClaim.StreakDay >= DailyLadder.Length ? 1 : yesterdayClaim.StreakDay + 1;
            }

            long reward = DailyLadder[Math.Clamp(streakDay - 1, 0, DailyLadder.Length - 1)];

            return await InTransactionAsync(async () =>
            {
                bool alreadyClaimed = await _db.DailyClaims
                    .AnyAsync(c => c.AccountId == accountId && c.ClaimDate == today, cancellationToken);

                if (alreadyClaimed)
                {
                    DailyStatus status = await GetDailyStatusAsync(accountId, cancellationToken);
                    status.JustClaimed = false;
                    status.Error = "Daily reward already claimed today.";
                    return status;
                }

                LedgerResult credit = await CreditAsync(accountId, reward, "daily_reward", "daily:" + today, null, cancellationToken);
                if (!credit.Applied && !credit.Duplicate)
                {
                    return new DailyStatus { Error = credit.Error ?? "Could not grant reward." };
                }

                _db.DailyClaims.Add(new DailyClaim
                {
                    Id = AuthService.NewId(),
                    AccountId = accountId,
                    ClaimDate = today,
                    StreakDay = streakDay,
                    Coins = reward,
                    CreatedUtc = now
                });
                await _db.SaveChangesAsync(cancellationToken);

                return new DailyStatus
                {
                    ClaimedToday = true,
                    JustClaimed = true,
                    NextStreakDay = streakDay,
                    NextReward = 0,
                    Rewards = DailyLadder,
                    Balance = credit.Balance
                };
            }, cancellationToken);
        }

        public async Task<List<MissionStatus>> GetMissionsAsync(string accountId, CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;
            var result = new List<MissionStatus>();

            foreach (MissionDefinition definition in Missions)
            {
                string periodKey = PeriodKey(definition.Period, now);
                MissionProgress row = await _db.MissionProgress.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.AccountId == accountId && m.MissionKey == definition.Key && m.PeriodKey == periodKey, cancellationToken);

                result.Add(new MissionStatus
                {
                    Key = definition.Key,
                    Title = definition.Title,
                    Period = definition.Period,
                    Target = definition.Target,
                    Progress = row?.Progress ?? 0,
                    Reward = definition.Reward,
                    Claimed = row?.Claimed ?? false,
                    PeriodKey = periodKey
                });
            }

            return result;
        }

        public async Task<LedgerResult> ClaimMissionAsync(string accountId, string missionKey, CancellationToken cancellationToken)
        {
            MissionDefinition definition = Missions.FirstOrDefault(m => m.Key == missionKey);
            if (definition == null)
            {
                return new LedgerResult { Error = "Unknown mission." };
            }

            string periodKey = PeriodKey(definition.Period, DateTime.UtcNow);

            return await InTransactionAsync(async () =>
            {
                MissionProgress row = await _db.MissionProgress
                    .FirstOrDefaultAsync(m => m.AccountId == accountId && m.MissionKey == definition.Key && m.PeriodKey == periodKey, cancellationToken);

                if (row == null || row.Progress < definition.Target)
                {
                    return new LedgerResult { Error = "Mission is not complete yet." };
                }

                if (row.Claimed)
                {
                    return new LedgerResult { Error = "Mission reward already claimed." };
                }

                LedgerResult credit = await CreditAsync(accountId, definition.Reward, "mission:" + definition.Key,
                    "mission:" + definition.Key + ":" + periodKey, null, cancellationToken);

                row.Claimed = true;
                row.UpdatedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                return new LedgerResult { Applied = credit.Applied, Duplicate = credit.Duplicate, Balance = credit.Balance, Error = credit.Error };
            }, cancellationToken);
        }

        public async Task<List<AchievementStatus>> GetAchievementsAsync(string accountId, CancellationToken cancellationToken)
        {
            Account account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
            var rows = await _db.Achievements.AsNoTracking().Where(a => a.AccountId == accountId).ToListAsync(cancellationToken);

            var result = new List<AchievementStatus>();
            foreach (AchievementDefinition definition in Achievements)
            {
                AchievementProgress row = rows.FirstOrDefault(r => r.AchievementKey == definition.Key);
                int current = MetricValue(account, definition.Metric);

                result.Add(new AchievementStatus
                {
                    Key = definition.Key,
                    Title = definition.Title,
                    Threshold = definition.Threshold,
                    Current = current,
                    Reward = definition.Reward,
                    Unlocked = row != null,
                    Claimed = row?.Claimed ?? false
                });
            }

            return result;
        }

        public async Task<LedgerResult> ClaimAchievementAsync(string accountId, string achievementKey, CancellationToken cancellationToken)
        {
            AchievementDefinition definition = Achievements.FirstOrDefault(a => a.Key == achievementKey);
            if (definition == null)
            {
                return new LedgerResult { Error = "Unknown achievement." };
            }

            return await InTransactionAsync(async () =>
            {
                AchievementProgress row = await _db.Achievements
                    .FirstOrDefaultAsync(a => a.AccountId == accountId && a.AchievementKey == achievementKey, cancellationToken);

                if (row == null)
                {
                    return new LedgerResult { Error = "Achievement is not unlocked yet." };
                }

                if (row.Claimed)
                {
                    return new LedgerResult { Error = "Achievement reward already claimed." };
                }

                LedgerResult credit = await CreditAsync(accountId, definition.Reward, "achievement:" + achievementKey,
                    "achievement:" + achievementKey + ":" + accountId, null, cancellationToken);

                row.Claimed = true;
                await _db.SaveChangesAsync(cancellationToken);

                return new LedgerResult { Applied = credit.Applied, Duplicate = credit.Duplicate, Balance = credit.Balance, Error = credit.Error };
            }, cancellationToken);
        }

        public async Task<(bool ok, string error, long balance)> PurchaseAsync(string accountId, string itemId, CancellationToken cancellationToken)
        {
            ShopItem item = Catalogue.FirstOrDefault(i => i.Id == itemId);
            if (item == null)
            {
                return (false, "Unknown item.", 0);
            }

            return await InTransactionAsync(async () =>
            {
                bool owned = await _db.Purchases.AnyAsync(p => p.AccountId == accountId && p.ItemId == itemId, cancellationToken);
                if (owned)
                {
                    return (false, "Item already owned.", await GetBalanceAsync(accountId, cancellationToken));
                }

                LedgerResult debit = await DebitAsync(accountId, item.PriceCoins, "shop:" + itemId, "shop:" + itemId, null, cancellationToken);
                if (!debit.Applied)
                {
                    return (false, debit.InsufficientFunds ? "Not enough coins." : (debit.Error ?? "Purchase failed."), debit.Balance);
                }

                _db.Purchases.Add(new Purchase
                {
                    Id = AuthService.NewId(),
                    AccountId = accountId,
                    ItemId = itemId,
                    CoinsSpent = item.PriceCoins,
                    CreatedUtc = DateTime.UtcNow
                });

                Account account = await _db.Accounts.FirstAsync(a => a.Id == accountId, cancellationToken);
                List<string> items = account.UnlockedItems == null
                    ? new List<string>()
                    : account.UnlockedItems.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (!items.Contains(itemId))
                {
                    items.Add(itemId);
                }

                account.UnlockedItems = string.Join(",", items);
                if (itemId == "remove_ads")
                {
                    account.AdsRemoved = true;
                }

                await _db.SaveChangesAsync(cancellationToken);
                return (true, (string)null, debit.Balance);
            }, cancellationToken);
        }

        public async Task<List<TransactionDto>> GetTransactionsAsync(string accountId, int take, CancellationToken cancellationToken)
        {
            return await _db.CoinTransactions
                .AsNoTracking()
                .Where(t => t.AccountId == accountId)
                .OrderByDescending(t => t.CreatedUtc)
                .Take(Math.Clamp(take, 1, 100))
                .Select(t => new TransactionDto
                {
                    Id = t.Id,
                    Delta = t.Delta,
                    BalanceAfter = t.BalanceAfter,
                    Reason = t.Reason,
                    CreatedUtc = t.CreatedUtc
                })
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// Unlocks achievements whose threshold has been reached. Called after a match;
        /// runs inside the match transaction so stats and unlocks stay consistent.
        /// </summary>
        public async Task UnlockAchievementsAsync(Account account, CancellationToken cancellationToken)
        {
            var existing = await _db.Achievements
                .Where(a => a.AccountId == account.Id)
                .Select(a => a.AchievementKey)
                .ToListAsync(cancellationToken);

            foreach (AchievementDefinition definition in Achievements)
            {
                if (existing.Contains(definition.Key))
                {
                    continue;
                }

                if (MetricValue(account, definition.Metric) >= definition.Threshold)
                {
                    _db.Achievements.Add(new AchievementProgress
                    {
                        Id = AuthService.NewId(),
                        AccountId = account.Id,
                        AchievementKey = definition.Key,
                        UnlockedUtc = DateTime.UtcNow,
                        Claimed = false
                    });
                }
            }
        }

        private static int MetricValue(Account account, string metric)
        {
            if (account == null)
            {
                return 0;
            }

            switch (metric)
            {
                case "wins":
                    return account.Wins;
                case "games":
                    return account.GamesPlayed;
                case "level":
                    return account.Level;
                default:
                    return 0;
            }
        }

        private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
        {
            if (_db.Database.CurrentTransaction != null)
            {
                return await work();
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            T result = await work();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }

    public sealed class DailyStatus
    {
        public bool ClaimedToday { get; set; }
        public bool JustClaimed { get; set; }
        public int NextStreakDay { get; set; }
        public long NextReward { get; set; }
        public long[] Rewards { get; set; } = Array.Empty<long>();
        public long Balance { get; set; }
        public string Error { get; set; }
    }

    public sealed class MissionStatus
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Period { get; set; }
        public string PeriodKey { get; set; }
        public int Target { get; set; }
        public int Progress { get; set; }
        public long Reward { get; set; }
        public bool Claimed { get; set; }
    }

    public sealed class AchievementStatus
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public int Threshold { get; set; }
        public int Current { get; set; }
        public long Reward { get; set; }
        public bool Unlocked { get; set; }
        public bool Claimed { get; set; }
    }

    public sealed class TransactionDto
    {
        public string Id { get; set; }
        public long Delta { get; set; }
        public long BalanceAfter { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}
