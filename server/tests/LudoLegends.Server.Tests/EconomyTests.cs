using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Server.Config;
using LudoLegends.Server.Data;
using LudoLegends.Server.Economy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LudoLegends.Server.Tests
{
    public class CoinServiceTests
    {
        [Fact]
        public async Task New_wallet_starts_with_configured_coins_and_a_ledger_row()
        {
            using var harness = new TestDatabase();
            ServerOptions options = TestOptions.Create();
            var coins = new CoinService(harness.Shared, options, NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", null);

            long balance = await coins.GetBalanceAsync("a1", CancellationToken.None);

            Assert.Equal(500, balance);

            using GameDbContext db = harness.NewContext();
            CoinTransaction row = await db.CoinTransactions.SingleAsync(t => t.AccountId == "a1");
            Assert.Equal("welcome_bonus", row.Reason);
            Assert.Equal(500, row.BalanceAfter);
        }

        [Fact]
        public async Task Credit_with_the_same_key_only_applies_once()
        {
            using var harness = new TestDatabase();
            var coins = new CoinService(harness.Shared, TestOptions.Create(), NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", 0);

            LedgerResult first = await coins.CreditAsync("a1", 100, "win", "prize:match1:a1", "match1", CancellationToken.None);
            LedgerResult second = await coins.CreditAsync("a1", 100, "win", "prize:match1:a1", "match1", CancellationToken.None);
            LedgerResult third = await coins.CreditAsync("a1", 100, "win", "prize:match1:a1", "match1", CancellationToken.None);

            Assert.True(first.Applied);
            Assert.True(second.Duplicate);
            Assert.True(third.Duplicate);
            Assert.Equal(100, await coins.GetBalanceAsync("a1", CancellationToken.None));

            using GameDbContext db = harness.NewContext();
            Assert.Equal(1, await db.CoinTransactions.CountAsync(t => t.AccountId == "a1"));
        }

        [Fact]
        public async Task Debit_beyond_the_balance_is_rejected_and_changes_nothing()
        {
            using var harness = new TestDatabase();
            var coins = new CoinService(harness.Shared, TestOptions.Create(), NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", 40);

            LedgerResult result = await coins.DebitAsync("a1", 100, "entry", "entry:m1:a1", "m1", CancellationToken.None);

            Assert.False(result.Applied);
            Assert.True(result.InsufficientFunds);
            Assert.Equal(40, await coins.GetBalanceAsync("a1", CancellationToken.None));
        }

        [Fact]
        public async Task Repeated_identical_claims_never_double_credit()
        {
            using var harness = new TestDatabase();
            var coins = new CoinService(harness.Shared, TestOptions.Create(), NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", 0);

            for (int i = 0; i < 10; i++)
            {
                await coins.CreditAsync("a1", 25, "daily_reward", "daily:2026-01-01", null, CancellationToken.None);
            }

            Assert.Equal(25, await coins.GetBalanceAsync("a1", CancellationToken.None));
        }

        [Fact]
        public async Task Daily_reward_can_only_be_claimed_once_per_day()
        {
            using var harness = new TestDatabase();
            var coins = new CoinService(harness.Shared, TestOptions.Create(), NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", 0);

            DailyStatus first = await coins.ClaimDailyAsync("a1", CancellationToken.None);
            Assert.True(first.JustClaimed);
            Assert.Equal(50, first.Balance);
            Assert.Equal(1, first.NextStreakDay);

            DailyStatus second = await coins.ClaimDailyAsync("a1", CancellationToken.None);
            Assert.False(second.JustClaimed);
            Assert.NotNull(second.Error);
            Assert.Equal(50, await coins.GetBalanceAsync("a1", CancellationToken.None));
        }

        [Fact]
        public async Task Daily_streak_advances_from_yesterday()
        {
            using var harness = new TestDatabase();
            ServerOptions options = TestOptions.Create();
            var coins = new CoinService(harness.Shared, options, NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", 0);

            using (GameDbContext db = harness.NewContext())
            {
                db.DailyClaims.Add(new DailyClaim
                {
                    Id = "c1",
                    AccountId = "a1",
                    ClaimDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"),
                    StreakDay = 2,
                    Coins = 75,
                    CreatedUtc = DateTime.UtcNow.AddDays(-1)
                });
                await db.SaveChangesAsync();
            }

            DailyStatus status = await coins.ClaimDailyAsync("a1", CancellationToken.None);
            Assert.True(status.JustClaimed);
            Assert.Equal(3, status.NextStreakDay);
            Assert.Equal(100, status.Balance);
        }

        [Fact]
        public async Task Mission_claim_requires_progress_and_pays_only_once()
        {
            using var harness = new TestDatabase();
            var coins = new CoinService(harness.Shared, TestOptions.Create(), NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", 0);

            LedgerResult tooEarly = await coins.ClaimMissionAsync("a1", "d_play3", CancellationToken.None);
            Assert.NotNull(tooEarly.Error);

            using (GameDbContext db = harness.NewContext())
            {
                db.MissionProgress.Add(new MissionProgress
                {
                    Id = "m1",
                    AccountId = "a1",
                    MissionKey = "d_play3",
                    PeriodKey = CoinService.PeriodKey("daily", DateTime.UtcNow),
                    Progress = 3,
                    Claimed = false,
                    UpdatedUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            LedgerResult claimed = await coins.ClaimMissionAsync("a1", "d_play3", CancellationToken.None);
            Assert.True(claimed.Applied);
            Assert.Equal(50, claimed.Balance);

            LedgerResult again = await coins.ClaimMissionAsync("a1", "d_play3", CancellationToken.None);
            Assert.NotNull(again.Error);
            Assert.Equal(50, await coins.GetBalanceAsync("a1", CancellationToken.None));
        }

        [Fact]
        public async Task Shop_purchase_deducts_once_and_grants_the_item()
        {
            using var harness = new TestDatabase();
            var coins = new CoinService(harness.Shared, TestOptions.Create(), NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", 3000);

            (bool ok, string error, long balance) = await coins.PurchaseAsync("a1", "avatar_pack_1", CancellationToken.None);
            Assert.True(ok, error);
            Assert.Equal(1800, balance);

            (bool second, _, long afterSecond) = await coins.PurchaseAsync("a1", "avatar_pack_1", CancellationToken.None);
            Assert.False(second);
            Assert.Equal(1800, afterSecond);

            using GameDbContext db = harness.NewContext();
            Account account = await db.Accounts.SingleAsync(a => a.Id == "a1");
            Assert.Contains("avatar_pack_1", account.UnlockedItems);
        }

        [Fact]
        public async Task Shop_purchase_fails_without_enough_coins()
        {
            using var harness = new TestDatabase();
            var coins = new CoinService(harness.Shared, TestOptions.Create(), NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", 10);

            (bool ok, string error, long balance) = await coins.PurchaseAsync("a1", "theme_neon", CancellationToken.None);

            Assert.False(ok);
            Assert.Contains("coins", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(10, balance);
        }

        [Fact]
        public async Task Achievements_unlock_from_stats_and_pay_once()
        {
            using var harness = new TestDatabase();
            var coins = new CoinService(harness.Shared, TestOptions.Create(), NullLogger<CoinService>.Instance);
            AccountFactory.Create(harness.NewContext(), "a1", "One", 0);

            Account account = await harness.Shared.Accounts.SingleAsync(a => a.Id == "a1");
            account.Wins = 1;
            account.GamesPlayed = 1;
            await coins.UnlockAchievementsAsync(account, CancellationToken.None);
            await harness.Shared.SaveChangesAsync();

            List<AchievementStatus> list = await coins.GetAchievementsAsync("a1", CancellationToken.None);
            AchievementStatus firstWin = list.Single(a => a.Key == "first_win");
            Assert.True(firstWin.Unlocked);
            Assert.False(firstWin.Claimed);

            LedgerResult claim = await coins.ClaimAchievementAsync("a1", "first_win", CancellationToken.None);
            Assert.True(claim.Applied);
            Assert.Equal(100, claim.Balance);

            LedgerResult again = await coins.ClaimAchievementAsync("a1", "first_win", CancellationToken.None);
            Assert.NotNull(again.Error);
        }
    }

    public class MatchServiceTests
    {
        private static (TestDatabase harness, CoinService coins, MatchService matches, ServerOptions options) Setup()
        {
            ServerOptions options = TestOptions.Create();
            var harness = new TestDatabase();
            var coins = new CoinService(harness.Shared, options, NullLogger<CoinService>.Instance);
            var matches = new MatchService(harness.Shared, coins, options, NullLogger<MatchService>.Instance);
            return (harness, coins, matches, options);
        }

        [Fact]
        public async Task Settling_a_match_pays_the_pot_and_updates_stats()
        {
            var (harness, coins, matches, _) = Setup();
            using (harness)
            {
                AccountFactory.Create(harness.NewContext(), "p1", "Winner", 500);
                AccountFactory.Create(harness.NewContext(), "p2", "Loser", 500);

                await matches.ChargeEntryAsync("m1", "p1", 100, CancellationToken.None);
                await matches.ChargeEntryAsync("m1", "p2", 100, CancellationToken.None);

                await matches.RecordAsync(new MatchOutcome
                {
                    MatchId = "m1",
                    RoomCode = "ABCDEF",
                    SeatCount = 2,
                    EntryFee = 100,
                    WinnerAccountId = "p1",
                    Seats = new List<MatchSeatOutcome>
                    {
                        new MatchSeatOutcome { AccountId = "p1", DisplayName = "Winner", Seat = 0, Color = 0, Rank = 1, TokensHome = 4 },
                        new MatchSeatOutcome { AccountId = "p2", DisplayName = "Loser", Seat = 1, Color = 2, Rank = 2, TokensHome = 1 }
                    }
                }, CancellationToken.None);

                Assert.Equal(600, await coins.GetBalanceAsync("p1", CancellationToken.None));
                Assert.Equal(400, await coins.GetBalanceAsync("p2", CancellationToken.None));

                using GameDbContext db = harness.NewContext();
                Account winner = await db.Accounts.SingleAsync(a => a.Id == "p1");
                Account loser = await db.Accounts.SingleAsync(a => a.Id == "p2");

                Assert.Equal(1, winner.Wins);
                Assert.Equal(1, winner.GamesPlayed);
                Assert.Equal(25, winner.Trophies);
                Assert.Equal(80, winner.Xp);
                Assert.Equal(1, loser.Losses);
                Assert.Equal(0, loser.Trophies);
                Assert.Equal(20, loser.Xp);

                MatchRecord record = await db.Matches.SingleAsync(m => m.Id == "m1");
                Assert.Equal(1, record.Status);
                Assert.Equal(200, record.Pot);
            }
        }

        [Fact]
        public async Task Settling_the_same_match_twice_does_not_pay_twice()
        {
            var (harness, coins, matches, _) = Setup();
            using (harness)
            {
                AccountFactory.Create(harness.NewContext(), "p1", "Winner", 500);
                AccountFactory.Create(harness.NewContext(), "p2", "Loser", 500);

                var outcome = new MatchOutcome
                {
                    MatchId = "m1",
                    RoomCode = "ABCDEF",
                    SeatCount = 2,
                    EntryFee = 100,
                    WinnerAccountId = "p1",
                    Seats = new List<MatchSeatOutcome>
                    {
                        new MatchSeatOutcome { AccountId = "p1", Seat = 0, Color = 0, Rank = 1, TokensHome = 4 },
                        new MatchSeatOutcome { AccountId = "p2", Seat = 1, Color = 2, Rank = 2, TokensHome = 0 }
                    }
                };

                await matches.RecordAsync(outcome, CancellationToken.None);
                await matches.RecordAsync(outcome, CancellationToken.None);

                // No entry fee was charged in this test, so the winner ends with the pot added
                // exactly once (500 + 200), not twice.
                Assert.Equal(700, await coins.GetBalanceAsync("p1", CancellationToken.None));
                using GameDbContext db = harness.NewContext();
                Assert.Equal(1, await db.Matches.CountAsync());
                Assert.Equal(2, await db.MatchParticipants.CountAsync());
            }
        }

        [Fact]
        public async Task Aborted_match_refunds_the_entry_fee()
        {
            var (harness, coins, matches, _) = Setup();
            using (harness)
            {
                AccountFactory.Create(harness.NewContext(), "p1", "One", 500);
                await matches.ChargeEntryAsync("m2", "p1", 250, CancellationToken.None);
                Assert.Equal(250, await coins.GetBalanceAsync("p1", CancellationToken.None));

                await matches.RecordAsync(new MatchOutcome
                {
                    MatchId = "m2",
                    RoomCode = "ZZZZZZ",
                    SeatCount = 2,
                    EntryFee = 250,
                    Aborted = true,
                    Seats = new List<MatchSeatOutcome>
                    {
                        new MatchSeatOutcome { AccountId = "p1", Seat = 0, Color = 0 }
                    }
                }, CancellationToken.None);

                Assert.Equal(500, await coins.GetBalanceAsync("p1", CancellationToken.None));
            }
        }

        [Fact]
        public async Task Recovery_refunds_a_match_that_never_settled()
        {
            var (harness, coins, matches, _) = Setup();
            using (harness)
            {
                AccountFactory.Create(harness.NewContext(), "p1", "One", 500);
                await matches.ChargeEntryAsync("m3", "p1", 200, CancellationToken.None);

                await matches.BeginAsync(new MatchOutcome
                {
                    MatchId = "m3",
                    RoomCode = "RUN123",
                    SeatCount = 2,
                    EntryFee = 200,
                    Seats = new List<MatchSeatOutcome>
                    {
                        new MatchSeatOutcome { AccountId = "p1", Seat = 0, Color = 0 }
                    }
                }, CancellationToken.None);

                Assert.Equal(300, await coins.GetBalanceAsync("p1", CancellationToken.None));

                int recovered = await matches.RecoverStaleAsync(TimeSpan.FromSeconds(-1), CancellationToken.None);

                Assert.Equal(1, recovered);
                Assert.Equal(500, await coins.GetBalanceAsync("p1", CancellationToken.None));

                using GameDbContext db = harness.NewContext();
                MatchRecord record = await db.Matches.SingleAsync(m => m.Id == "m3");
                Assert.Equal(2, record.Status);

                // Running the recovery a second time must not refund again.
                Assert.Equal(0, await matches.RecoverStaleAsync(TimeSpan.FromSeconds(-1), CancellationToken.None));
                Assert.Equal(500, await coins.GetBalanceAsync("p1", CancellationToken.None));
            }
        }

        [Fact]
        public async Task Entry_fee_is_charged_once_per_match()
        {
            var (harness, coins, matches, _) = Setup();
            using (harness)
            {
                AccountFactory.Create(harness.NewContext(), "p1", "One", 500);

                await matches.ChargeEntryAsync("m4", "p1", 100, CancellationToken.None);
                await matches.ChargeEntryAsync("m4", "p1", 100, CancellationToken.None);

                Assert.Equal(400, await coins.GetBalanceAsync("p1", CancellationToken.None));
            }
        }
    }
}
