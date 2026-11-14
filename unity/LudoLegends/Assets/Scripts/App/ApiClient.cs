using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace LudoLegends.Client
{
    // ------------------------------------------------------------------ DTOs
    // These mirror the server's anonymous response objects. Field names match the
    // camelCase JSON produced by System.Text.Json.

    [Serializable]
    public sealed class ProfileDto
    {
        public string accountId;
        public string playerCode;
        public string displayName;
        public string avatarId;
        public bool isGuest;
        public int level;
        public int xp;
        public int xpIntoLevel;
        public int xpForNextLevel;
        public int trophies;
        public long coins;
        public bool adsRemoved;
        public string[] unlockedItems = Array.Empty<string>();
    }

    [Serializable]
    public sealed class AuthResponse
    {
        public string token;
        public int expiresInDays;
        public ProfileDto profile;
    }

    [Serializable]
    public sealed class StatsDto
    {
        public int gamesPlayed;
        public int wins;
        public int losses;
        public int abandoned;
        public int trophies;
        public float winRate;
    }

    [Serializable]
    public sealed class MeResponse
    {
        public ProfileDto profile;
        public StatsDto stats;
        public int friendsOnline;
        public int pendingFriendRequests;
    }

    [Serializable]
    public sealed class WalletResponse
    {
        public long balance;
    }

    [Serializable]
    public sealed class DailyStatusDto
    {
        public bool claimedToday;
        public bool justClaimed;
        public int nextStreakDay;
        public long nextReward;
        public long[] rewards = Array.Empty<long>();
        public long balance;
        public string error;
    }

    [Serializable]
    public sealed class MissionDto
    {
        public string key;
        public string title;
        public string period;
        public string periodKey;
        public int target;
        public int progress;
        public long reward;
        public bool claimed;
    }

    [Serializable]
    public sealed class MissionListDto
    {
        public MissionDto[] items = Array.Empty<MissionDto>();
    }

    [Serializable]
    public sealed class AchievementDto
    {
        public string key;
        public string title;
        public int threshold;
        public int current;
        public long reward;
        public bool unlocked;
        public bool claimed;
    }

    [Serializable]
    public sealed class AchievementListDto
    {
        public AchievementDto[] items = Array.Empty<AchievementDto>();
    }

    [Serializable]
    public sealed class ShopItemDto
    {
        public string id;
        public string title;
        public string kind;
        public long priceCoins;
        public string description;
    }

    [Serializable]
    public sealed class ShopResponse
    {
        public ShopItemDto[] items = Array.Empty<ShopItemDto>();
        public long balance;
        public string[] owned = Array.Empty<string>();
        public bool adsRemoved;
    }

    [Serializable]
    public sealed class LeaderboardEntryDto
    {
        public int position;
        public string accountId;
        public string displayName;
        public string avatarId;
        public int level;
        public int trophies;
        public int wins;
        public int value;
    }

    [Serializable]
    public sealed class LeaderboardResponse
    {
        public string scope;
        public LeaderboardEntryDto[] items = Array.Empty<LeaderboardEntryDto>();
    }

    [Serializable]
    public sealed class FriendDto
    {
        public string accountId;
        public string playerCode;
        public string displayName;
        public string avatarId;
        public int level;
        public int trophies;
        public bool online;
        public int status;
        public string friendshipId;
        public string lastSeenUtc;
    }

    [Serializable]
    public sealed class FriendListResponse
    {
        public FriendDto[] friends = Array.Empty<FriendDto>();
        public FriendDto[] incoming = Array.Empty<FriendDto>();
        public FriendDto[] declined = Array.Empty<FriendDto>();
        public string myPlayerCode;
    }

    [Serializable]
    public sealed class MatchHistoryDto
    {
        public string matchId;
        public string roomCode;
        public int seatCount;
        public long entryFee;
        public long pot;
        public bool won;
        public bool aborted;
        public int rank;
        public long coinsDelta;
        public int tokensHome;
        public string endedUtc;
    }

    [Serializable]
    public sealed class HistoryResponse
    {
        public MatchHistoryDto[] items = Array.Empty<MatchHistoryDto>();
    }

    [Serializable]
    public sealed class VoiceTokenResponse
    {
        public string provider;
        public string channel;
        public string token;
        public int expiresIn;
        public long serverTimeMs;
    }

    [Serializable]
    public sealed class ErrorResponse
    {
        public string error;
        public string message;
    }

    /// <summary>
    /// Thin REST client. Every call authenticates with the bearer session token; the
    /// server derives the account from that token, so no user id is ever sent.
    ///
    /// Coroutine based on purpose: UnityWebRequest must run on the main thread, and this
    /// keeps the client allocation-free compared to async state machines per request.
    /// </summary>
    public sealed class ApiClient : MonoBehaviour
    {
        private const int TimeoutSeconds = 20;

        public string Token { get; set; }

        public bool HasSession
        {
            get { return !string.IsNullOrEmpty(Token); }
        }

        private string BaseUrl
        {
            get { return (AppConfig.Current.apiBaseUrl ?? string.Empty).TrimEnd('/'); }
        }

        public IEnumerator GuestLogin(string deviceId, string displayName, Action<AuthResponse> onSuccess, Action<string> onError)
        {
            var body = new Dictionary<string, object>
            {
                { "deviceId", deviceId },
                { "displayName", displayName }
            };

            yield return Send("POST", "/api/auth/guest", body, true, onSuccess, onError);
        }

        public IEnumerator GoogleLogin(string idToken, Action<AuthResponse> onSuccess, Action<string> onError)
        {
            var body = new Dictionary<string, object> { { "idToken", idToken } };
            yield return Send("POST", "/api/auth/google", body, true, onSuccess, onError);
        }

        public IEnumerator RefreshSession(Action<AuthResponse> onSuccess, Action<string> onError)
        {
            yield return Send<AuthResponse>("POST", "/api/auth/refresh", null, false, onSuccess, onError);
        }

        public IEnumerator GetMe(Action<MeResponse> onSuccess, Action<string> onError)
        {
            yield return Send<MeResponse>("GET", "/api/me", null, false, onSuccess, onError);
        }

        public IEnumerator Rename(string displayName, string avatarId, Action onSuccess, Action<string> onError)
        {
            var body = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(displayName))
            {
                body["displayName"] = displayName;
            }

            if (!string.IsNullOrEmpty(avatarId))
            {
                body["avatarId"] = avatarId;
            }

            yield return SendRaw("PATCH", "/api/me", body, onSuccess, onError);
        }

        public IEnumerator DeleteAccount(Action onSuccess, Action<string> onError)
        {
            yield return SendRaw("DELETE", "/api/me", null, onSuccess, onError);
        }

        public IEnumerator GetWallet(Action<WalletResponse> onSuccess, Action<string> onError)
        {
            yield return Send<WalletResponse>("GET", "/api/wallet", null, false, onSuccess, onError);
        }

        public IEnumerator GetDaily(Action<DailyStatusDto> onSuccess, Action<string> onError)
        {
            yield return Send<DailyStatusDto>("GET", "/api/rewards/daily", null, false, onSuccess, onError);
        }

        public IEnumerator ClaimDaily(Action<DailyStatusDto> onSuccess, Action<string> onError)
        {
            yield return Send<DailyStatusDto>("POST", "/api/rewards/daily/claim", null, false, onSuccess, onError);
        }

        public IEnumerator GetMissions(Action<MissionListDto> onSuccess, Action<string> onError)
        {
            yield return Send<MissionListDto>("GET", "/api/missions", null, false, onSuccess, onError);
        }

        public IEnumerator ClaimMission(string key, Action<WalletResponse> onSuccess, Action<string> onError)
        {
            yield return Send<WalletResponse>("POST", "/api/missions/" + UnityWebRequest.EscapeURL(key) + "/claim", null, false, onSuccess, onError);
        }

        public IEnumerator GetAchievements(Action<AchievementListDto> onSuccess, Action<string> onError)
        {
            yield return Send<AchievementListDto>("GET", "/api/achievements", null, false, onSuccess, onError);
        }

        public IEnumerator ClaimAchievement(string key, Action<WalletResponse> onSuccess, Action<string> onError)
        {
            yield return Send<WalletResponse>("POST", "/api/achievements/" + UnityWebRequest.EscapeURL(key) + "/claim", null, false, onSuccess, onError);
        }

        public IEnumerator GetShop(Action<ShopResponse> onSuccess, Action<string> onError)
        {
            yield return Send<ShopResponse>("GET", "/api/shop", null, false, onSuccess, onError);
        }

        public IEnumerator Purchase(string itemId, Action<WalletResponse> onSuccess, Action<string> onError)
        {
            var body = new Dictionary<string, object> { { "itemId", itemId } };
            yield return Send<WalletResponse>("POST", "/api/shop/purchase", body, false, onSuccess, onError);
        }

        public IEnumerator GetLeaderboard(string scope, Action<LeaderboardResponse> onSuccess, Action<string> onError)
        {
            yield return Send<LeaderboardResponse>("GET", "/api/leaderboard?scope=" + UnityWebRequest.EscapeURL(scope) + "&take=50", null, false, onSuccess, onError);
        }

        public IEnumerator GetFriends(Action<FriendListResponse> onSuccess, Action<string> onError)
        {
            yield return Send<FriendListResponse>("GET", "/api/friends", null, false, onSuccess, onError);
        }

        public IEnumerator AddFriend(string playerCode, Action onSuccess, Action<string> onError)
        {
            var body = new Dictionary<string, object> { { "playerCode", playerCode } };
            yield return SendRaw("POST", "/api/friends/request", body, onSuccess, onError);
        }

        public IEnumerator RespondToFriend(string friendshipId, bool accept, Action onSuccess, Action<string> onError)
        {
            var body = new Dictionary<string, object> { { "accept", accept } };
            yield return SendRaw("POST", "/api/friends/" + friendshipId + "/respond", body, onSuccess, onError);
        }

        public IEnumerator RemoveFriend(string accountId, Action onSuccess, Action<string> onError)
        {
            yield return SendRaw("DELETE", "/api/friends/" + accountId, null, onSuccess, onError);
        }

        public IEnumerator GetHistory(Action<HistoryResponse> onSuccess, Action<string> onError)
        {
            yield return Send<HistoryResponse>("GET", "/api/history?take=30", null, false, onSuccess, onError);
        }

        public IEnumerator GetVoiceToken(string roomCode, Action<VoiceTokenResponse> onSuccess, Action<string> onError)
        {
            var body = new Dictionary<string, object> { { "roomCode", roomCode } };
            yield return Send<VoiceTokenResponse>("POST", "/api/voice/token", body, false, onSuccess, onError);
        }

        // ------------------------------------------------------------- plumbing

        private IEnumerator Send<T>(string method, string path, object body, bool anonymous, Action<T> onSuccess, Action<string> onError)
        {
            StringContent content = null;
            if (body != null)
            {
                content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }

            using (UnityWebRequest request = new UnityWebRequest(BaseUrl + path, method))
            {
                if (content != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(content.Bytes);
                }

                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = TimeoutSeconds;

                if (!anonymous && HasSession)
                {
                    request.SetRequestHeader("Authorization", "Bearer " + Token);
                }

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(DescribeError(request));
                    yield break;
                }

                T parsed;
                try
                {
                    parsed = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    onError?.Invoke("Unexpected server response: " + ex.Message);
                    yield break;
                }

                if (parsed == null)
                {
                    onError?.Invoke("Empty server response.");
                    yield break;
                }

                onSuccess?.Invoke(parsed);
            }
        }

        private IEnumerator SendRaw(string method, string path, object body, Action onSuccess, Action<string> onError)
        {
            byte[] bytes = body == null ? null : Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body));

            using (UnityWebRequest request = new UnityWebRequest(BaseUrl + path, method))
            {
                if (bytes != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(bytes);
                }

                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = TimeoutSeconds;

                if (HasSession)
                {
                    request.SetRequestHeader("Authorization", "Bearer " + Token);
                }

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(DescribeError(request));
                    yield break;
                }

                onSuccess?.Invoke();
            }
        }

        private static string DescribeError(UnityWebRequest request)
        {
            if (request.responseCode == 401)
            {
                return "Session expired. Please sign in again.";
            }

            if (request.responseCode == 429)
            {
                return "Too many requests. Please wait a moment.";
            }

            string text = request.downloadHandler != null ? request.downloadHandler.text : null;
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    ErrorResponse error = JsonConvert.DeserializeObject<ErrorResponse>(text);
                    if (error != null && !string.IsNullOrEmpty(error.message))
                    {
                        return error.message;
                    }
                }
                catch (Exception)
                {
                    // Not JSON; fall through to the generic message.
                }
            }

            return "Network error (" + request.responseCode + "). Check your connection.";
        }
    }

    /// <summary>Minimal content container so raw bodies can be built before the request.</summary>
    public sealed class StringContent
    {
        public StringContent(string text, Encoding encoding, string contentType)
        {
            Bytes = encoding.GetBytes(text);
        }

        public byte[] Bytes { get; }
    }
}
