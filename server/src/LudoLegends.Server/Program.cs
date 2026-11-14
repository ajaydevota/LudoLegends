using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.RateLimiting;
using LudoLegends.Server.Api;
using LudoLegends.Server.Auth;
using LudoLegends.Server.Config;
using LudoLegends.Server.Data;
using LudoLegends.Server.Economy;
using LudoLegends.Server.Net;
using LudoLegends.Server.Rooms;
using LudoLegends.Server.Social;
using LudoLegends.Server.Voice;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LudoLegends.Server
{
    public static class Program
    {
        public static async System.Threading.Tasks.Task Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Configuration.AddEnvironmentVariables();

            var options = new ServerOptions();
            builder.Configuration.GetSection(ServerOptions.SectionName).Bind(options);

            // In development we generate an ephemeral key so the server is usable with zero
            // setup, but production refuses to start without a real secret.
            if (string.IsNullOrWhiteSpace(options.TokenSigningKey))
            {
                if (builder.Environment.IsDevelopment())
                {
                    options.TokenSigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                }
            }

            var problems = options.Validate(builder.Environment.IsProduction());
            if (problems.Count > 0)
            {
                var logger = LoggerFactory.Create(b => b.AddSimpleConsole()).CreateLogger("Startup");
                foreach (string problem in problems)
                {
                    logger.LogError("Configuration error: {Problem}", problem);
                }

                throw new InvalidOperationException(
                    "Server configuration is invalid:\n - " + string.Join("\n - ", problems));
            }

            builder.Services.AddSingleton(options);

            builder.Services.AddDbContext<GameDbContext>(db =>
            {
                if (string.Equals(options.Database.Provider, "postgres", StringComparison.OrdinalIgnoreCase))
                {
                    db.UseNpgsql(options.Database.ConnectionString);
                }
                else
                {
                    db.UseSqlite(options.Database.ConnectionString);
                }
            });

            // Auth
            var tokenValidator = new TokenValidator(options);
            builder.Services.AddSingleton(tokenValidator);
            builder.Services.AddSingleton(new GoogleTokenValidator(options.GoogleClientId));
            builder.Services.AddScoped<AuthService>();

            // Economy / social
            builder.Services.AddScoped<CoinService>();
            builder.Services.AddScoped<MatchService>();
            builder.Services.AddScoped<SocialService>();
            builder.Services.AddSingleton<PresenceTracker>();

            // Realtime
            builder.Services.AddSingleton<RoomManager>();
            builder.Services.AddSingleton<ConnectionRegistry>();
            builder.Services.AddSingleton<GameHub>();
            builder.Services.AddHostedService<RoomTicker>();
            builder.Services.AddScoped<VoiceService>();

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwt =>
                {
                    jwt.MapInboundClaims = false;
                    jwt.TokenValidationParameters = tokenValidator.BuildValidationParameters();
                    jwt.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            // Browsers cannot set headers on WebSocket upgrades, so a query
                            // token is also accepted for the /ws endpoint.
                            string accessToken = context.Request.Query["access_token"];
                            if (!string.IsNullOrEmpty(accessToken) &&
                                context.HttpContext.Request.Path.StartsWithSegments("/ws"))
                            {
                                context.Token = accessToken;
                            }

                            return System.Threading.Tasks.Task.CompletedTask;
                        }
                    };
                });

            builder.Services.AddAuthorization();

            builder.Services.AddRateLimiter(limiter =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
            });

            WebApplication app = builder.Build();

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            app.UseRateLimiter();
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapLudoApi();

            GameHub hub = app.Services.GetRequiredService<GameHub>();
            app.Map("/ws", (RequestDelegate)hub.HandleAsync);

            // Database bootstrap + crash recovery.
            using (IServiceScope scope = app.Services.CreateScope())
            {
                GameDbContext db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
                if (options.Database.AutoCreate)
                {
                    await db.Database.EnsureCreatedAsync();
                }

                MatchService matches = scope.ServiceProvider.GetRequiredService<MatchService>();
                await matches.RecoverStaleAsync(TimeSpan.FromHours(2), CancellationToken.None);
            }

            ILogger startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
            startupLog.LogInformation(
                "Ludo Legends server ready. db={Provider} google={Google} voice={Voice}",
                options.Database.Provider,
                string.IsNullOrWhiteSpace(options.GoogleClientId) ? "disabled" : "enabled",
                string.IsNullOrWhiteSpace(options.Voice.TokenSigningKey) ? "inherits-session-key" : "separate-key");

            await app.RunAsync();
        }
    }
}
