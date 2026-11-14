using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace LudoLegends.Server.Auth
{
    /// <summary>
    /// Validates Google Sign-In ID tokens.
    ///
    /// The token is verified locally against Google's published JWKS: signature (RS256
    /// only, so no algorithm-confusion attack), issuer, audience (our client id) and
    /// lifetime. No network call is made per login beyond cached key refreshes.
    /// </summary>
    public sealed class GoogleTokenValidator
    {
        private const string DiscoveryUrl = "https://accounts.google.com/.well-known/openid-configuration";

        private static readonly string[] ValidIssuers = { "https://accounts.google.com", "accounts.google.com" };

        private static readonly Lazy<ConfigurationManager<OpenIdConnectConfiguration>> ConfigManager =
            new Lazy<ConfigurationManager<OpenIdConnectConfiguration>>(() =>
                new ConfigurationManager<OpenIdConnectConfiguration>(
                    DiscoveryUrl,
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever { RequireHttps = true }));

        private readonly string _clientId;

        public GoogleTokenValidator(string clientId)
        {
            _clientId = clientId;
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(_clientId); }
        }

        public async Task<GoogleUser> ValidateAsync(string idToken, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                throw new NotConfiguredException("Google Sign-In is not configured on this server (Ludo:GoogleClientId).");
            }

            if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 8192)
            {
                throw new UnauthorizedAccessException("Missing or oversized Google ID token.");
            }

            OpenIdConnectConfiguration configuration = await ConfigManager.Value.GetConfigurationAsync(cancellationToken);

            var handler = new JsonWebTokenHandler { MapInboundClaims = false };
            TokenValidationResult result = await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = ValidIssuers,
                ValidateAudience = true,
                ValidAudience = _clientId,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
            });

            if (!result.IsValid)
            {
                throw new UnauthorizedAccessException("Google ID token rejected: " + result.Exception?.Message);
            }

            var identity = result.ClaimsIdentity;
            string subject = identity.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new UnauthorizedAccessException("Google ID token has no subject.");
            }

            string email = identity.FindFirst("email")?.Value;
            bool emailVerified = string.Equals(identity.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            string name = identity.FindFirst("name")?.Value ?? identity.FindFirst("given_name")?.Value;

            if (email != null && !emailVerified)
            {
                throw new UnauthorizedAccessException("Google account email is not verified.");
            }

            return new GoogleUser
            {
                Subject = subject,
                Email = email,
                DisplayName = SanitizeName(name),
                Issuer = identity.FindFirst("iss")?.Value
            };
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var chars = name.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-').ToArray();
            string cleaned = new string(chars).Trim();
            if (cleaned.Length > 16)
            {
                cleaned = cleaned.Substring(0, 16).Trim();
            }

            return cleaned.Length >= 3 ? cleaned : null;
        }
    }

    public sealed class GoogleUser
    {
        public string Subject { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string Issuer { get; set; }
    }

    public sealed class NotConfiguredException : Exception
    {
        public NotConfiguredException(string message)
            : base(message)
        {
        }
    }
}
