using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LudoLegends.Client
{
    /// <summary>
    /// Entry point. Unity creates this automatically in any scene
    /// (<see cref="RuntimeInitializeOnLoadMethod"/>), so the project needs no configured
    /// scene, no prefabs and no manual wiring: press Play and the game builds its own UI.
    ///
    /// It assembles the services (API, socket, audio, game controller), restores the saved
    /// session, and hands control to <see cref="UIRouter"/>.
    /// </summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        private Canvas _canvas;
        private RectTransform _screenRoot;
        private RectTransform _overlayRoot;

        public RectTransform ScreenRoot
        {
            get { return _screenRoot; }
        }

        public RectTransform OverlayRoot
        {
            get { return _overlayRoot; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (FindObjectOfType<AppBootstrap>() != null)
            {
                return;
            }

            var go = new GameObject("LudoLegends");
            go.AddComponent<AppBootstrap>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            Input.multiTouchEnabled = false;

            BuildCanvas();
            AudioService.Ensure(gameObject);

            AppSession session = AppSession.Current;
            session.Api = gameObject.AddComponent<ApiClient>();
            session.Socket = gameObject.AddComponent<SocketClient>();
            session.Game = gameObject.AddComponent<GameController>();

            session.Router = new UIRouter(this, session);
            StartCoroutine(StartupFlow());
        }

        private void BuildCanvas()
        {
            UiKit.EnsureEventSystem();

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 0;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // 0.5 keeps the layout sane on both tall and wide phones.
            scaler.matchWidthOrHeight = 0.5f;

            _screenRoot = UiKit.Rect(canvasGo.transform, "Screens");
            _overlayRoot = UiKit.Rect(canvasGo.transform, "Overlay");

            // Warm parchment background for every screen.
            Image background = UiKit.Panel(_screenRoot, "Background", UiKit.Parchment);
            background.transform.SetAsFirstSibling();
        }

        private IEnumerator StartupFlow()
        {
            AppSession session = AppSession.Current;
            session.Router.ShowSplash();

            yield return new WaitForSeconds(0.8f);

            string stored = session.LoadToken();
            if (string.IsNullOrEmpty(stored))
            {
                session.Router.ShowLogin();
                yield break;
            }

            // Validate the stored session against the server before trusting it.
            session.Api.Token = stored;
            MeResponse me = null;
            string error = null;

            // The coroutine completes only after one of these callbacks ran, so waiting on it
            // is the synchronisation - no extra flag is needed.
            yield return session.Api.GetMe(
                response => me = response,
                failure => error = failure);

            if (me != null)
            {
                session.SaveToken(stored);
                session.Profile = me.profile;
                session.Stats = me.stats;
                ConnectSocket();
                session.Router.ShowMenu();
                yield break;
            }

            Debug.Log("Session restore failed: " + error);
            session.SignOut();
            session.Router.ShowLogin();
        }

        /// <summary>Opens the realtime connection and routes room updates into the UI.</summary>
        public void ConnectSocket()
        {
            AppSession session = AppSession.Current;
            if (session.Socket == null || session.Profile == null)
            {
                return;
            }

            session.Socket.Connect(AppConfig.Current.WebsocketUrl(), session.Token);
            session.Socket.StatusChanged -= OnSocketStatus;
            session.Socket.StatusChanged += OnSocketStatus;
        }

        /// <summary>
        /// Android lifecycle: a backgrounded app loses its socket and must never keep the
        /// microphone open. On resume the socket is reopened (or resynced when it survived),
        /// so a match continues exactly where the server left it.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            AppSession session = AppSession.Current;
            if (session == null)
            {
                return;
            }

            if (paused)
            {
                VoiceChatService.LeaveRoom();
                return;
            }

            if (session.Socket == null || !session.SignedIn)
            {
                return;
            }

            if (!session.Socket.IsOpen)
            {
                ConnectSocket();
            }
            else
            {
                // Reconnects are automatic, but an explicit resync covers the window where
                // the OS froze the process without closing the socket.
                session.Socket.SendResync();
            }
        }

        private void OnSocketStatus(string status)
        {
            AppSession session = AppSession.Current;
            if (status == "reconnecting")
            {
                session.Router.Toast("Reconnecting...");
            }
            else if (status == "open")
            {
                // Re-sync the room after any reconnect so the board never goes stale.
                if (session.Room != null)
                {
                    session.Socket.SendResync();
                }
            }
        }
    }
}
