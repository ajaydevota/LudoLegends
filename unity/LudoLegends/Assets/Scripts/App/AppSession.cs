using System;
using LudoLegends.Core;
using UnityEngine;

namespace LudoLegends.Client
{
    /// <summary>
    /// Holds the live session: tokens, profile, cached room and wallet.
    ///
    /// The session token is stored in PlayerPrefs (the standard place for it on Android;
    /// it is app-private storage). It is a short-lived, revocable server token, not a
    /// password, and it grants nothing the server does not re-check.
    /// </summary>
    public sealed class AppSession
    {
        private const string TokenKey = "ludo.token";
        private const string DeviceKey = "ludo.device";

        public static AppSession Current { get; } = new AppSession();

        public string Token { get; set; }
        public ProfileDto Profile { get; set; }
        public StatsDto Stats { get; set; }
        public RoomSnapshot Room { get; set; }
        public int MySeat { get; set; } = -1;

        public ApiClient Api { get; set; }
        public SocketClient Socket { get; set; }
        public GameController Game { get; set; }
        public UIRouter Router { get; set; }

        public bool SignedIn
        {
            get { return !string.IsNullOrEmpty(Token) && Profile != null; }
        }

        public long Coins
        {
            get { return Profile?.coins ?? 0; }
        }

        /// <summary>Stable installation id used for guest accounts. Never a hardware id.</summary>
        public string DeviceId()
        {
            string id = PlayerPrefs.GetString(DeviceKey, string.Empty);
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceKey, id);
                PlayerPrefs.Save();
            }

            return id;
        }

        public void SaveToken(string token)
        {
            Token = token;
            PlayerPrefs.SetString(TokenKey, token ?? string.Empty);
            PlayerPrefs.Save();
        }

        public string LoadToken()
        {
            return PlayerPrefs.GetString(TokenKey, string.Empty);
        }

        public void SignOut()
        {
            if (Socket != null)
            {
                Socket.LeaveRoom();
                Socket.Disconnect();
            }

            Token = null;
            Profile = null;
            Stats = null;
            Room = null;
            MySeat = -1;
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.Save();
        }

        public void SetCoins(long coins)
        {
            if (Profile != null)
            {
                Profile.coins = coins;
            }
        }
    }
}
