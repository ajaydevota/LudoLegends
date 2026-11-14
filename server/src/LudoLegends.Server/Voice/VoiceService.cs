using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LudoLegends.Server.Auth;
using LudoLegends.Server.Config;
using LudoLegends.Server.Rooms;

namespace LudoLegends.Server.Voice
{
    public sealed class VoiceTokenDto
    {
        public string Provider { get; set; }
        public string Channel { get; set; }
        public string Token { get; set; }
        public int ExpiresIn { get; set; }
        public long ServerTimeMs { get; set; }
    }

    /// <summary>
    /// Issues short-lived, room-scoped voice join tokens.
    ///
    /// Voice is deliberately decoupled from the game transport: the client asks for a
    /// token when it enters a room, hands it to the voice provider (Photon Voice by
    /// default), and the provider verifies it through the custom-auth webhook exposed at
    /// /api/photon/auth. Leaving the room invalidates the channel because the game server
    /// stops refreshing membership, and the token itself expires in five minutes.
    ///
    /// Tokens carry no personal data beyond the account id and channel name.
    /// </summary>
    public sealed class VoiceService
    {
        private readonly ServerOptions _options;
        private readonly RoomManager _rooms;
        private readonly TimeProvider _time;

        public VoiceService(ServerOptions options, RoomManager rooms)
        {
            _options = options;
            _rooms = rooms;
            _time = TimeProvider.System;
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(SigningKey()); }
        }

        public VoiceTokenDto CreateToken(string accountId, string roomCode)
        {
            string key = SigningKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new NotConfiguredException(
                    "Voice is not configured. Set Ludo:Voice:TokenSigningKey (env LUDO__VOICE__TOKENSIGNINGKEY).");
            }

            string code = (roomCode ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length == 0 || code.Length > 12)
            {
                throw new ArgumentException("A room code is required.", nameof(roomCode));
            }

            if (!_rooms.TryGetRoom(code, out GameRoom room) || !room.IsMember(accountId))
            {
                throw new UnauthorizedAccessException("You must be in that room to join its voice channel.");
            }

            int lifetime = Math.Clamp(_options.Voice.TokenLifetimeSeconds, 60, 900);
            long expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime).ToUnixTimeSeconds();
            string channel = "room-" + code;

            string payload = accountId + "|" + channel + "|" + expiresAt.ToString(CultureInfo.InvariantCulture);
            string encoded = Base64Url(Encoding.UTF8.GetBytes(payload));
            string signature = Sign(key, encoded);

            return new VoiceTokenDto
            {
                Provider = string.IsNullOrWhiteSpace(_options.Voice.Provider) ? "photon" : _options.Voice.Provider,
                Channel = channel,
                Token = encoded + "." + signature,
                ExpiresIn = lifetime,
                ServerTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        /// <summary>
        /// Validates a token handed back by the voice provider's custom auth webhook.
        /// Returns the account id on success.
        /// </summary>
        public bool TryValidate(string token, out string accountId, out string channel, out string error)
        {
            accountId = null;
            channel = null;
            error = null;

            string key = SigningKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                error = "Voice is not configured on this server.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(token) || token.Length > 1024)
            {
                error = "Missing token.";
                return false;
            }

            string[] parts = token.Split('.');
            if (parts.Length != 2)
            {
                error = "Malformed token.";
                return false;
            }

            string expected = Sign(key, parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected),
                    Encoding.UTF8.GetBytes(parts[1])))
            {
                error = "Bad signature.";
                return false;
            }

            try
            {
                string payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
                string[] fields = payload.Split('|');
                if (fields.Length != 3)
                {
                    error = "Malformed payload.";
                    return false;
                }

                if (!long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expiresAt) ||
                    expiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    error = "Token expired.";
                    return false;
                }

                accountId = fields[0];
                channel = fields[1];
                return true;
            }
            catch (Exception)
            {
                error = "Malformed payload.";
                return false;
            }
        }

        /// <summary>
        /// Photon custom-auth response shape. Enable it in the Photon dashboard under
        /// Authentication -> Custom Authentication with this URL.
        /// </summary>
        public static object PhotonAuthResponse(bool ok, string userId, string message)
        {
            return ok
                ? new Dictionary<string, object> { { "ResultCode", 1 }, { "UserId", userId }, { "Message", string.Empty } }
                : new Dictionary<string, object> { { "ResultCode", 2 }, { "Message", message ?? "denied" } };
        }

        private string SigningKey()
        {
            return string.IsNullOrWhiteSpace(_options.Voice.TokenSigningKey)
                ? _options.TokenSigningKey
                : _options.Voice.TokenSigningKey;
        }

        private static string Sign(string key, string data)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Convert.FromBase64String(padded);
        }
    }
}
