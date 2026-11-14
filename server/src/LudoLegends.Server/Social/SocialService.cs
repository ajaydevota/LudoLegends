using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Server.Auth;
using LudoLegends.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace LudoLegends.Server.Social
{
    public sealed class FriendDto
    {
        public string AccountId { get; set; }
        public string PlayerCode { get; set; }
        public string DisplayName { get; set; }
        public string AvatarId { get; set; }
        public int Level { get; set; }
        public int Trophies { get; set; }
        public bool Online { get; set; }
        public int Status { get; set; }
        public string FriendshipId { get; set; }
        public DateTime? LastSeenUtc { get; set; }
    }

    public sealed class LeaderboardEntryDto
    {
        public int Position { get; set; }
        public string AccountId { get; set; }
        public string DisplayName { get; set; }
        public string AvatarId { get; set; }
        public int Level { get; set; }
        public int Trophies { get; set; }
        public int Wins { get; set; }
        public int Value { get; set; }
    }

    /// <summary>
    /// Friends and leaderboards. Presence is supplied by the live sockets through
    /// <see cref="PresenceTracker"/>; everything else is database state.
    /// </summary>
    public sealed class SocialService
    {
        private readonly GameDbContext _db;
        private readonly PresenceTracker _presence;

        public SocialService(GameDbContext db, PresenceTracker presence)
        {
            _db = db;
            _presence = presence;
        }

        public async Task<(bool ok, string error)> SendRequestAsync(string requesterId, string playerCode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(playerCode))
            {
                return (false, "Enter a player code.");
            }

            string code = playerCode.Trim().ToUpperInvariant();
            Account target = await _db.Accounts.FirstOrDefaultAsync(a => a.PlayerCode == code && a.DeletedUtc == null, cancellationToken);
            if (target == null)
            {
                return (false, "No player with that code.");
            }

            if (target.Id == requesterId)
            {
                return (false, "You cannot add yourself.");
            }

            Friendship existing = await _db.Friendships.FirstOrDefaultAsync(
                f => (f.RequesterId == requesterId && f.AddresseeId == target.Id) ||
                     (f.RequesterId == target.Id && f.AddresseeId == requesterId),
                cancellationToken);

            if (existing != null)
            {
                if (existing.Status == 1)
                {
                    return (false, "You are already friends.");
                }

                if (existing.Status == 0 && existing.AddresseeId == requesterId)
                {
                    // They already asked us: auto-accept.
                    existing.Status = 1;
                    existing.RespondedUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                    return (true, null);
                }

                return (false, "A request is already pending.");
            }

            _db.Friendships.Add(new Friendship
            {
                Id = AuthService.NewId(),
                RequesterId = requesterId,
                AddresseeId = target.Id,
                Status = 0,
                CreatedUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);
            return (true, null);
        }

        public async Task<(bool ok, string error)> RespondAsync(string accountId, string friendshipId, bool accept, CancellationToken cancellationToken)
        {
            Friendship friendship = await _db.Friendships.FirstOrDefaultAsync(f => f.Id == friendshipId, cancellationToken);
            if (friendship == null || friendship.AddresseeId != accountId)
            {
                return (false, "Request not found.");
            }

            if (friendship.Status != 0)
            {
                return (false, "Request already handled.");
            }

            friendship.Status = accept ? 1 : 2;
            friendship.RespondedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return (true, null);
        }

        public async Task<bool> RemoveFriendAsync(string accountId, string friendAccountId, CancellationToken cancellationToken)
        {
            Friendship friendship = await _db.Friendships.FirstOrDefaultAsync(
                f => (f.RequesterId == accountId && f.AddresseeId == friendAccountId) ||
                     (f.RequesterId == friendAccountId && f.AddresseeId == accountId),
                cancellationToken);

            if (friendship == null)
            {
                return false;
            }

            _db.Friendships.Remove(friendship);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<List<FriendDto>> GetFriendsAsync(string accountId, CancellationToken cancellationToken)
        {
            var links = await _db.Friendships
                .AsNoTracking()
                .Where(f => f.RequesterId == accountId || f.AddresseeId == accountId)
                .ToListAsync(cancellationToken);

            var otherIds = links
                .Select(f => f.RequesterId == accountId ? f.AddresseeId : f.RequesterId)
                .Distinct()
                .ToList();

            var accounts = await _db.Accounts
                .AsNoTracking()
                .Where(a => otherIds.Contains(a.Id) && a.DeletedUtc == null)
                .ToDictionaryAsync(a => a.Id, cancellationToken);

            var result = new List<FriendDto>();
            foreach (Friendship link in links)
            {
                string otherId = link.RequesterId == accountId ? link.AddresseeId : link.RequesterId;
                if (!accounts.TryGetValue(otherId, out Account other))
                {
                    continue;
                }

                result.Add(new FriendDto
                {
                    AccountId = other.Id,
                    PlayerCode = other.PlayerCode,
                    DisplayName = other.DisplayName,
                    AvatarId = other.AvatarId,
                    Level = other.Level,
                    Trophies = other.Trophies,
                    Online = _presence.IsOnline(other.Id),
                    Status = link.Status,
                    FriendshipId = link.Id,
                    LastSeenUtc = other.LastSeenUtc
                });
            }

            return result
                .OrderByDescending(f => f.Status == 1)
                .ThenByDescending(f => f.Online)
                .ThenBy(f => f.DisplayName)
                .ToList();
        }

        /// <summary>
        /// Leaderboard scopes:
        ///   global  - all-time trophies
        ///   weekly  - wins in the last 7 days
        ///   friends - all-time trophies restricted to accepted friends
        /// </summary>
        public async Task<List<LeaderboardEntryDto>> GetLeaderboardAsync(string scope, string accountId, int take, CancellationToken cancellationToken)
        {
            take = Math.Clamp(take, 1, 100);
            List<LeaderboardEntryDto> entries;

            if (scope == "weekly")
            {
                DateTime cutoff = DateTime.UtcNow.AddDays(-7);
                var wins = await _db.Matches
                    .AsNoTracking()
                    .Where(m => m.Status == 1 && m.EndedUtc != null && m.EndedUtc >= cutoff && m.WinnerAccountId != null)
                    .Select(m => m.WinnerAccountId)
                    .ToListAsync(cancellationToken);

                var counts = wins
                    .Where(id => id != null)
                    .GroupBy(id => id)
                    .Select(g => new { AccountId = g.Key, Wins = g.Count() })
                    .OrderByDescending(x => x.Wins)
                    .Take(take)
                    .ToList();

                var ids = counts.Select(c => c.AccountId).ToList();
                var accounts = await _db.Accounts
                    .AsNoTracking()
                    .Where(a => ids.Contains(a.Id) && a.DeletedUtc == null)
                    .ToDictionaryAsync(a => a.Id, cancellationToken);

                entries = new List<LeaderboardEntryDto>();
                int position = 1;
                foreach (var count in counts)
                {
                    if (!accounts.TryGetValue(count.AccountId, out Account account))
                    {
                        continue;
                    }

                    entries.Add(new LeaderboardEntryDto
                    {
                        Position = position++,
                        AccountId = account.Id,
                        DisplayName = account.DisplayName,
                        AvatarId = account.AvatarId,
                        Level = account.Level,
                        Trophies = account.Trophies,
                        Wins = count.Wins,
                        Value = count.Wins
                    });
                }

                return entries;
            }

            IQueryable<Account> query = _db.Accounts.AsNoTracking().Where(a => a.DeletedUtc == null);

            if (scope == "friends")
            {
                var links = await _db.Friendships
                    .AsNoTracking()
                    .Where(f => f.Status == 1 && (f.RequesterId == accountId || f.AddresseeId == accountId))
                    .ToListAsync(cancellationToken);

                List<string> friendIds = links
                    .Select(f => f.RequesterId == accountId ? f.AddresseeId : f.RequesterId)
                    .ToList();

                friendIds.Add(accountId);
                query = query.Where(a => friendIds.Contains(a.Id));
            }

            var rows = await query
                .OrderByDescending(a => a.Trophies)
                .ThenByDescending(a => a.Wins)
                .Take(take)
                .Select(a => new { a.Id, a.DisplayName, a.AvatarId, a.Level, a.Trophies, a.Wins })
                .ToListAsync(cancellationToken);

            entries = rows.Select((a, index) => new LeaderboardEntryDto
            {
                Position = index + 1,
                AccountId = a.Id,
                DisplayName = a.DisplayName,
                AvatarId = a.AvatarId,
                Level = a.Level,
                Trophies = a.Trophies,
                Wins = a.Wins,
                Value = a.Trophies
            }).ToList();

            return entries;
        }
    }
}
