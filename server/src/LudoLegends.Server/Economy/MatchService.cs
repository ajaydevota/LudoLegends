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
    public sealed class MatchSeatOutcome
    {
        public string AccountId { get; set; }
        public string DisplayName { get; set; }
        public int Seat { get; set; }
        public int Color { get; set; }
        public bool IsBot { get; set; }
        public int Rank { get; set; }
        public int TokensHome { get; set; }
        public int SixesRolled { get; set; }
        public bool Disconnected { get; set; }
    }

    public sealed class MatchOutcome
    {
        public string MatchId { get; set; }
        public string RoomCode { get; set; }
        public int SeatCount { get; set; }
        public long EntryFee { get; set; }
        public string WinnerAccountId { get; set; }
        public bool Aborted { get; set; }
        public List<MatchSeatOutcome> Seats { get; set; } = new List<MatchSeatOutcome>();
    }

    /// <summary>
    /// Settles matches. The result always comes from the authoritative engine, never from
    /// a client message. Settlement is idempotent per match id, so a restart or a retried
    /// settlement can never pay the prize twice.
    /// </summary>
    public sealed class MatchService
    {
        private readonly GameDbContext _db;
        private readonly CoinService _coins;
        private readonly ServerOptions _options;
        private readonly ILogger<MatchService> _log;

        public MatchService(GameDbContext db, CoinService coins, ServerOptions options, ILogger<MatchService> log)
        {
            _db = db;
            _coins = coins;
            _options = options;
            _log = log;
        }

        /// <summary>Takes the table entry fee before play starts. All-or-nothing per seat.</summary>
        public async Task<LedgerResult> ChargeEntryAsync(string matchId, string accountId, long entryFee, CancellationToken cancellationToken)
        {
            if (entryFee <= 0)
            {
                return new LedgerResult { Applied = true, Balance = await _coins.GetBalanceAsync(accountId, cancellationToken) };
            }

            return await _coins.DebitAsync(accountId, entryFee, "table_entry", "entry:" + matchId + ":" + accountId, matchId, cancellationToken);
        }

        /// <summary>Refunds a table fee when a match is aborted before it ends.</summary>
        public async Task RefundEntryAsync(string matchId, string accountId, long entryFee, CancellationToken cancellationToken)
        {
            if (entryFee <= 0)
            {
                return;
            }

            await _coins.CreditAsync(accountId, entryFee, "table_refund", "refund:" + matchId + ":" + accountId, matchId, cancellationToken);
        }

        /// <summary>
        /// Writes the match result, prize, stats, mission progress and achievements in one
        /// transaction. Safe to call twice with the same match id.
        /// </summary>
        public async Task RecordAsync(MatchOutcome outcome, CancellationToken cancellationToken)
        {
            if (outcome == null || string.IsNullOrWhiteSpace(outcome.MatchId))
            {
                throw new ArgumentException("Match outcome requires an id.", nameof(outcome));
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            MatchRecord record = await _db.Matches
                .Include(m => m.Participants)
                .FirstOrDefaultAsync(m => m.Id == outcome.MatchId, cancellationToken);

            if (record != null && record.Status != 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                _log.LogInformation("Match {MatchId} already settled", outcome.MatchId);
                return;
            }

            if (record == null)
            {
                record = new MatchRecord
                {
                    Id = outcome.MatchId,
                    RoomCode = outcome.RoomCode,
                    SeatCount = outcome.SeatCount,
                    EntryFee = outcome.EntryFee,
                    Status = 0,
                    StartedUtc = DateTime.UtcNow,
                    Participants = new List<MatchParticipant>()
                };
                _db.Matches.Add(record);
            }
            else
            {
                // Started by BeginAsync: replace the placeholder participants with the
                // final results in the same transaction.
                record.RoomCode = outcome.RoomCode;
                record.SeatCount = outcome.SeatCount;
                record.EntryFee = outcome.EntryFee;
                _db.MatchParticipants.RemoveRange(record.Participants);
                record.Participants.Clear();
            }

            var humans = outcome.Seats.Where(s => !s.IsBot && !string.IsNullOrEmpty(s.AccountId)).ToList();
            long pot = outcome.EntryFee * humans.Count;

            record.Pot = pot;
            record.WinnerAccountId = outcome.WinnerAccountId;
            record.Status = outcome.Aborted ? 2 : 1;
            record.EndedUtc = DateTime.UtcNow;

            if (!outcome.Aborted)
            {
                foreach (MatchSeatOutcome seat in outcome.Seats)
                {
                    long delta = 0;
                    if (seat.IsBot || string.IsNullOrEmpty(seat.AccountId))
                    {
                        delta = 0;
                    }
                    else
                    {
                        delta -= outcome.EntryFee;
                        if (seat.AccountId == outcome.WinnerAccountId && pot > 0)
                        {
                            LedgerResult prize = await _coins.CreditAsync(
                                seat.AccountId,
                                pot,
                                "match_prize",
                                "prize:" + outcome.MatchId + ":" + seat.AccountId,
                                outcome.MatchId,
                                cancellationToken);

                            if (prize.Applied || prize.Duplicate)
                            {
                                delta += pot;
                            }
                        }

                        await UpdateAccountForResultAsync(seat, outcome, cancellationToken);
                    }

                    record.Participants.Add(new MatchParticipant
                    {
                        Id = AuthService.NewId(),
                        MatchId = outcome.MatchId,
                        AccountId = seat.AccountId,
                        DisplayName = seat.DisplayName ?? "Player",
                        Seat = seat.Seat,
                        Color = seat.Color,
                        IsBot = seat.IsBot,
                        Rank = seat.Rank,
                        TokensHome = seat.TokensHome,
                        SixesRolled = seat.SixesRolled,
                        CoinsDelta = delta,
                        Disconnected = seat.Disconnected
                    });
                }
            }
            else
            {
                foreach (MatchSeatOutcome seat in outcome.Seats)
                {
                    if (seat.IsBot || string.IsNullOrEmpty(seat.AccountId))
                    {
                        continue;
                    }

                    await RefundEntryAsync(outcome.MatchId, seat.AccountId, outcome.EntryFee, cancellationToken);

                    Account account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == seat.AccountId, cancellationToken);
                    if (account != null)
                    {
                        account.Abandoned++;
                        account.LastSeenUtc = DateTime.UtcNow;
                    }

                    record.Participants.Add(new MatchParticipant
                    {
                        Id = AuthService.NewId(),
                        MatchId = outcome.MatchId,
                        AccountId = seat.AccountId,
                        DisplayName = seat.DisplayName ?? "Player",
                        Seat = seat.Seat,
                        Color = seat.Color,
                        IsBot = seat.IsBot,
                        Rank = 0,
                        TokensHome = seat.TokensHome,
                        SixesRolled = seat.SixesRolled,
                        CoinsDelta = 0,
                        Disconnected = seat.Disconnected
                    });
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _log.LogInformation("Settled match {MatchId} ({Seats} seats, winner {Winner})",
                outcome.MatchId, outcome.SeatCount, outcome.WinnerAccountId ?? "none");
        }

        private async Task UpdateAccountForResultAsync(MatchSeatOutcome seat, MatchOutcome outcome, CancellationToken cancellationToken)
        {
            Account account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == seat.AccountId && a.DeletedUtc == null, cancellationToken);
            if (account == null)
            {
                return;
            }

            bool won = seat.AccountId == outcome.WinnerAccountId;

            account.GamesPlayed++;
            if (won)
            {
                account.Wins++;
                account.Trophies += _options.Economy.TrophiesPerWin;
                account.Xp += _options.Economy.XpPerGame + _options.Economy.XpPerWin;
            }
            else
            {
                account.Losses++;
                account.Trophies = Math.Max(0, account.Trophies - _options.Economy.TrophiesPerLoss);
                account.Xp += _options.Economy.XpPerGame;
            }

            if (seat.Disconnected)
            {
                account.Abandoned++;
            }

            int newLevel = 1 + (account.Xp / Math.Max(1, _options.Economy.XpPerLevel));
            account.Level = Math.Clamp(newLevel, 1, 100);
            account.LastSeenUtc = DateTime.UtcNow;

            await BumpMissionProgressAsync(account.Id, "games", 1, cancellationToken);
            if (won)
            {
                await BumpMissionProgressAsync(account.Id, "wins", 1, cancellationToken);
            }

            if (seat.SixesRolled > 0)
            {
                await BumpMissionProgressAsync(account.Id, "sixes", seat.SixesRolled, cancellationToken);
            }

            await _coins.UnlockAchievementsAsync(account, cancellationToken);
        }

        /// <summary>
        /// Increments mission counters for the current daily and weekly periods. Progress
        /// lives on the server, so a client cannot claim a mission it did not earn.
        /// </summary>
        private async Task BumpMissionProgressAsync(string accountId, string metric, int amount, CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;

            foreach (MissionDefinition definition in CoinService.Missions.Where(m => m.Metric == metric))
            {
                string periodKey = CoinService.PeriodKey(definition.Period, now);

                MissionProgress row = await _db.MissionProgress.FirstOrDefaultAsync(
                    m => m.AccountId == accountId && m.MissionKey == definition.Key && m.PeriodKey == periodKey,
                    cancellationToken);

                if (row == null)
                {
                    row = new MissionProgress
                    {
                        Id = AuthService.NewId(),
                        AccountId = accountId,
                        MissionKey = definition.Key,
                        PeriodKey = periodKey,
                        Progress = 0,
                        Claimed = false,
                        UpdatedUtc = now
                    };
                    _db.MissionProgress.Add(row);
                }

                row.Progress = Math.Min(row.Progress + amount, definition.Target);
                row.UpdatedUtc = now;
            }
        }

        /// <summary>
        /// Records that a match has started (status 0) before any play happens, so a crash
        /// mid-match leaves evidence behind. <see cref="RecoverStaleAsync"/> then refunds
        /// the entry fees of matches that never settled.
        /// </summary>
        public async Task BeginAsync(MatchOutcome outcome, CancellationToken cancellationToken)
        {
            if (outcome == null || string.IsNullOrWhiteSpace(outcome.MatchId))
            {
                return;
            }

            bool exists = await _db.Matches.AnyAsync(m => m.Id == outcome.MatchId, cancellationToken);
            if (exists)
            {
                return;
            }

            var record = new MatchRecord
            {
                Id = outcome.MatchId,
                RoomCode = outcome.RoomCode,
                SeatCount = outcome.SeatCount,
                EntryFee = outcome.EntryFee,
                Pot = 0,
                Status = 0,
                StartedUtc = DateTime.UtcNow,
                Participants = outcome.Seats.Select(seat => new MatchParticipant
                {
                    Id = AuthService.NewId(),
                    MatchId = outcome.MatchId,
                    AccountId = seat.AccountId,
                    DisplayName = seat.DisplayName ?? "Player",
                    Seat = seat.Seat,
                    Color = seat.Color,
                    IsBot = seat.IsBot,
                    Rank = 0,
                    TokensHome = 0,
                    SixesRolled = 0,
                    CoinsDelta = 0,
                    Disconnected = false
                }).ToList()
            };

            _db.Matches.Add(record);
            await _db.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Startup recovery: any match still marked running after the grace period is
        /// aborted and every human entry fee is refunded (idempotently).
        /// </summary>
        public async Task<int> RecoverStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken)
        {
            DateTime cutoff = DateTime.UtcNow - olderThan;

            List<MatchRecord> stale = await _db.Matches
                .Include(m => m.Participants)
                .Where(m => m.Status == 0 && m.StartedUtc < cutoff)
                .Take(200)
                .ToListAsync(cancellationToken);

            foreach (MatchRecord match in stale)
            {
                foreach (MatchParticipant participant in match.Participants)
                {
                    if (participant.IsBot || string.IsNullOrEmpty(participant.AccountId) || match.EntryFee <= 0)
                    {
                        continue;
                    }

                    await RefundEntryAsync(match.Id, participant.AccountId, match.EntryFee, cancellationToken);
                }

                match.Status = 2;
                match.EndedUtc = DateTime.UtcNow;
            }

            if (stale.Count > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
                _log.LogWarning("Recovered {Count} stale matches and refunded entry fees", stale.Count);
            }

            return stale.Count;
        }

        public async Task<List<MatchHistoryDto>> GetHistoryAsync(string accountId, int take, CancellationToken cancellationToken)
        {
            var rows = await _db.Matches
                .AsNoTracking()
                .Where(m => m.Participants.Any(p => p.AccountId == accountId))
                .OrderByDescending(m => m.StartedUtc)
                .Take(Math.Clamp(take, 1, 50))
                .Select(m => new
                {
                    m.Id,
                    m.RoomCode,
                    m.EntryFee,
                    m.Pot,
                    m.WinnerAccountId,
                    m.Status,
                    m.EndedUtc,
                    Me = m.Participants.Where(p => p.AccountId == accountId).Select(p => new { p.Rank, p.CoinsDelta, p.TokensHome }).FirstOrDefault(),
                    Seats = m.Participants.Count
                })
                .ToListAsync(cancellationToken);

            return rows.Select(r => new MatchHistoryDto
            {
                MatchId = r.Id,
                RoomCode = r.RoomCode,
                SeatCount = r.Seats,
                EntryFee = r.EntryFee,
                Pot = r.Pot,
                Won = r.WinnerAccountId == accountId,
                Aborted = r.Status == 2,
                Rank = r.Me?.Rank ?? 0,
                CoinsDelta = r.Me?.CoinsDelta ?? 0,
                TokensHome = r.Me?.TokensHome ?? 0,
                EndedUtc = r.EndedUtc ?? DateTime.UtcNow
            }).ToList();
        }
    }

    public sealed class MatchHistoryDto
    {
        public string MatchId { get; set; }
        public string RoomCode { get; set; }
        public int SeatCount { get; set; }
        public long EntryFee { get; set; }
        public long Pot { get; set; }
        public bool Won { get; set; }
        public bool Aborted { get; set; }
        public int Rank { get; set; }
        public long CoinsDelta { get; set; }
        public int TokensHome { get; set; }
        public DateTime EndedUtc { get; set; }
    }
}
