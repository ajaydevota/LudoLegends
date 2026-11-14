using System;
using Newtonsoft.Json;
using UnityEngine;

namespace LudoLegends.Client
{
    /// <summary>
    /// Client configuration, loaded from Assets/Resources/app-config.json.
    ///
    /// Only PUBLIC values live here: the server URL, the Google web client id, the AdMob
    /// unit ids and the Photon app id. None of them is a secret (all are visible in the
    /// shipped APK by design). No signing keys, no service accounts, no API secrets.
    /// </summary>
    [Serializable]
    public sealed class AppConfig
    {
        /// <summary>HTTP(S) origin of the authoritative server, e.g. https://ludo.example.com</summary>
        public string apiBaseUrl = "http://127.0.0.1:5080";

        /// <summary>WebSocket URL, derived from apiBaseUrl when left empty.</summary>
        public string socketUrl = "";

        /// <summary>Google OAuth *web* client id used by Google Sign-In.</summary>
        public string googleWebClientId = "";

        public string photonAppId = "";
        public string photonVoiceAppId = "";

        public bool adsEnabled = false;
        public string admobBannerUnitId = "";
        public string admobInterstitialUnitId = "";
        public string admobRewardedUnitId = "";

        public bool iapEnabled = false;
        public string iapRemoveAdsProductId = "remove_ads";

        public bool voiceEnabled = false;

        /// <summary>Turn clock shown by the HUD timer bar; keep in sync with the server.</summary>
        public float turnTimeoutSeconds = 25f;

        public string WebsocketUrl()
        {
            if (!string.IsNullOrWhiteSpace(socketUrl))
            {
                return socketUrl;
            }

            string trimmed = (apiBaseUrl ?? string.Empty).TrimEnd('/');
            if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "wss://" + trimmed.Substring("https://".Length) + "/ws";
            }

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "ws://" + trimmed.Substring("http://".Length) + "/ws";
            }

            return trimmed + "/ws";
        }

        private static AppConfig _current;

        public static AppConfig Current
        {
            get
            {
                if (_current != null)
                {
                    return _current;
                }

                TextAsset asset = Resources.Load<TextAsset>("app-config");
                if (asset != null)
                {
                    try
                    {
                        _current = JsonConvert.DeserializeObject<AppConfig>(asset.text) ?? new AppConfig();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("app-config.json is invalid: " + ex.Message);
                        _current = new AppConfig();
                    }
                }
                else
                {
                    Debug.LogWarning("No Resources/app-config.json found; using local defaults.");
                    _current = new AppConfig();
                }

                return _current;
            }
        }
    }
}
