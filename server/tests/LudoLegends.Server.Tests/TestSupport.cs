using System;
using System.Collections.Generic;
using LudoLegends.Core;
using LudoLegends.Server.Auth;
using LudoLegends.Server.Config;
using LudoLegends.Server.Data;
using LudoLegends.Server.Economy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LudoLegends.Server.Tests
{
    /// <summary>
    /// One in-memory SQLite database per test, shared by every context the test creates.
    /// The production code is provider agnostic, so behaviour tested here matches
    /// PostgreSQL apart from SQL dialect details already isolated in EF.
    /// </summary>
    internal sealed class TestDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;
        private GameDbContext _shared;

        public TestDatabase()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            using GameDbContext db = NewContext();
            db.Database.EnsureCreated();
        }

        /// <summary>
        /// The context shared by composed services, mirroring ASP.NET's scoped DbContext:
        /// MatchService and CoinService must see the same transaction.
        /// </summary>
        public GameDbContext Shared
        {
            get { return _shared ??= NewContext(); }
        }

        public GameDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<GameDbContext>()
                .UseSqlite(_connection)
                .EnableSensitiveDataLogging(false)
                .Options;

            return new GameDbContext(options);
        }

        public CoinService NewCoinService(ServerOptions options)
        {
            return new CoinService(NewContext(), options, NullLogger<CoinService>.Instance);
        }

        public void Dispose()
        {
            _shared?.Dispose();
            _connection.Dispose();
        }
    }

    internal static class TestOptions
    {
        public static ServerOptions Create()
        {
            return new ServerOptions
            {
                Issuer = "ludolegends",
                TokenSigningKey = "test-signing-key-that-is-long-enough-1234",
                TokenLifetimeDays = 30,
                GoogleClientId = string.Empty,
                Database = new DatabaseOptions { Provider = "sqlite", ConnectionString = "DataSource=:memory:", AutoCreate = true },
                Game = new GameOptions
                {
                    TurnTimeoutSeconds = 25,
                    DisconnectGraceSeconds = 60,
                    RoomIdleTimeoutMinutes = 10,
                    MaxMessageBytes = 4096,
                    MessagesPerSecond = 8
                },
                Economy = new EconomyOptions
                {
                    StartingCoins = 500,
                    DailyRewards = new long[] { 50, 75, 100, 125, 150, 200, 300 },
                    XpPerGame = 20,
                    XpPerWin = 60,
                    XpPerLevel = 200,
                    TrophiesPerWin = 25,
                    TrophiesPerLoss = 15
                }
            };
        }
    }

    internal sealed class ScriptedRandom : IRandomSource
    {
        private readonly int[] _values;
        private int _index;

        public ScriptedRandom(params int[] values)
        {
            _values = values.Length == 0 ? new[] { 6 } : values;
        }

        public int NextDie()
        {
            int value = _values[_index % _values.Length];
            _index++;
            return value;
        }
    }

    internal sealed class FakeClock
    {
        public long NowMs { get; set; } = 1_000_000;

        public long Read()
        {
            return NowMs;
        }

        public void Advance(int milliseconds)
        {
            NowMs += milliseconds;
        }
    }

    internal static class AccountFactory
    {
        public static Account Create(GameDbContext db, string id, string name, long? balance = null)
        {
            var account = new Account
            {
                Id = id,
                PlayerCode = id.ToUpperInvariant().PadRight(6, 'X'),
                DisplayName = name,
                AvatarId = "avatar_01",
                IsGuest = true,
                DeviceHash = null,
                CreatedUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                Level = 1
            };

            db.Accounts.Add(account);

            if (balance.HasValue)
            {
                db.Wallets.Add(new Wallet { AccountId = id, Balance = balance.Value, UpdatedUtc = DateTime.UtcNow });
            }

            db.SaveChanges();
            return account;
        }
    }
}
