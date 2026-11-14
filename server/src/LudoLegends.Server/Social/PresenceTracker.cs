using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LudoLegends.Server.Social
{
    /// <summary>
    /// Tracks which accounts currently have a live socket. In-memory by design: presence
    /// is ephemeral and per instance. With multiple server instances this would be backed
    /// by Redis (documented in docs/architecture.md) - the interface stays the same.
    /// </summary>
    public sealed class PresenceTracker
    {
        private readonly ConcurrentDictionary<string, int> _connections = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new ConcurrentDictionary<string, DateTime>();

        public void Attach(string accountId)
        {
            _connections.AddOrUpdate(accountId, 1, (_, current) => current + 1);
            _lastSeen[accountId] = DateTime.UtcNow;
        }

        public void Detach(string accountId)
        {
            _connections.AddOrUpdate(accountId, 0, (_, current) => Math.Max(0, current - 1));
            _lastSeen[accountId] = DateTime.UtcNow;
        }

        public bool IsOnline(string accountId)
        {
            return accountId != null && _connections.TryGetValue(accountId, out int count) && count > 0;
        }

        public DateTime? LastSeen(string accountId)
        {
            if (accountId != null && _lastSeen.TryGetValue(accountId, out DateTime value))
            {
                return value;
            }

            return null;
        }

        public IReadOnlyCollection<string> OnlineAccounts()
        {
            var online = new List<string>();
            foreach (KeyValuePair<string, int> pair in _connections)
            {
                if (pair.Value > 0)
                {
                    online.Add(pair.Key);
                }
            }

            return online;
        }
    }
}
