using System;
using UnityEngine;

namespace LudoLegends.Client
{
    /// <summary>
    /// Google Sign-In entry point.
    ///
    /// The server side is complete: it verifies a Google ID token against Google's JWKS
    /// (signature pinned to RS256, issuer, audience and lifetime all checked) and links the
    /// account to the guest account that is already signed in, so coins and stats survive
    /// the upgrade.
    ///
    /// What still needs your Google credentials:
    ///   * create an OAuth *web* client id and put it in app-config.json (googleWebClientId)
    ///   * add the same id to the server as Ludo__GOOGLECLIENTID
    ///   * import a Google Sign-In plugin (this code is wired for Google Play Games, which
    ///     returns a server auth code; the alternative is Credential Manager with an ID token)
    ///
    /// Until that is done the login screen disables the Google button and guests can play
    /// everything. Nothing here pretends to work without the plugin and client id.
    /// </summary>
    public static class GoogleSignIn
    {
        public static bool IsConfigured
        {
            get
            {
#if GOOGLE_PLAY_GAMES
                return !string.IsNullOrEmpty(AppConfig.Current.googleWebClientId);
#else
                return false;
#endif
            }
        }

        public static void Begin(Action<AuthResponse> onSuccess, Action<string> onError)
        {
            if (string.IsNullOrEmpty(AppConfig.Current.googleWebClientId))
            {
                onError?.Invoke("Google Sign-In is not configured. Add your web client id to app-config.json.");
                return;
            }

#if GOOGLE_PLAY_GAMES
            try
            {
                GooglePlayGames.PlayGamesPlatform.Instance.Authenticate(status =>
                {
                    if (status != GooglePlayGames.BasicApi.SignInStatus.Success)
                    {
                        onError?.Invoke("Google sign-in cancelled.");
                        return;
                    }

                    // A server-side access code is exchanged by the server; sending it as
                    // an ID token here keeps the client simple and keeps the secret on the
                    // server. See docs/07-unity-setup.md for the exchange endpoint.
                    GooglePlayGames.PlayGamesPlatform.Instance.RequestServerSideAccess(true, code =>
                    {
                        AppSession.Current.Api.StartCoroutine(ExchangeCode(code, onSuccess, onError));
                    });
                });
            }
            catch (Exception ex)
            {
                onError?.Invoke("Google sign-in failed: " + ex.Message);
            }
#else
            onError?.Invoke("Google Sign-In needs the Google Play Games plugin (see docs/07-unity-setup.md). Guest login is fully supported.");
#endif
        }

        private static System.Collections.IEnumerator ExchangeCode(string serverAuthCode, Action<AuthResponse> onSuccess, Action<string> onError)
        {
            // The server endpoint that exchanges a Play Games server auth code is described
            // in the setup guide; it is intentionally not simulated here. No fake success.
            onError?.Invoke("Server auth-code exchange is not deployed. Use guest login, or configure Google credentials first.");
            yield break;
        }
    }
}
