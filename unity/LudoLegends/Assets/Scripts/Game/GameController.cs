using System;
using System.Collections;
using System.Collections.Generic;
using LudoLegends.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LudoLegends.Client
{
    /// <summary>
    /// Drives a match: authoritative snapshots from the server, or a local engine for the
    /// offline modes. Both paths run the *same* rules assembly, so offline play cannot drift
    /// from online rules.
    ///
    /// The controller never invents state. It renders snapshots, plays the events that came
    /// with them, and sends intents when the player taps.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private SocketClient _socket;
        private string _myAccountId;
        private int _mySeat = -1;
        private GameSnapshot _state;
        private readonly Queue<ServerEnvelope> _pending = new Queue<ServerEnvelope>();
        private bool _playingEvents;
        private BoardView _board;

        // Offline
        private LudoGame _local;
        private bool _offline;
        private bool _passAndPlay;
        private System.Random _random;

        public event Action<GameSnapshot, int, int> SnapshotUpdated; // state, mySeat, remainingMs
        public event Action<int> Rolled;                             // die value
        public event Action<string> Rejected;
        public event Action<GameSnapshot> Finished;
        public event Action<string> Notice;

        public GameSnapshot State
        {
            get { return _state; }
        }

        public int MySeat
        {
            get { return _mySeat; }
        }

        public bool IsOffline
        {
            get { return _offline; }
        }

        public bool CanRoll
        {
            get
            {
                return _state != null &&
                       _state.Phase == (int)GamePhase.AwaitingRoll &&
                       _state.ActiveSeat == _mySeat &&
                       !_state.Seats[_mySeat].Finished;
            }
        }

        public bool CanMove
        {
            get
            {
                return _state != null &&
                       _state.Phase == (int)GamePhase.AwaitingMove &&
                       _state.ActiveSeat == _mySeat;
            }
        }

        public void Bind(BoardView board)
        {
            _board = board;
            _board.TokenTapped += OnTokenTapped;
        }

        // ------------------------------------------------------------- online

        public void BindOnline(SocketClient socket, string myAccountId)
        {
            _offline = false;
            _socket = socket;
            _myAccountId = myAccountId;
            _pending.Clear();
            _state = null;
            _mySeat = -1;

            _socket.EnvelopeReceived -= OnEnvelope;
            _socket.EnvelopeReceived += OnEnvelope;
        }

        public void Unbind()
        {
            if (_socket != null)
            {
                _socket.EnvelopeReceived -= OnEnvelope;
            }

            if (_board != null)
            {
                _board.TokenTapped -= OnTokenTapped;
            }
        }

        private void OnEnvelope(ServerEnvelope envelope)
        {
            if (envelope == null)
            {
                return;
            }

            switch (envelope.Type)
            {
                case MessageTypes.RoomState:
                    if (envelope.Room != null)
                    {
                        ResolveSeat(envelope.Room);
                    }

                    break;

                case MessageTypes.GameStateMsg:
                    _pending.Enqueue(envelope);
                    if (!_playingEvents)
                    {
                        StartCoroutine(DrainEnvelopes());
                    }

                    break;

                case MessageTypes.CommandRejected:
                    if (!string.IsNullOrEmpty(envelope.Error))
                    {
                        Rejected?.Invoke(envelope.Error);
                    }

                    break;

                case MessageTypes.PlayerConnection:
                    if (envelope.Room != null)
                    {
                        ResolveSeat(envelope.Room);
                    }

                    break;
            }
        }

        private void ResolveSeat(RoomSnapshot room)
        {
            if (room?.Members == null || string.IsNullOrEmpty(_myAccountId))
            {
                return;
            }

            foreach (RoomMemberSnapshot member in room.Members)
            {
                if (member.PlayerId == _myAccountId)
                {
                    _mySeat = member.Seat;
                    if (_board != null)
                    {
                        _board.SetPlayerSeat(_mySeat);
                    }
                }
            }
        }

        private IEnumerator DrainEnvelopes()
        {
            _playingEvents = true;

            while (_pending.Count > 0)
            {
                ServerEnvelope envelope = _pending.Dequeue();
                GameSnapshot incoming = envelope.State;
                if (incoming == null)
                {
                    continue;
                }

                // Snapshots are ordered per connection, but a reconnect can replay the
                // current state after newer messages were already applied. Never let an
                // older snapshot roll the board backwards.
                if (_state != null && incoming.Version < _state.Version)
                {
                    continue;
                }

                // Render the authoritative result first, then replay events on top so the
                // board can never disagree with the server, even if animation is skipped.
                if (envelope.Events != null)
                {
                    for (int i = 0; i < envelope.Events.Length; i++)
                    {
                        GameEvent evt = envelope.Events[i];
                        if (evt == null)
                        {
                            continue;
                        }

                        if (evt.Kind == GameEventKind.DiceRolled)
                        {
                            Rolled?.Invoke(evt.Die);
                            AudioService.Instance.PlayDice();
                        }
                        else if (evt.Kind == GameEventKind.TokenMoved && _board != null)
                        {
                            yield return _board.AnimateMove(evt);
                        }
                        else if (evt.Kind == GameEventKind.TokenCaptured && _board != null)
                        {
                            yield return _board.AnimateCapture(evt.Seat, evt.TokenIndex);
                        }
                        else if (evt.Kind == GameEventKind.TokenHome)
                        {
                            AudioService.Instance.PlayHome();
                        }
                        else if (evt.Kind == GameEventKind.TurnForfeited)
                        {
                            Notice?.Invoke(string.IsNullOrEmpty(evt.Message) ? "Turn forfeited." : evt.Message);
                        }
                        else if (evt.Kind == GameEventKind.NoLegalMoves)
                        {
                            Notice?.Invoke("No legal move - turn passes.");
                        }
                        else if (evt.Kind == GameEventKind.GameFinished)
                        {
                            if (_board != null && evt.Seat >= 0)
                            {
                                yield return _board.AnimateWin(evt.Seat);
                            }
                        }
                    }
                }

                ApplySnapshot(incoming, incoming.LegalTokens, envelope.TurnDeadlineMs);

                if (incoming.Phase == (int)GamePhase.Finished)
                {
                    Finished?.Invoke(incoming);
                }
            }

            _playingEvents = false;
        }

        private void ApplySnapshot(GameSnapshot snapshot, int[] legalTokens, int remainingMs)
        {
            _state = snapshot;

            if (_board != null)
            {
                _board.Interactable = snapshot.ActiveSeat == _mySeat && snapshot.Phase == (int)GamePhase.AwaitingMove;
                _board.Render(snapshot, legalTokens, _mySeat);
            }

            SnapshotUpdated?.Invoke(snapshot, _mySeat, remainingMs);
        }

        public void RequestRoll()
        {
            if (_offline)
            {
                OfflineRoll();
                return;
            }

            if (!CanRoll)
            {
                Rejected?.Invoke("It is not your turn.");
                return;
            }

            _socket.SendRoll();
        }

        public void RequestMove(int tokenIndex)
        {
            if (_offline)
            {
                OfflineMove(tokenIndex);
                return;
            }

            if (!CanMove)
            {
                return;
            }

            _socket.SendMove(tokenIndex);
        }

        private void OnTokenTapped(int tokenIndex)
        {
            if (_offline)
            {
                OfflineMove(tokenIndex);
            }
            else
            {
                RequestMove(tokenIndex);
            }
        }

        // ------------------------------------------------------------- offline

        /// <summary>Starts a fully local match: pass-and-play or versus AI seats.</summary>
        public void StartOffline(int seatCount, bool passAndPlay, bool[] bots)
        {
            _offline = true;
            _passAndPlay = passAndPlay;
            _random = new System.Random(Environment.TickCount);
            _mySeat = 0;

            var seats = new List<SeatDefinition>();
            PlayerColor[] colors = BoardTopology.ColorsForSeatCount(seatCount);
            for (int i = 0; i < seatCount; i++)
            {
                bool isBot = bots != null && i < bots.Length && bots[i];
                seats.Add(new SeatDefinition("local" + i, isBot ? "Bot " + (i + 1) : "Player " + (i + 1), colors[i], isBot));
            }

            _local = LudoGame.Start(GameRules.Standard(), seats, new LocalRandom(_random));

            if (_board != null)
            {
                _board.SetPlayerSeat(_mySeat);
            }

            PublishLocal(Array.Empty<GameEvent>());
            MaybePlayBotTurn();
        }

        private void OfflineRoll()
        {
            if (_local == null || _local.State.IsOver)
            {
                return;
            }

            if (_local.State.ActiveSeat != _mySeat && !_passAndPlay)
            {
                Rejected?.Invoke("Wait for your turn.");
                return;
            }

            CommandResult result = _local.Roll(_local.State.ActiveSeat, Guid.NewGuid().ToString("N"));
            if (!result.Accepted)
            {
                Rejected?.Invoke(result.Message);
                return;
            }

            StartCoroutine(PlayLocalResult(result));
        }

        private void OfflineMove(int tokenIndex)
        {
            if (_local == null || _local.State.Phase != GamePhase.AwaitingMove)
            {
                return;
            }

            CommandResult result = _local.MoveToken(_local.State.ActiveSeat, tokenIndex, Guid.NewGuid().ToString("N"));
            if (!result.Accepted)
            {
                Rejected?.Invoke(result.Message);
                return;
            }

            StartCoroutine(PlayLocalResult(result));
        }

        private IEnumerator PlayLocalResult(CommandResult result)
        {
            for (int i = 0; i < result.Events.Count; i++)
            {
                GameEvent evt = result.Events[i];
                if (evt.Kind == GameEventKind.DiceRolled)
                {
                    Rolled?.Invoke(evt.Die);
                    AudioService.Instance.PlayDice();
                    yield return new WaitForSeconds(0.35f);
                }
                else if (evt.Kind == GameEventKind.TokenMoved && _board != null)
                {
                    yield return _board.AnimateMove(evt);
                }
                else if (evt.Kind == GameEventKind.TokenCaptured && _board != null)
                {
                    yield return _board.AnimateCapture(evt.Seat, evt.TokenIndex);
                }
                else if (evt.Kind == GameEventKind.TokenHome)
                {
                    AudioService.Instance.PlayHome();
                }
                else if (evt.Kind == GameEventKind.GameFinished && _board != null)
                {
                    yield return _board.AnimateWin(evt.Seat);
                }
            }

            PublishLocal(result.Events.ToArray());
            MaybePlayBotTurn();
        }

        private void PublishLocal(GameEvent[] events)
        {
            GameSnapshot snapshot = GameSnapshot.From(_local.State, _local.LegalMoves(_local.State.ActiveSeat));
            int mySeat = _passAndPlay ? _local.State.ActiveSeat : _mySeat;

            if (_board != null)
            {
                _board.SetPlayerSeat(mySeat);
                _board.Interactable = snapshot.Phase == (int)GamePhase.AwaitingMove;
                _board.Render(snapshot, snapshot.LegalTokens, mySeat);
            }

            _state = snapshot;
            SnapshotUpdated?.Invoke(snapshot, mySeat, 0);

            if (snapshot.Phase == (int)GamePhase.Finished)
            {
                Finished?.Invoke(snapshot);
            }
        }

        private void MaybePlayBotTurn()
        {
            if (_local == null || _local.State.IsOver || _passAndPlay)
            {
                return;
            }

            int active = _local.State.ActiveSeat;
            if (!_local.State.Seats[active].IsBot)
            {
                return;
            }

            StartCoroutine(BotTurn());
        }

        private IEnumerator BotTurn()
        {
            yield return new WaitForSeconds(0.5f);

            if (_local == null || _local.State.IsOver)
            {
                yield break;
            }

            if (_local.State.Phase == GamePhase.AwaitingRoll)
            {
                CommandResult roll = _local.Roll(_local.State.ActiveSeat, Guid.NewGuid().ToString("N"));
                if (roll.Events.Count > 0)
                {
                    Rolled?.Invoke(roll.Die);
                    AudioService.Instance.PlayDice();
                }

                yield return new WaitForSeconds(0.35f);
                PublishLocal(roll.Events.ToArray());

                if (!roll.Accepted)
                {
                    yield break;
                }
            }

            if (_local.State.Phase == GamePhase.AwaitingMove)
            {
                MoveOption move = LudoAi.ChooseMove(_local);
                if (move != null)
                {
                    CommandResult result = _local.MoveToken(_local.State.ActiveSeat, move.TokenIndex, Guid.NewGuid().ToString("N"));
                    for (int i = 0; i < result.Events.Count; i++)
                    {
                        GameEvent evt = result.Events[i];
                        if (evt.Kind == GameEventKind.TokenMoved && _board != null)
                        {
                            yield return _board.AnimateMove(evt);
                        }
                        else if (evt.Kind == GameEventKind.TokenCaptured && _board != null)
                        {
                            yield return _board.AnimateCapture(evt.Seat, evt.TokenIndex);
                        }
                        else if (evt.Kind == GameEventKind.TokenHome)
                        {
                            AudioService.Instance.PlayHome();
                        }
                        else if (evt.Kind == GameEventKind.GameFinished && _board != null)
                        {
                            yield return _board.AnimateWin(evt.Seat);
                        }
                    }

                    PublishLocal(result.Events.ToArray());
                }
            }

            MaybePlayBotTurn();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        /// <summary>Deterministic-ish local die source; only used when there is nobody to cheat.</summary>
        private sealed class LocalRandom : IRandomSource
        {
            private readonly System.Random _random;

            public LocalRandom(System.Random random)
            {
                _random = random;
            }

            public int NextDie()
            {
                return _random.Next(1, 7);
            }
        }
    }
}
