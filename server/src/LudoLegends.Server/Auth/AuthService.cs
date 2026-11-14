using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Server.Config;
using LudoLegends.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LudoLegends.Server.Auth
{
    /// <summary>
    /// Account creation and session token issuing.
    ///
    /// Guests get a real server account keyed by a hash of their installation id, so
    /// coins and stats survive reinstalls of the same device and can later be upgraded to
    /// a Google account in place. Nothing is ever trusted from the client except the
    /// device id and, for Google, a token we verify ourselves.
    /// </summary>
    public sealed class AuthService
    {
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly GameDbContext _db;
        private readonly ServerOptions _options;
        private readonly GoogleTokenValidator _google;
        private readonly ILogger<AuthService> _log;

        public AuthService(GameDbContext db, ServerOptions options, GoogleTokenValidator google, ILogger<AuthService> log)
        {
            _db = db;
            _options = options;
            _google = google;
            _log = log;
        }

        public bool GoogleConfigured
        {
            get { return _google.IsConfigured; }
        }

        public async Task<Account> SignInGuestAsync(string deviceId, string displayName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length < 8 || deviceId.Length > 128)
            {
                throw new ArgumentException("A device id of 8 to 128 characters is required.", nameof(deviceId));
            }

            string hash = HashDeviceId(deviceId);
            Account account = await _db.Accounts.FirstOrDefaultAsync(a => a.DeviceHash == hash && a.DeletedUtc == null, cancellationToken);

            if (account != null)
            {
                account.LastSeenUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return account;
            }

            account = new Account
            {
                Id = NewId(),
                PlayerCode = await GeneratePlayerCodeAsync(cancellationToken),
                DisplayName = NormalizeName(displayName) ?? ("Guest" + RandomDigits(4)),
                AvatarId = "avatar_01",
                IsGuest = true,
                DeviceHash = hash,
                CreatedUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                Level = 1,
                Xp = 0,
                Trophies = 0
            };

            _db.Accounts.Add(account);
            await _db.SaveChangesAsync(cancellationToken);
            _log.LogInformation("Created guest account {AccountId} ({PlayerCode})", account.Id, account.PlayerCode);
            return account;
        }

        public async Task<Account> SignInGoogleAsync(string idToken, string linkToAccountId, CancellationToken cancellationToken)
        {
            GoogleUser googleUser = await _google.ValidateAsync(idToken, cancellationToken);

            Account account = await _db.Accounts
                .FirstOrDefaultAsync(a => a.GoogleSubject == googleUser.Subject && a.DeletedUtc == null, cancellationToken);

            if (account != null)
            {
                account.LastSeenUtc = DateTime.UtcNow;
                if (googleUser.DisplayName != null && string.IsNullOrWhiteSpace(account.DisplayName))
                {
                    account.DisplayName = googleUser.DisplayName;
                }

                await _db.SaveChangesAsync(cancellationToken);
                return account;
            }

            // Upgrade an existing guest account in place so coins and stats are preserved.
            if (!string.IsNullOrWhiteSpace(linkToAccountId))
            {
                Account guest = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == linkToAccountId && a.DeletedUtc == null, cancellationToken);
                if (guest != null && string.IsNullOrWhiteSpace(guest.GoogleSubject))
                {
                    guest.GoogleSubject = googleUser.Subject;
                    guest.Email = googleUser.Email;
                    guest.IsGuest = false;
                    guest.DeviceHash = null; // device no longer defines identity
                    guest.LastSeenUtc = DateTime.UtcNow;
                    if (googleUser.DisplayName != null)
                    {
                        guest.DisplayName = googleUser.DisplayName;
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                    _log.LogInformation("Linked guest {AccountId} to Google subject", guest.Id);
                    return guest;
                }
            }

            account = new Account
            {
                Id = NewId(),
                PlayerCode = await GeneratePlayerCodeAsync(cancellationToken),
                DisplayName = googleUser.DisplayName ?? ("Player" + RandomDigits(4)),
                AvatarId = "avatar_01",
                IsGuest = false,
                GoogleSubject = googleUser.Subject,
                Email = googleUser.Email,
                CreatedUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                Level = 1
            };

            _db.Accounts.Add(account);
            await _db.SaveChangesAsync(cancellationToken);
            _log.LogInformation("Created Google account {AccountId}", account.Id);
            return account;
        }

        /// <summary>
        /// Issues a signed session token. The client stores it and sends it on every REST
        /// call and as the WebSocket auth token. Only the account id and display data are
        /// inside; nothing secret, nothing the client can escalate.
        /// </summary>
        public string IssueToken(Account account)
        {
            if (string.IsNullOrWhiteSpace(_options.TokenSigningKey))
            {
                throw new InvalidOperationException("Token signing key is not configured.");
            }

            var claims = new List<Claim>
            {
                new Claim("sub", account.Id),
                new Claim("name", account.DisplayName),
                new Claim("code", account.PlayerCode),
                new Claim("lvl", account.Level.ToString(CultureInfo.InvariantCulture)),
                new Claim("guest", account.IsGuest ? "1" : "0"),
                new Claim("jti", NewId())
            };

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = _options.Issuer,
                Audience = _options.Issuer,
                IssuedAt = DateTime.UtcNow,
                NotBefore = DateTime.UtcNow.AddSeconds(-30),
                Expires = DateTime.UtcNow.AddDays(_options.TokenLifetimeDays),
                Subject = new ClaimsIdentity(claims),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.TokenSigningKey)),
                    SecurityAlgorithms.HmacSha256)
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        public static string HashDeviceId(string deviceId)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes("ludo-device:" + deviceId));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public async Task<Account> GetAccountAsync(string accountId, CancellationToken cancellationToken)
        {
            return await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.DeletedUtc == null, cancellationToken);
        }

        /// <summary>
        /// Account deletion (Play Store requirement). The row is anonymised and all
        /// identifiers are released, so the same Google account or device can sign up
        /// fresh. Financial history is retained without personal data for audit purposes.
        /// </summary>
        public async Task<bool> DeleteAccountAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null)
            {
                return false;
            }

            account.DeletedUtc = DateTime.UtcNow;
            account.DeviceHash = null;
            account.GoogleSubject = null;
            account.Email = null;
            account.DisplayName = "Deleted player";
            account.UnlockedItems = string.Empty;

            List<DeviceSession> sessions = await _db.DeviceSessions.Where(s => s.AccountId == account.Id).ToListAsync(cancellationToken);
            _db.DeviceSessions.RemoveRange(sessions);

            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<string> GeneratePlayerCodeAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var builder = new StringBuilder(6);
                for (int i = 0; i < 6; i++)
                {
                    builder.Append(CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]);
                }

                string code = builder.ToString();
                bool exists = await _db.Accounts.AnyAsync(a => a.PlayerCode == code, cancellationToken);
                if (!exists)
                {
                    return code;
                }
            }

            throw new InvalidOperationException("Could not allocate a unique player code.");
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static string RandomDigits(int count)
        {
            var builder = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                builder.Append((char)('0' + RandomNumberGenerator.GetInt32(10)));
            }

            return builder.ToString();
        }

        /// <summary>Username rules: 3-16 chars, letters/digits/space/_/-, no leading/trailing space.</summary>
        public static string NormalizeName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return null;
            }

            var filtered = new StringBuilder(displayName.Length);
            foreach (char c in displayName.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-')
                {
                    filtered.Append(c);
                }
            }

            string cleaned = filtered.ToString().Trim();
            if (cleaned.Length < 3)
            {
                return null;
            }

            return cleaned.Length > 16 ? cleaned.Substring(0, 16).Trim() : cleaned;
        }
    }
}
