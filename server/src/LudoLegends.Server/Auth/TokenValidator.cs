using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using LudoLegends.Server.Config;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LudoLegends.Server.Auth
{
    /// <summary>
    /// Validates self-issued session tokens. The algorithm is pinned to HS256, the issuer
    /// is checked, and lifetime is enforced, so a token cannot be forged or replayed after
    /// expiry.
    /// </summary>
    public sealed class TokenValidator
    {
        private readonly ServerOptions _options;

        public TokenValidator(ServerOptions options)
        {
            _options = options;
        }

        public TokenValidationParameters BuildValidationParameters()
        {
            return new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Issuer,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.TokenSigningKey ?? string.Empty)),
                NameClaimType = "sub"
            };
        }

        public async Task<string> ValidateAccountIdAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(_options.TokenSigningKey))
            {
                return null;
            }

            if (token.Length > 4096)
            {
                return null;
            }

            var handler = new JsonWebTokenHandler { MapInboundClaims = false };
            TokenValidationResult result = await handler.ValidateTokenAsync(token, BuildValidationParameters());
            if (!result.IsValid)
            {
                return null;
            }

            ClaimsIdentity identity = result.ClaimsIdentity;
            return identity?.FindFirst("sub")?.Value;
        }
    }
}
