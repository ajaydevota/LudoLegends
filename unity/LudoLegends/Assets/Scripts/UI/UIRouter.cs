using System;
using System.Collections;
using System.Collections.Generic;
using LudoLegends.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LudoLegends.Client
{
    public enum AppScreen
    {
        Splash,
        Login,
        Menu,
        Modes,
        Matchmaking,
        Room,
        Game,
        Shop,
        Daily,
        Leaderboard,
        Friends,
        Settings,
        Profile,
        History,
        Result
    }

    /// <summary>
    /// Screen navigation and shared chrome.
    ///
    /// Screens are rebuilt on demand from code, which keeps memory low on budget devices
    /// and avoids stale state after a reconnect. Only the game screen keeps long-lived
    /// objects (board and tokens).
    /// </summary>
    public sealed partial class UIRouter
    {
        private readonly AppBootstrap _app;
        private readonly AppSession _session;
        private RectTransform _body;
        private Text _title;
        private Text _coinsLabel;
        private RectTransform _toastRoot;
        private Coroutine _toastRoutine;
        private GameScreen _gameScreen;
        private AppScreen _current = AppScreen.Splash;

        public UIRouter(AppBootstrap app, AppSession session)
        {
            _app = app;
            _session = session;
            _session.Socket.EnvelopeReceived += OnEnvelope;
        }

        public AppScreen Current
        {
            get { return _current; }
        }

        // UIRouter is a plain class, not a MonoBehaviour (screens are built from code), so
        // coroutines are hosted by the bootstrap object. They therefore survive screen
        // rebuilds, which is exactly what request/refresh flows need.
        private Coroutine StartCoroutine(IEnumerator routine)
        {
            return _app.StartCoroutine(routine);
        }

        private void StopCoroutine(Coroutine routine)
        {
            if (routine != null)
            {
                _app.StopCoroutine(routine);
            }
        }

        // ------------------------------------------------------------- chrome

        private void Clear()
        {
            if (_gameScreen != null)
            {
                _gameScreen.Dispose();
                _gameScreen = null;
            }

            UiKit.Clear(_app.ScreenRoot);
            Image background = UiKit.Panel(_app.ScreenRoot, "Background", UiKit.Parchment);
            background.transform.SetAsFirstSibling();
        }

        /// <summary>Creates the standard header/body frame and returns the body container.</summary>
        private RectTransform Frame(string title, bool showBack, bool showCoins = true)
        {
            Clear();

            RectTransform header = UiKit.Panel(_app.ScreenRoot, "Header", UiKit.Primary).rectTransform;
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 190f);
            header.anchoredPosition = Vector2.zero;

            _title = UiKit.Label(header, title, 46, Color.white);
            _title.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _title.rectTransform.sizeDelta = new Vector2(700f, 70f);

            if (showBack)
            {
                Button back = UiKit.Button(header, "<", () => ShowMenu(), new Color(1f, 1f, 1f, 0.18f), 42);
                RectTransform rect = back.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(110f, 84f);
                rect.anchoredPosition = new Vector2(28f, 0f);
            }

            if (showCoins)
            {
                Image pill = UiKit.RoundedPanel(header, "Coins", new Color(1f, 1f, 1f, 0.2f), 30f);
                RectTransform pillRect = pill.rectTransform;
                pillRect.anchorMin = new Vector2(1f, 0.5f);
                pillRect.anchorMax = new Vector2(1f, 0.5f);
                pillRect.pivot = new Vector2(1f, 0.5f);
                pillRect.sizeDelta = new Vector2(230f, 72f);
                pillRect.anchoredPosition = new Vector2(-28f, 0f);

                _coinsLabel = UiKit.Label(pill.transform, "0", 32, UiKit.Ink);
                _coinsLabel.rectTransform.offsetMin = new Vector2(0f, 0f);
                _coinsLabel.rectTransform.offsetMax = new Vector2(-18f, 0f);
                _coinsLabel.alignment = TextAnchor.MiddleRight;
                RefreshCoins();
            }

            RectTransform body = UiKit.Rect(_app.ScreenRoot, "Body");
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(48f, 48f);
            body.offsetMax = new Vector2(-48f, -215f);

            _body = body;
            return body;
        }

        public void RefreshCoins()
        {
            if (_coinsLabel != null)
            {
                _coinsLabel.text = _session.Coins + " coins";
            }
        }

        public void RefreshProfile()
        {
            StartCoroutine(RefreshProfileRoutine());
        }

        private IEnumerator RefreshProfileRoutine()
        {
            yield return _session.Api.GetMe(
                me =>
                {
                    _session.Profile = me.profile;
                    _session.Stats = me.stats;
                    RefreshCoins();
                },
                error => Debug.LogWarning("Profile refresh failed: " + error));
        }

        public void Toast(string message)
        {
            if (_toastRoot != null)
            {
                UnityEngine.Object.Destroy(_toastRoot.gameObject);
            }

            Image toast = UiKit.RoundedPanel(_app.OverlayRoot, "Toast", new Color(0.11f, 0.08f, 0.06f, 0.92f), 26f);
            _toastRoot = toast.rectTransform;
            _toastRoot.anchorMin = new Vector2(0.5f, 0f);
            _toastRoot.anchorMax = new Vector2(0.5f, 0f);
            _toastRoot.pivot = new Vector2(0.5f, 0f);
            _toastRoot.sizeDelta = new Vector2(860f, 110f);
            _toastRoot.anchoredPosition = new Vector2(0f, 70f);

            Text label = UiKit.Label(toast.transform, message, 30, Color.white);
            label.rectTransform.offsetMin = new Vector2(24f, 12f);
            label.rectTransform.offsetMax = new Vector2(-24f, -12f);

            if (_toastRoutine != null)
            {
                _app.StopCoroutine(_toastRoutine);
            }

            _toastRoutine = _app.StartCoroutine(HideToast(toast));
        }

        private IEnumerator HideToast(Image toast)
        {
            yield return new WaitForSeconds(2.6f);
            if (toast == null)
            {
                yield break;
            }

            float elapsed = 0f;
            Color start = toast.color;
            while (elapsed < 0.3f && toast != null)
            {
                elapsed += Time.deltaTime;
                toast.color = new Color(start.r, start.g, start.b, Mathf.Lerp(start.a, 0f, elapsed / 0.3f));
                yield return null;
            }

            if (toast != null)
            {
                UnityEngine.Object.Destroy(toast.gameObject);
            }

            _toastRoot = null;
        }

        // ------------------------------------------------------------- screens

        public void ShowSplash()
        {
            _current = AppScreen.Splash;
            Clear();

            Image card = UiKit.RoundedPanel(_app.ScreenRoot, "Logo", UiKit.Primary, 48f);
            RectTransform rect = card.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(760f, 420f);

            Text logo = UiKit.Label(card.transform, "LUDO\nLEGENDS", 86, Color.white);
            logo.rectTransform.offsetMin = new Vector2(0f, 70f);
            logo.rectTransform.offsetMax = new Vector2(0f, -40f);

            Text tagline = UiKit.Label(card.transform, "Original board game  -  play online", 26, new Color(1f, 1f, 1f, 0.8f));
            tagline.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            tagline.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            tagline.rectTransform.pivot = new Vector2(0.5f, 0f);
            tagline.rectTransform.sizeDelta = new Vector2(700f, 60f);
            tagline.rectTransform.anchoredPosition = new Vector2(0f, 48f);

            Image spinner = UiKit.Panel(_app.ScreenRoot, "Spinner", UiKit.Accent);
            spinner.sprite = Sprites.Circle();
            RectTransform spinnerRect = spinner.rectTransform;
            spinnerRect.anchorMin = new Vector2(0.5f, 0f);
            spinnerRect.anchorMax = new Vector2(0.5f, 0f);
            spinnerRect.sizeDelta = new Vector2(60f, 60f);
            spinnerRect.anchoredPosition = new Vector2(0f, 220f);
            spinner.gameObject.AddComponent<Spin>();
        }

        public void ShowMenu()
        {
            _current = AppScreen.Menu;
            Clear();

            RectTransform body = UiKit.Rect(_app.ScreenRoot, "Body");
            body.anchorMin = new Vector2(0.5f, 0f);
            body.anchorMax = new Vector2(0.5f, 1f);

            // Player card
            Image card = UiKit.RoundedPanel(_app.ScreenRoot, "PlayerCard", UiKit.Card, 32f);
            RectTransform cardRect = card.rectTransform;
            cardRect.anchorMin = new Vector2(0f, 1f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.offsetMin = new Vector2(48f, 0f);
            cardRect.offsetMax = new Vector2(-48f, -56f);
            cardRect.sizeDelta = new Vector2(cardRect.sizeDelta.x, 260f);

            Image avatar = UiKit.Panel(card.transform, "Avatar", UiKit.ColorFor(0));
            avatar.sprite = Sprites.Circle();
            RectTransform avatarRect = avatar.rectTransform;
            avatarRect.anchorMin = new Vector2(0f, 0.5f);
            avatarRect.anchorMax = new Vector2(0f, 0.5f);
            avatarRect.pivot = new Vector2(0f, 0.5f);
            avatarRect.sizeDelta = new Vector2(150f, 150f);
            avatarRect.anchoredPosition = new Vector2(36f, 0f);

            Text initial = UiKit.Label(avatar.transform, InitialOf(_session.Profile?.displayName), 64, Color.white);

            Text name = UiKit.Label(card.transform, _session.Profile?.displayName ?? "Player", 40, UiKit.Ink, TextAnchor.MiddleLeft);
            name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            name.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            name.rectTransform.offsetMin = new Vector2(214f, 40f);
            name.rectTransform.offsetMax = new Vector2(-210f, 84f);

            Text meta = UiKit.Label(card.transform,
                "Level " + (_session.Profile?.level ?? 1) + "   -   " + (_session.Profile?.trophies ?? 0) + " trophies   -   code " + (_session.Profile?.playerCode ?? "------"),
                24, UiKit.Muted, TextAnchor.MiddleLeft);
            meta.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            meta.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            meta.rectTransform.offsetMin = new Vector2(214f, -30f);
            meta.rectTransform.offsetMax = new Vector2(-40f, 10f);

            Image coinPill = UiKit.RoundedPanel(card.transform, "Coins", new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.22f), 24f);
            RectTransform coinRect = coinPill.rectTransform;
            coinRect.anchorMin = new Vector2(1f, 0.5f);
            coinRect.anchorMax = new Vector2(1f, 0.5f);
            coinRect.pivot = new Vector2(1f, 0.5f);
            coinRect.sizeDelta = new Vector2(200f, 72f);
            coinRect.anchoredPosition = new Vector2(-36f, 30f);

            Text coins = UiKit.Label(coinPill.transform, _session.Coins.ToString(), 30, UiKit.Ink);
            coins.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Main actions
            BuildMenuButton("Play", 0f, () => ShowModes(), UiKit.Primary);
            BuildMenuButton("Daily reward", 1f, () => ShowDaily(), UiKit.Success);
            BuildMenuButton("Shop", 2f, () => ShowShop(), UiKit.Accent);
            BuildMenuButton("Leaderboard", 3f, () => ShowLeaderboard(), UiKit.PrimaryDark);
            BuildMenuButton("Friends", 4f, () => ShowFriends(), UiKit.PrimaryDark);
            BuildMenuButton("Profile", 5f, () => ShowProfile(), UiKit.PrimaryDark);
            BuildMenuButton("Settings", 6f, () => ShowSettings(), UiKit.Muted);
        }

        private void BuildMenuButton(string text, float index, Action action, Color color)
        {
            Button button = UiKit.Button(_app.ScreenRoot, text, action, color, 34);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(48f, 0f);
            rect.offsetMax = new Vector2(-48f, 0f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, 132f);
            rect.anchoredPosition = new Vector2(0f, -356f - index * 150f);
        }

        private static string InitialOf(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "?" : name.Trim().Substring(0, 1).ToUpperInvariant();
        }

        // ------------------------------------------------------------- login

        public void ShowLogin()
        {
            _current = AppScreen.Login;
            Clear();

            Image logo = UiKit.RoundedPanel(_app.ScreenRoot, "Logo", UiKit.Primary, 42f);
            RectTransform logoRect = logo.rectTransform;
            logoRect.anchorMin = new Vector2(0.5f, 1f);
            logoRect.anchorMax = new Vector2(0.5f, 1f);
            logoRect.pivot = new Vector2(0.5f, 1f);
            logoRect.sizeDelta = new Vector2(760f, 250f);
            logoRect.anchoredPosition = new Vector2(0f, -140f);

            UiKit.Label(logo.transform, "LUDO LEGENDS", 62, Color.white);

            Image card = UiKit.RoundedPanel(_app.ScreenRoot, "LoginCard", UiKit.Card, 36f);
            RectTransform cardRect = card.rectTransform;
            cardRect.anchorMin = new Vector2(0.5f, 1f);
            cardRect.anchorMax = new Vector2(0.5f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.sizeDelta = new Vector2(880f, 620f);
            cardRect.anchoredPosition = new Vector2(0f, -430f);

            Text heading = UiKit.Label(card.transform, "Choose how to play", 34, UiKit.Ink);
            heading.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            heading.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            heading.rectTransform.pivot = new Vector2(0.5f, 1f);
            heading.rectTransform.sizeDelta = new Vector2(760f, 60f);
            heading.rectTransform.anchoredPosition = new Vector2(0f, -32f);

            InputField nameInput = UiKit.Input(card.transform, "Display name (optional)");
            RectTransform inputRect = nameInput.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.5f, 1f);
            inputRect.anchorMax = new Vector2(0.5f, 1f);
            inputRect.pivot = new Vector2(0.5f, 1f);
            inputRect.sizeDelta = new Vector2(740f, 116f);
            inputRect.anchoredPosition = new Vector2(0f, -116f);

            Button guest = UiKit.Button(card.transform, "Play as guest", () => StartCoroutine(GuestLogin(nameInput.text)), UiKit.Primary, 36);
            RectTransform guestRect = guest.GetComponent<RectTransform>();
            guestRect.anchorMin = new Vector2(0.5f, 1f);
            guestRect.anchorMax = new Vector2(0.5f, 1f);
            guestRect.pivot = new Vector2(0.5f, 1f);
            guestRect.sizeDelta = new Vector2(740f, 140f);
            guestRect.anchoredPosition = new Vector2(0f, -264f);

            Button google = UiKit.Button(card.transform,
                string.IsNullOrEmpty(AppConfig.Current.googleWebClientId) ? "Google sign-in (not configured)" : "Sign in with Google",
                () => GoogleSignIn.Begin(response => OnSignedIn(response), error => Toast(error)),
                new Color(0.2f, 0.35f, 0.7f),
                32);
            RectTransform googleRect = google.GetComponent<RectTransform>();
            googleRect.anchorMin = new Vector2(0.5f, 1f);
            googleRect.anchorMax = new Vector2(0.5f, 1f);
            googleRect.pivot = new Vector2(0.5f, 1f);
            googleRect.sizeDelta = new Vector2(740f, 140f);
            googleRect.anchoredPosition = new Vector2(0f, -424f);

            if (string.IsNullOrEmpty(AppConfig.Current.googleWebClientId))
            {
                google.interactable = false;
            }

            Text note = UiKit.Label(card.transform,
                "Guest progress is saved to this device. Linking Google later keeps your coins.",
                22, UiKit.Muted);
            note.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            note.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            note.rectTransform.pivot = new Vector2(0.5f, 0f);
            note.rectTransform.sizeDelta = new Vector2(760f, 80f);
            note.rectTransform.anchoredPosition = new Vector2(0f, 24f);
        }

        private IEnumerator GuestLogin(string displayName)
        {
            Toast("Signing in...");
            yield return _session.Api.GuestLogin(_session.DeviceId(), displayName, OnSignedIn, Toast);
        }

        private void OnSignedIn(AuthResponse response)
        {
            _session.SaveToken(response.token);
            _session.Profile = response.profile;
            _session.Api.Token = response.token;
            _app.ConnectSocket();
            ShowMenu();
        }

        // ------------------------------------------------------------- modes

        public void ShowModes()
        {
            _current = AppScreen.Modes;
            RectTransform body = Frame("Play", true);

            AddModeCard(body, "Online match", "Automatic matchmaking, 2 to 4 players", 0f, () => ShowMatchmaking());
            AddModeCard(body, "Private room", "Create a code and invite friends", 1f, () => ShowRoom());
            AddModeCard(body, "Play vs bots", "Offline, no connection needed", 2f, () => StartOfflineVsBots());
            AddModeCard(body, "Pass and play", "Two players on one device", 3f, () => StartPassAndPlay());
        }

        private void AddModeCard(RectTransform body, string title, string subtitle, float index, Action action)
        {
            Button card = UiKit.Button(body, string.Empty, action, UiKit.Card, 30);
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, 210f);
            rect.anchoredPosition = new Vector2(0f, -index * 236f);

            // Left colour bar
            Image bar = UiKit.Panel(rect, "Bar", UiKit.ColorFor((int)index));
            bar.rectTransform.anchorMin = new Vector2(0f, 0f);
            bar.rectTransform.anchorMax = new Vector2(0f, 1f);
            bar.rectTransform.pivot = new Vector2(0f, 0.5f);
            bar.rectTransform.sizeDelta = new Vector2(18f, 0f);

            Text titleText = UiKit.Label(rect, title, 38, UiKit.Ink, TextAnchor.MiddleLeft);
            titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleText.rectTransform.offsetMin = new Vector2(48f, -110f);
            titleText.rectTransform.offsetMax = new Vector2(-32f, -26f);

            Text subtitleText = UiKit.Label(rect, subtitle, 26, UiKit.Muted, TextAnchor.MiddleLeft);
            subtitleText.rectTransform.anchorMin = new Vector2(0f, 0f);
            subtitleText.rectTransform.anchorMax = new Vector2(1f, 0f);
            subtitleText.rectTransform.offsetMin = new Vector2(48f, 28f);
            subtitleText.rectTransform.offsetMax = new Vector2(-32f, 96f);

            // Buttons inside a Button are not clickable; the card itself carries the action.
            titleText.raycastTarget = false;
            subtitleText.raycastTarget = false;
            bar.raycastTarget = false;
        }
    }

    /// <summary>Small spinner used by the splash and loading states.</summary>
    public sealed class Spin : MonoBehaviour
    {
        private void Update()
        {
            transform.Rotate(0f, 0f, -360f * Time.deltaTime);
        }
    }
}
