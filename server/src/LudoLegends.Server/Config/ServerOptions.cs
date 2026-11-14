using System;
using System.Collections.Generic;

namespace LudoLegends.Server.Config
{
    /// <summary>
    /// All server configuration. Bound from configuration/env vars in Program.cs.
    /// Secrets are ONLY read from configuration (env vars / user secrets / secret store),
    /// never committed to the repository.
    /// </summary>
    public sealed class ServerOptions
    {
        public const string SectionName = "Ludo";

        /// <summary>Public origin of this server, used in documentation and voice tokens.</summary>
        public string PublicBaseUrl { get; set; } = "http://localhost:5080";

        /// <summary>JWT issuer claim for self-issued session tokens.</summary>
        public string Issuer { get; set; } = "ludolegends";

        /// <summary>HMAC key for session tokens. MUST be at least 32 chars in production.</summary>
        public string TokenSigningKey { get; set; }

        public int TokenLifetimeDays { get; set; } = 30;

        /// <summary>Google OAuth web client id used as the audience for ID tokens.</summary>
        public string GoogleClientId { get; set; }

        public DatabaseOptions Database { get; set; } = new DatabaseOptions();

        public GameOptions Game { get; set; } = new GameOptions();

        public MatchmakingOptions Matchmaking { get; set; } = new MatchmakingOptions();

        public VoiceOptions Voice { get; set; } = new VoiceOptions();

        public FcmOptions Push { get; set; } = new FcmOptions();

        public EconomyOptions Economy { get; set; } = new EconomyOptions();

        /// <summary>Returns every configuration problem, so startup can fail loudly.</summary>
        public IReadOnlyList<string> Validate(bool isProduction)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(TokenSigningKey))
            {
                problems.Add("Ludo:TokenSigningKey is not set (env LUDO__TOKENSIGNINGKEY). Sessions cannot be signed.");
            }
            else if (isProduction && TokenSigningKey.Length < 32)
            {
                problems.Add("Ludo:TokenSigningKey must be at least 32 characters in production.");
            }

            if (TokenLifetimeDays < 1 || TokenLifetimeDays > 365)
            {
                problems.Add("Ludo:TokenLifetimeDays must be between 1 and 365.");
            }

            if (Database.Provider != "sqlite" && Database.Provider != "postgres")
            {
                problems.Add("Ludo:Database:Provider must be 'sqlite' or 'postgres'.");
            }

            if (Database.Provider == "postgres" && string.IsNullOrWhiteSpace(Database.ConnectionString))
            {
                problems.Add("Ludo:Database:ConnectionString is required for postgres.");
            }

            if (Game.TurnTimeoutSeconds < 10 || Game.TurnTimeoutSeconds > 180)
            {
                problems.Add("Ludo:Game:TurnTimeoutSeconds must be between 10 and 180.");
            }

            if (Game.DisconnectGraceSeconds < 10 || Game.DisconnectGraceSeconds > 600)
            {
                problems.Add("Ludo:Game:DisconnectGraceSeconds must be between 10 and 600.");
            }

            return problems;
        }
    }

    public sealed class DatabaseOptions
    {
        /// <summary>"sqlite" (default, zero-setup dev) or "postgres".</summary>
        public string Provider { get; set; } = "sqlite";

        public string ConnectionString { get; set; } = "Data Source=ludolegends.db";

        /// <summary>Creates tables from the EF model on startup. Fine for dev, use SQL in production.</summary>
        public bool AutoCreate { get; set; } = true;
    }

    public sealed class GameOptions
    {
        /// <summary>Seconds the active seat has to roll and move before the server auto-plays.</summary>
        public int TurnTimeoutSeconds { get; set; } = 25;

        /// <summary>Seconds a disconnected seat keeps its turn before the server auto-plays for it.</summary>
        public int DisconnectGraceSeconds { get; set; } = 60;

        /// <summary>Minutes an idle room is kept before cleanup.</summary>
        public int RoomIdleTimeoutMinutes { get; set; } = 10;

        /// <summary>Maximum accepted client message size in bytes.</summary>
        public int MaxMessageBytes { get; set; } = 4096;

        /// <summary>Sustained client messages per second (bursts allowed up to 4x).</summary>
        public int MessagesPerSecond { get; set; } = 8;
    }

    public sealed class MatchmakingOptions
    {
        /// <summary>Seconds in the queue before filling empty seats with bots (0 disables bot fill).</summary>
        public int BotFillAfterSeconds { get; set; } = 0;

        /// <summary>Seconds a player waits before the queue gives up.</summary>
        public int QueueTimeoutSeconds { get; set; } = 60;
    }

    public sealed class VoiceOptions
    {
        /// <summary>HMAC key used to sign short-lived voice room tokens.</summary>
        public string TokenSigningKey { get; set; }

        /// <summary>Voice provider ("photon"). Kept abstract so the provider can be swapped.</summary>
        public string Provider { get; set; } = "photon";

        public string PhotonAppId { get; set; }

        public int TokenLifetimeSeconds { get; set; } = 300;
    }

    public sealed class FcmOptions
    {
        /// <summary>Path to a Firebase service account JSON. When empty, push is disabled.</summary>
        public string ServiceAccountPath { get; set; }

        public string ProjectId { get; set; }
    }

    public sealed class EconomyOptions
    {
        public long StartingCoins { get; set; } = 500;

        /// <summary>
        /// Coins paid across a daily-login streak, starting at day 1. Left null so the
        /// configuration binder assigns it instead of appending to a default array
        /// (arrays are additive in Microsoft.Extensions.Configuration).
        /// </summary>
        public long[] DailyRewards { get; set; }

        public int XpPerGame { get; set; } = 20;

        public int XpPerWin { get; set; } = 60;

        public int XpPerLevel { get; set; } = 200;

        public int TrophiesPerWin { get; set; } = 25;

        public int TrophiesPerLoss { get; set; } = 15;
    }
}
