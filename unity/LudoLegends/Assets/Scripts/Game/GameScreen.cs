using System;
using System.Collections;
using LudoLegends.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LudoLegends.Client
{
    /// <summary>
    /// In-match HUD: players, turn indicator, dice, timer, voice and emote controls.
    ///
    /// The HUD renders authoritative snapshots only. Dice taps send an intent; the face
    /// that finally settles is the value the server rolled (delivered as an event).
    /// </summary>
    public sealed class GameScreen : MonoBehaviour
    {
        private UIRouter _router;
        private AppSession _session;
        private GameController _game;
        private BoardView _board;
        private RectTransform _root;

        private readonly Image[] _pips = new Image[7];
        private Button _diceButton;
        private Text _turnLabel;
        private Text _statusLabel;
        private Image _timerFill;
        private Image[] _playerChips = new Image[4];
        private Text[] _playerNames = new Text[4];
        private Coroutine _diceRoutine;
        private bool _subscribed;

        public void Initialize(UIRouter router, AppSession session, GameController game, RectTransform root)
        {
            _router = router;
            _session = session;
            _game = game;
            _root = root;

            BuildHeader();
            BuildBoard();
            BuildFooter();

            _game.SnapshotUpdated += OnSnapshot;
            _game.Rolled += OnRolled;
            _game.Rejected += OnRejected;
            _game.Finished += OnFinished;
            _game.Notice += OnNotice;
            _subscribed = true;

            SetDiceFace(1);

            if (_game.State != null)
            {
                OnSnapshot(_game.State, _game.MySeat, 0);
            }

            TryJoinVoice();
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _game.SnapshotUpdated -= OnSnapshot;
                _game.Rolled -= OnRolled;
                _game.Rejected -= OnRejected;
                _game.Finished -= OnFinished;
                _game.Notice -= OnNotice;
                _subscribed = false;
            }

            if (_diceRoutine != null)
            {
                StopCoroutine(_diceRoutine);
                _diceRoutine = null;
            }

            // Leaving the board must also leave the voice channel: a hot microphone in the
            // menu is a privacy bug, not a feature.
            if (!_game.IsOffline)
            {
                VoiceChatService.LeaveRoom();
            }
        }

        // ------------------------------------------------------------- layout

        private void BuildHeader()
        {
            RectTransform header = UiKit.Rect(_root, "HudHeader");
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 210f);

            Image background = UiKit.Panel(header, "Bg", UiKit.Primary);
            UiKit.Stretch(background.gameObject);

            for (int i = 0; i < 4; i++)
            {
                Image chip = UiKit.RoundedPanel(header, "Chip" + i, new Color(1f, 1f, 1f, 0.15f), 18f);
                RectTransform rect = chip.rectTransform;
                rect.anchorMin = new Vector2(i / 4f, 0.5f);
                rect.anchorMax = new Vector2((i + 1) / 4f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(-12f, 150f);

                Image dot = UiKit.Panel(chip.transform, "Dot", UiKit.ColorFor(i));
                dot.sprite = Sprites.Circle();
                RectTransform dotRect = dot.rectTransform;
                dotRect.anchorMin = new Vector2(0.5f, 1f);
                dotRect.anchorMax = new Vector2(0.5f, 1f);
                dotRect.pivot = new Vector2(0.5f, 1f);
                dotRect.sizeDelta = new Vector2(46f, 46f);
                dotRect.anchoredPosition = new Vector2(0f, -10f);

                Text name = UiKit.Label(chip.transform, "-", 20, Color.white);
                name.rectTransform.anchorMin = new Vector2(0f, 0f);
                name.rectTransform.anchorMax = new Vector2(1f, 0f);
                name.rectTransform.pivot = new Vector2(0.5f, 0f);
                name.rectTransform.sizeDelta = new Vector2(-8f, 70f);
                name.rectTransform.anchoredPosition = new Vector2(0f, 8f);
                name.horizontalOverflow = HorizontalWrapMode.Wrap;

                _playerChips[i] = chip;
                _playerNames[i] = name;
            }
        }

        private void BuildBoard()
        {
            RectTransform holder = UiKit.Rect(_root, "BoardHolder");
            holder.anchorMin = new Vector2(0f, 0f);
            holder.anchorMax = new Vector2(1f, 1f);
            holder.offsetMin = new Vector2(24f, 330f);
            holder.offsetMax = new Vector2(-24f, -230f);

            _board = holder.gameObject.AddComponent<BoardView>();
            _board.Build(holder);

            // Fit the square board into the available area, preserving aspect ratio.
            _board.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
            _board.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
            _board.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
            _board.GetComponent<RectTransform>().sizeDelta = new Vector2(960f, 960f);

            AspectRatioFitter fitter = _board.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1f;

            _game.Bind(_board);
        }

        private void BuildFooter()
        {
            RectTransform footer = UiKit.Rect(_root, "HudFooter");
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, 320f);

            Image background = UiKit.Panel(footer, "Bg", UiKit.Card);
            UiKit.Stretch(background.gameObject);
            background.gameObject.AddComponent<Outline>();

            _turnLabel = UiKit.Label(footer, "Waiting...", 32, UiKit.Ink);
            _turnLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            _turnLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _turnLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            _turnLabel.rectTransform.sizeDelta = new Vector2(-40f, 70f);
            _turnLabel.rectTransform.anchoredPosition = new Vector2(0f, -12f);

            _statusLabel = UiKit.Label(footer, string.Empty, 22, UiKit.Muted);
            _statusLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            _statusLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            _statusLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            _statusLabel.rectTransform.sizeDelta = new Vector2(-40f, 40f);
            _statusLabel.rectTransform.anchoredPosition = new Vector2(0f, -80f);

            // Timer bar
            Image track = UiKit.Panel(footer, "TimerTrack", new Color(0f, 0f, 0f, 0.08f));
            RectTransform trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0f, 1f);
            trackRect.anchorMax = new Vector2(1f, 1f);
            trackRect.pivot = new Vector2(0.5f, 1f);
            trackRect.sizeDelta = new Vector2(-60f, 12f);
            trackRect.anchoredPosition = new Vector2(0f, -126f);

            _timerFill = UiKit.Panel(track.transform, "TimerFill", UiKit.Accent);
            RectTransform fillRect = _timerFill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            // Dice
            Image dice = UiKit.RoundedPanel(footer, "Dice", Color.white, 28f);
            RectTransform diceRect = dice.rectTransform;
            diceRect.anchorMin = new Vector2(0f, 0f);
            diceRect.anchorMax = new Vector2(0f, 0f);
            diceRect.pivot = new Vector2(0f, 0f);
            diceRect.sizeDelta = new Vector2(190f, 190f);
            diceRect.anchoredPosition = new Vector2(24f, 22f);

            dice.gameObject.AddComponent<Outline>();
            _diceButton = dice.gameObject.AddComponent<Button>();
            _diceButton.targetGraphic = dice;
            _diceButton.onClick.AddListener(() =>
            {
                if (!_game.CanRoll)
                {
                    return;
                }

                AudioService.Instance.PlayClick();
                AudioService.Instance.Tap();
                StartDiceSpin();
                _game.RequestRoll();
            });

            for (int i = 0; i < 7; i++)
            {
                Image pip = UiKit.Panel(dice.transform, "Pip" + i, UiKit.Ink);
                pip.sprite = Sprites.Circle(32);
                RectTransform pipRect = pip.rectTransform;
                pipRect.anchorMin = new Vector2(0.5f, 0.5f);
                pipRect.anchorMax = new Vector2(0.5f, 0.5f);
                pipRect.sizeDelta = new Vector2(28f, 28f);
                pip.raycastTarget = false;
                _pips[i] = pip;
            }

            // Leave
            Button leave = UiKit.Button(footer, "Leave", () =>
            {
                _session.Socket.LeaveRoom();
                _session.Room = null;
                Dispose();
                _router.ShowMenu();
            }, UiKit.Muted, 26);
            RectTransform leaveRect = leave.GetComponent<RectTransform>();
            leaveRect.anchorMin = new Vector2(1f, 0f);
            leaveRect.anchorMax = new Vector2(1f, 0f);
            leaveRect.pivot = new Vector2(1f, 0f);
            leaveRect.sizeDelta = new Vector2(200f, 120f);
            leaveRect.anchoredPosition = new Vector2(-24f, 40f);

            // Emotes
            Button emote = UiKit.Button(footer, "Emote", () =>
            {
                _session.Socket.SendEmote("emote_01");
            }, UiKit.PrimaryDark, 24);
            RectTransform emoteRect = emote.GetComponent<RectTransform>();
            emoteRect.anchorMin = new Vector2(1f, 0f);
            emoteRect.anchorMax = new Vector2(1f, 0f);
            emoteRect.pivot = new Vector2(1f, 0f);
            emoteRect.sizeDelta = new Vector2(200f, 110f);
            emoteRect.anchoredPosition = new Vector2(-24f, 176f);

            // Only offered when a voice provider is actually connected; otherwise there would
            // be a button that does nothing.
            if (VoiceChatService.Available)
            {
                Button mic = UiKit.Button(footer, "Mic", () =>
                {
                    VoiceChatService.ToggleMicrophone();
                    ToastVoice();
                }, UiKit.Success, 24);
                RectTransform micRect = mic.GetComponent<RectTransform>();
                micRect.anchorMin = new Vector2(1f, 0f);
                micRect.anchorMax = new Vector2(1f, 0f);
                micRect.pivot = new Vector2(1f, 0f);
                micRect.sizeDelta = new Vector2(200f, 110f);
                micRect.anchoredPosition = new Vector2(-24f, 300f);
            }
        }

        private void ToastVoice()
        {
            _router.Toast(VoiceChatService.MicrophoneEnabled ? "Microphone on" : "Microphone muted");
        }

        /// <summary>
        /// Asks the server for a short-lived, room-scoped voice token and joins the channel.
        /// The server only issues tokens to verified members of that room, so this cannot be
        /// used to listen in on a match you are not in. No provider configured = no-op.
        /// </summary>
        private void TryJoinVoice()
        {
            if (!VoiceChatService.Available || _game.IsOffline)
            {
                return;
            }

            string code = _session.Room?.Code;
            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            _session.Api.StartCoroutine(_session.Api.GetVoiceToken(code,
                token =>
                {
                    if (token == null || string.IsNullOrEmpty(token.token))
                    {
                        return;
                    }

                    VoiceChatService.JoinRoom(token.channel ?? code, token.token,
                        _session.Profile?.displayName ?? "Player");
                },
                error => Debug.Log("Voice unavailable: " + error)));
        }

        // ------------------------------------------------------------- updates

        private void OnSnapshot(GameSnapshot state, int mySeat, int remainingMs)
        {
            if (state == null)
            {
                return;
            }

            for (int i = 0; i < _playerChips.Length; i++)
            {
                bool exists = i < state.Seats.Length;
                _playerChips[i].gameObject.SetActive(exists);
                if (!exists)
                {
                    continue;
                }

                SeatSnapshot seat = state.Seats[i];
                bool active = state.ActiveSeat == i && state.Phase != (int)GamePhase.Finished;

                _playerChips[i].color = active
                    ? new Color(1f, 1f, 1f, 0.42f)
                    : new Color(1f, 1f, 1f, 0.15f);

                _playerNames[i].text = seat.DisplayName + "\n" + seat.TokensHome + "/4" +
                                       (seat.Finished ? "  done" : string.Empty) +
                                       (i == mySeat ? "  (you)" : string.Empty);

                _playerNames[i].color = active ? UiKit.Accent : Color.white;
            }

            if (state.Phase == (int)GamePhase.Finished)
            {
                _turnLabel.text = "Match finished";
                _statusLabel.text = string.Empty;
                _diceButton.interactable = false;
                return;
            }

            bool myTurn = state.ActiveSeat == mySeat;
            _turnLabel.text = myTurn
                ? (state.Phase == (int)GamePhase.AwaitingRoll ? "Your turn - tap the dice" : "Choose a token")
                : state.Seats[state.ActiveSeat].DisplayName + "'s turn";

            _turnLabel.color = myTurn ? UiKit.Primary : UiKit.Ink;
            _diceButton.interactable = _game.CanRoll;

            float window = Mathf.Max(1f, AppConfig.Current.turnTimeoutSeconds * 1000f);
            float ratio = remainingMs <= 0 ? 0f : Mathf.Clamp01(remainingMs / window);
            _timerFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
            _timerFill.color = ratio < 0.3f ? UiKit.Danger : UiKit.Accent;
        }

        private void OnRolled(int die)
        {
            if (_diceRoutine != null)
            {
                StopCoroutine(_diceRoutine);
            }

            _diceRoutine = StartCoroutine(SettleDice(die));
        }

        private IEnumerator SettleDice(int die)
        {
            for (int i = 0; i < 6; i++)
            {
                SetDiceFace(UnityEngine.Random.Range(1, 7));
                yield return new WaitForSeconds(0.05f);
            }

            SetDiceFace(Mathf.Clamp(die, 1, 6));
            AudioService.Instance.PlayDice();
            _diceRoutine = null;
        }

        private void StartDiceSpin()
        {
            if (_diceRoutine != null)
            {
                StopCoroutine(_diceRoutine);
            }

            _diceRoutine = StartCoroutine(SpinDice());
        }

        private IEnumerator SpinDice()
        {
            for (int i = 0; i < 20; i++)
            {
                SetDiceFace(UnityEngine.Random.Range(1, 7));
                yield return new WaitForSeconds(0.05f);
            }

            _diceRoutine = null;
        }

        private void SetDiceFace(int face)
        {
            Vector2[] layout = PipLayout(face);
            for (int i = 0; i < _pips.Length; i++)
            {
                _pips[i].gameObject.SetActive(i < layout.Length);
                if (i < layout.Length)
                {
                    _pips[i].rectTransform.anchoredPosition = layout[i] * 52f;
                }
            }
        }

        private static Vector2[] PipLayout(int face)
        {
            switch (face)
            {
                case 1:
                    return new[] { Vector2.zero };
                case 2:
                    return new[] { new Vector2(-1f, 1f), new Vector2(1f, -1f) };
                case 3:
                    return new[] { new Vector2(-1f, 1f), Vector2.zero, new Vector2(1f, -1f) };
                case 4:
                    return new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f) };
                case 5:
                    return new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-1f, -1f), new Vector2(1f, -1f) };
                default:
                    return new[]
                    {
                        new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, 0f),
                        new Vector2(1f, 0f), new Vector2(-1f, -1f), new Vector2(1f, -1f)
                    };
            }
        }

        private void OnRejected(string message)
        {
            AudioService.Instance.PlayError();
            if (!string.IsNullOrEmpty(message))
            {
                _router.Toast(message);
            }
        }

        private void OnNotice(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _statusLabel.text = message;
            }
        }

        private void OnFinished(GameSnapshot state)
        {
            StartCoroutine(ShowResultSoon(state));
        }

        private IEnumerator ShowResultSoon(GameSnapshot state)
        {
            yield return new WaitForSeconds(1.2f);
            _router.ShowResult(state, _game.IsOffline);
        }
    }
}
