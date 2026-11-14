using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Server.Auth;
using LudoLegends.Server.Config;
using LudoLegends.Server.Data;
using LudoLegends.Server.Economy;
using LudoLegends.Server.Rooms;
using LudoLegends.Server.Social;
using LudoLegends.Server.Voice;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LudoLegends.Server.Api
{
    public sealed class GuestRequest
    {
        public string DeviceId { get; set; }
        public string DisplayName { get; set; }
    }

    public sealed class GoogleRequest
    {
        public string IdToken { get; set; }
    }

    public sealed class ProfileRequest
    {
        public string DisplayName { get; set; }
        public string AvatarId { get; set; }
    }

    public sealed class PurchaseRequest
    {
        public string ItemId { get; set; }
    }

    public sealed class FriendRequestDto
    {
        public string PlayerCode { get; set; }
    }

    public sealed class FriendResponseDto
    {
        public bool Accept { get; set; }
    }

    public sealed class VoiceRequest
    {
        public string RoomCode { get; set; }
    }

    /// <summary>
    /// HTTP API. Everything under /api/me and /api/wallet etc. requires a valid session
    /// token; the account id always comes from the token's subject claim, never from a
    /// parameter, so one player can never read or spend another player's coins.
    /// </summary>
    public static class Endpoints
    {
        public static void MapLudoApi(this WebApplication app)
        {
            app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

            MapAuth(app);
            MapProfile(app);
            MapEconomy(app);
            MapSocial(app);
            MapVoice(app);
        }

        // ------------------------------------------------------------- auth

        private static void MapAuth(WebApplication app)
        {
            app.MapPost("/api/auth/guest", async (
                GuestRequest request,
                AuthService auth,
                CoinService coins,
                ServerOptions options,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    Account account = await auth.SignInGuestAsync(request?.DeviceId, request?.DisplayName, cancellationToken);
                    long balance = await coins.GetBalanceAsync(account.Id, cancellationToken);
                    return Results.Ok(new
                    {
                        token = auth.IssueToken(account),
                        expiresInDays = options.TokenLifetimeDays,
                        profile = ToProfile(account, balance)
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = "invalid_request", message = ex.Message });
                }
            });

            app.MapPost("/api/auth/google", async (
                GoogleRequest request,
                HttpContext context,
                AuthService auth,
                GoogleTokenValidator google,
                CoinService coins,
                ServerOptions options,
                CancellationToken cancellationToken) =>
            {
                if (!google.IsConfigured)
                {
                    return Results.Json(new
                    {
                        error = "google_not_configured",
                        message = "Google Sign-In is not configured on this server yet. Guest login works."
                    }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                try
                {
                    string linkAccountId = context.User?.FindFirst("sub")?.Value;
                    Account account = await auth.SignInGoogleAsync(request?.IdToken, linkAccountId, cancellationToken);
                    long balance = await coins.GetBalanceAsync(account.Id, cancellationToken);
                    return Results.Ok(new
                    {
                        token = auth.IssueToken(account),
                        expiresInDays = options.TokenLifetimeDays,
                        profile = ToProfile(account, balance)
                    });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = "invalid_token", message = ex.Message },
                        statusCode: StatusCodes.Status401Unauthorized);
                }
                catch (NotConfiguredException ex)
                {
                    return Results.Json(new { error = "google_not_configured", message = ex.Message },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });

            app.MapPost("/api/auth/refresh", async (
                HttpContext context,
                AuthService auth,
                CoinService coins,
                ServerOptions options,
                CancellationToken cancellationToken) =>
            {
                Account account = await CurrentAccount(context, auth, cancellationToken);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                long balance = await coins.GetBalanceAsync(account.Id, cancellationToken);
                return Results.Ok(new
                {
                    token = auth.IssueToken(account),
                    expiresInDays = options.TokenLifetimeDays,
                    profile = ToProfile(account, balance)
                });
            }).RequireAuthorization();
        }

        // ------------------------------------------------------------- profile

        private static void MapProfile(WebApplication app)
        {
            app.MapGet("/api/me", async (
                HttpContext context,
                AuthService auth,
                CoinService coins,
                SocialService social,
                CancellationToken cancellationToken) =>
            {
                Account account = await CurrentAccount(context, auth, cancellationToken);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                long balance = await coins.GetBalanceAsync(account.Id, cancellationToken);
                List<FriendDto> friends = await social.GetFriendsAsync(account.Id, cancellationToken);

                return Results.Ok(new
                {
                    profile = ToProfile(account, balance),
                    stats = new
                    {
                        gamesPlayed = account.GamesPlayed,
                        wins = account.Wins,
                        losses = account.Losses,
                        abandoned = account.Abandoned,
                        trophies = account.Trophies,
                        winRate = account.GamesPlayed == 0 ? 0 : Math.Round(account.Wins * 100.0 / account.GamesPlayed, 1)
                    },
                    friendsOnline = friends.Count(f => f.Status == 1 && f.Online),
                    pendingFriendRequests = friends.Count(f => f.Status == 0)
                });
            }).RequireAuthorization();

            app.MapPatch("/api/me", async (
                ProfileRequest request,
                HttpContext context,
                AuthService auth,
                GameDbContext db,
                CancellationToken cancellationToken) =>
            {
                Account account = await CurrentAccount(context, auth, cancellationToken);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                if (!string.IsNullOrWhiteSpace(request?.DisplayName))
                {
                    string name = AuthService.NormalizeName(request.DisplayName);
                    if (name == null)
                    {
                        return Results.BadRequest(new { error = "invalid_name", message = "Name must be 3-16 letters, digits, spaces, _ or -." });
                    }

                    account.DisplayName = name;
                }

                if (!string.IsNullOrWhiteSpace(request?.AvatarId))
                {
                    string avatar = request.AvatarId.Trim();
                    if (avatar.Length > 32 || avatar.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
                    {
                        return Results.BadRequest(new { error = "invalid_avatar", message = "Unknown avatar id." });
                    }

                    account.AvatarId = avatar;
                }

                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { displayName = account.DisplayName, avatarId = account.AvatarId });
            }).RequireAuthorization();

            app.MapDelete("/api/me", async (
                HttpContext context,
                AuthService auth,
                CancellationToken cancellationToken) =>
            {
                Account account = await CurrentAccount(context, auth, cancellationToken);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                await auth.DeleteAccountAsync(account, cancellationToken);
                return Results.Ok(new { deleted = true });
            }).RequireAuthorization();
        }

        // ------------------------------------------------------------- economy

        private static void MapEconomy(WebApplication app)
        {
            app.MapGet("/api/wallet", async (HttpContext context, AuthService auth, CoinService coins, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new { balance = await coins.GetBalanceAsync(account.Id, ct) });
            }).RequireAuthorization();

            app.MapGet("/api/wallet/transactions", async (HttpContext context, AuthService auth, CoinService coins, int? take, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new { items = await coins.GetTransactionsAsync(account.Id, take ?? 50, ct) });
            }).RequireAuthorization();

            app.MapGet("/api/rewards/daily", async (HttpContext context, AuthService auth, CoinService coins, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(await coins.GetDailyStatusAsync(account.Id, ct));
            }).RequireAuthorization();

            app.MapPost("/api/rewards/daily/claim", async (HttpContext context, AuthService auth, CoinService coins, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                DailyStatus status = await coins.ClaimDailyAsync(account.Id, ct);
                return status.Error == null ? Results.Ok(status) : Results.BadRequest(status);
            }).RequireAuthorization();

            app.MapGet("/api/missions", async (HttpContext context, AuthService auth, CoinService coins, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new { items = await coins.GetMissionsAsync(account.Id, ct) });
            }).RequireAuthorization();

            app.MapPost("/api/missions/{key}/claim", async (string key, HttpContext context, AuthService auth, CoinService coins, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                LedgerResult result = await coins.ClaimMissionAsync(account.Id, key, ct);
                return result.Error == null ? Results.Ok(new { balance = result.Balance }) : Results.BadRequest(new { error = "mission_not_claimable", message = result.Error });
            }).RequireAuthorization();

            app.MapGet("/api/achievements", async (HttpContext context, AuthService auth, CoinService coins, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new { items = await coins.GetAchievementsAsync(account.Id, ct) });
            }).RequireAuthorization();

            app.MapPost("/api/achievements/{key}/claim", async (string key, HttpContext context, AuthService auth, CoinService coins, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                LedgerResult result = await coins.ClaimAchievementAsync(account.Id, key, ct);
                return result.Error == null ? Results.Ok(new { balance = result.Balance }) : Results.BadRequest(new { error = "achievement_not_claimable", message = result.Error });
            }).RequireAuthorization();

            app.MapGet("/api/shop", async (HttpContext context, AuthService auth, CoinService coins, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new
                {
                    items = CoinService.Catalogue,
                    balance = await coins.GetBalanceAsync(account.Id, ct),
                    owned = (account.UnlockedItems ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
                    adsRemoved = account.AdsRemoved
                });
            }).RequireAuthorization();

            app.MapPost("/api/shop/purchase", async (PurchaseRequest request, HttpContext context, AuthService auth, CoinService coins, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                (bool ok, string error, long balance) = await coins.PurchaseAsync(account.Id, request?.ItemId, ct);
                return ok
                    ? Results.Ok(new { purchased = true, balance })
                    : Results.BadRequest(new { error = "purchase_failed", message = error, balance });
            }).RequireAuthorization();

            app.MapGet("/api/history", async (HttpContext context, AuthService auth, MatchService matches, int? take, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new { items = await matches.GetHistoryAsync(account.Id, take ?? 20, ct) });
            }).RequireAuthorization();
        }

        // ------------------------------------------------------------- social

        private static void MapSocial(WebApplication app)
        {
            app.MapGet("/api/leaderboard", async (
                HttpContext context,
                AuthService auth,
                SocialService social,
                string scope,
                int? take,
                CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                string normalized = (scope ?? "global").Trim().ToLowerInvariant();
                if (normalized != "global" && normalized != "weekly" && normalized != "friends")
                {
                    return Results.BadRequest(new { error = "invalid_scope", message = "scope must be global, weekly or friends." });
                }

                return Results.Ok(new
                {
                    scope = normalized,
                    items = await social.GetLeaderboardAsync(normalized, account.Id, take ?? 50, ct)
                });
            }).RequireAuthorization();

            app.MapGet("/api/friends", async (HttpContext context, AuthService auth, SocialService social, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                List<FriendDto> friends = await social.GetFriendsAsync(account.Id, ct);
                return Results.Ok(new
                {
                    friends = friends.Where(f => f.Status == 1),
                    incoming = friends.Where(f => f.Status == 0),
                    declined = friends.Where(f => f.Status == 2),
                    myPlayerCode = account.PlayerCode
                });
            }).RequireAuthorization();

            app.MapPost("/api/friends/request", async (FriendRequestDto request, HttpContext context, AuthService auth, SocialService social, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                (bool ok, string error) = await social.SendRequestAsync(account.Id, request?.PlayerCode, ct);
                return ok ? Results.Ok(new { sent = true }) : Results.BadRequest(new { error = "friend_request_failed", message = error });
            }).RequireAuthorization();

            app.MapPost("/api/friends/{id}/respond", async (string id, FriendResponseDto request, HttpContext context, AuthService auth, SocialService social, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                (bool ok, string error) = await social.RespondAsync(account.Id, id, request?.Accept ?? false, ct);
                return ok ? Results.Ok(new { handled = true }) : Results.BadRequest(new { error = "friend_request_failed", message = error });
            }).RequireAuthorization();

            app.MapDelete("/api/friends/{id}", async (string id, HttpContext context, AuthService auth, SocialService social, CancellationToken ct) =>
            {
                Account account = await CurrentAccount(context, auth, ct);
                if (account == null)
                {
                    return Results.Unauthorized();
                }

                bool removed = await social.RemoveFriendAsync(account.Id, id, ct);
                return removed ? Results.Ok(new { removed = true }) : Results.NotFound(new { error = "not_found" });
            }).RequireAuthorization();

            app.MapGet("/api/rooms/{code}", (string code, RoomManager rooms) =>
            {
                if (!rooms.TryGetRoom(code, out GameRoom room))
                {
                    return Results.NotFound(new { error = "room_not_found" });
                }

                return Results.Ok(room.Snapshot());
            }).RequireAuthorization();
        }

        // ------------------------------------------------------------- voice

        private static void MapVoice(WebApplication app)
        {
            app.MapPost("/api/voice/token", (VoiceRequest request, HttpContext context, AuthService auth, VoiceService voice, CancellationToken ct) =>
            {
                string accountId = context.User?.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(accountId))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    return Results.Ok(voice.CreateToken(accountId, request?.RoomCode));
                }
                catch (NotConfiguredException ex)
                {
                    return Results.Json(new { error = "voice_not_configured", message = ex.Message },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = "not_in_room", message = ex.Message },
                        statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = "invalid_request", message = ex.Message });
                }
            }).RequireAuthorization();

            // Photon custom-auth webhook. Configure this URL in the Photon dashboard
            // (Authentication -> Custom Authentication). It returns Photon's expected shape.
            app.MapPost("/api/photon/auth", async (HttpRequest request, VoiceService voice, CancellationToken ct) =>
            {
                string token = request.Query["token"];
                if (string.IsNullOrEmpty(token) && request.HasFormContentType)
                {
                    IFormCollection form = await request.ReadFormAsync(ct);
                    token = form["token"];
                    if (string.IsNullOrEmpty(token))
                    {
                        token = form["AuthParameters"];
                    }
                }

                bool ok = voice.TryValidate(token, out string accountId, out _, out string error);
                return Results.Ok(VoiceService.PhotonAuthResponse(ok, accountId, error));
            });
        }

        // ------------------------------------------------------------- helpers

        private static async Task<Account> CurrentAccount(HttpContext context, AuthService auth, CancellationToken cancellationToken)
        {
            string accountId = context.User?.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(accountId))
            {
                return null;
            }

            return await auth.GetAccountAsync(accountId, cancellationToken);
        }

        private static object ToProfile(Account account, long balance)
        {
            return new
            {
                accountId = account.Id,
                playerCode = account.PlayerCode,
                displayName = account.DisplayName,
                avatarId = account.AvatarId,
                isGuest = account.IsGuest,
                level = account.Level,
                xp = account.Xp,
                xpIntoLevel = account.Xp % 200,
                xpForNextLevel = 200,
                trophies = account.Trophies,
                coins = balance,
                adsRemoved = account.AdsRemoved,
                unlockedItems = (account.UnlockedItems ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
                createdAtUtc = account.CreatedUtc
            };
        }
    }
}
