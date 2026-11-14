using System;
using System.Collections;
using System.Collections.Generic;
using LudoLegends.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LudoLegends.Client
{
    /// <summary>Second half of the router: every functional screen after the menu.</summary>
    public sealed partial class UIRouter
    {
        private int _seats = 2;
        private long _entryFee = 0;
        private string _joinCode = string.Empty;
        private string _leaderboardScope = "global";

        // ------------------------------------------------------------- matchmaking

        public void ShowMatchmaking()
        {
            _current = AppScreen.Matchmaking;
            RectTransform body = Frame("Online match", true);

            Text seatsLabel = UiKit.Label(body, "Players", 28, UiKit.Muted, TextAnchor.MiddleLeft);
            seatsLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            seatsLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            seatsLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            seatsLabel.rectTransform.sizeDelta = new Vector2(0f, 60f);
            seatsLabel.rectTransform.anchoredPosition = Vector2.zero;

            Button[] seatButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int count = i + 2;
                seatButtons[i] = UiKit.Button(body, count + "P", () =>
                {
                    _seats = count;
                    ShowMatchmaking();
                }, _seats == count ? UiKit.Primary : UiKit.Muted, 34);
                RectTransform rect = seatButtons[i].GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(i / 3f, 1f);
                rect.anchorMax = new Vector2((i + 1) / 3f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(-24f, 140f);
                rect.anchoredPosition = new Vector2(0f, -66f);
            }

            Text feeLabel = UiKit.Label(body, "Table", 28, UiKit.Muted, TextAnchor.MiddleLeft);
            feeLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            feeLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            feeLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            feeLabel.rectTransform.sizeDelta = new Vector2(0f, 60f);
            feeLabel.rectTransform.anchoredPosition = new Vector2(0f, -230f);

            long[] fees = { 0, 100, 250 };
            for (int i = 0; i < fees.Length; i++)
            {
                long fee = fees[i];
                Button button = UiKit.Button(body, fee == 0 ? "Free" : fee + " coins", () =>
                {
                    _entryFee = fee;
                    ShowMatchmaking();
                }, _entryFee == fee ? UiKit.Success : UiKit.Muted, 30);
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(i / 3f, 1f);
                rect.anchorMax = new Vector2((i + 1) / 3f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(-24f, 140f);
                rect.anchoredPosition = new Vector2(0f, -296f);
            }

            Text explanation = UiKit.Label(body,
                _entryFee == 0
                    ? "Free tables pay no coins. The winner still earns trophies and XP."
                    : "Entry " + _entryFee + " coins per player. Winner takes the whole pot (" + (_entryFee * _seats) + ").",
                24, UiKit.Muted);
            explanation.rectTransform.anchorMin = new Vector2(0f, 1f);
            explanation.rectTransform.anchorMax = new Vector2(1f, 1f);
            explanation.rectTransform.pivot = new Vector2(0.5f, 1f);
            explanation.rectTransform.sizeDelta = new Vector2(0f, 120f);
            explanation.rectTransform.anchoredPosition = new Vector2(0f, -446f);

            Button find = UiKit.Button(body, "Find match", () =>
            {
                _session.Socket.FindMatch(_seats, _entryFee);
                Toast("Looking for a table...");
            }, UiKit.Primary, 36);
            RectTransform findRect = find.GetComponent<RectTransform>();
            findRect.anchorMin = new Vector2(0f, 0f);
            findRect.anchorMax = new Vector2(1f, 0f);
            findRect.pivot = new Vector2(0.5f, 0f);
            findRect.sizeDelta = new Vector2(0f, 150f);
            findRect.anchoredPosition = new Vector2(0f, 20f);
        }

        // ------------------------------------------------------------- private room

        public void ShowRoom()
        {
            _current = AppScreen.Room;
            RectTransform body = Frame("Private room", true);

            if (_session.Room != null)
            {
                BuildRoomLobby(body);
                return;
            }

            Text createLabel = UiKit.Label(body, "Create a room", 30, UiKit.Ink, TextAnchor.MiddleLeft);
            createLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            createLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            createLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            createLabel.rectTransform.sizeDelta = new Vector2(0f, 56f);

            for (int i = 0; i < 3; i++)
            {
                int count = i + 2;
                Button button = UiKit.Button(body, "Create " + count + "P room", () =>
                {
                    _session.Socket.CreateRoom(count, 0);
                    Toast("Creating room...");
                }, UiKit.Primary, 28);
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, 130f);
                rect.anchoredPosition = new Vector2(0f, -70f - i * 146f);
            }

            Text joinLabel = UiKit.Label(body, "Join with a code", 30, UiKit.Ink, TextAnchor.MiddleLeft);
            joinLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            joinLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            joinLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            joinLabel.rectTransform.sizeDelta = new Vector2(0f, 56f);
            joinLabel.rectTransform.anchoredPosition = new Vector2(0f, -528f);

            InputField codeInput = UiKit.Input(body, "ROOM CODE");
            RectTransform inputRect = codeInput.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 1f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.pivot = new Vector2(0.5f, 1f);
            inputRect.sizeDelta = new Vector2(0f, 120f);
            inputRect.anchoredPosition = new Vector2(0f, -596f);
            codeInput.characterLimit = 6;

            Button join = UiKit.Button(body, "Join room", () =>
            {
                string code = (codeInput.text ?? string.Empty).Trim().ToUpperInvariant();
                if (code.Length != 6)
                {
                    Toast("Enter the 6 character room code.");
                    return;
                }

                _session.Socket.JoinRoom(code);
            }, UiKit.Success, 32);
            RectTransform joinRect = join.GetComponent<RectTransform>();
            joinRect.anchorMin = new Vector2(0f, 1f);
            joinRect.anchorMax = new Vector2(1f, 1f);
            joinRect.pivot = new Vector2(0.5f, 1f);
            joinRect.sizeDelta = new Vector2(0f, 130f);
            joinRect.anchoredPosition = new Vector2(0f, -736f);
        }

        private void BuildRoomLobby(RectTransform body)
        {
            RoomSnapshot room = _session.Room;

            Text code = UiKit.Label(body, room.Code, 64, UiKit.Primary);
            code.rectTransform.anchorMin = new Vector2(0f, 1f);
            code.rectTransform.anchorMax = new Vector2(1f, 1f);
            code.rectTransform.pivot = new Vector2(0.5f, 1f);
            code.rectTransform.sizeDelta = new Vector2(0f, 100f);

            Text hint = UiKit.Label(body, "Share this code with your friends", 24, UiKit.Muted);
            hint.rectTransform.anchorMin = new Vector2(0f, 1f);
            hint.rectTransform.anchorMax = new Vector2(1f, 1f);
            hint.rectTransform.pivot = new Vector2(0.5f, 1f);
            hint.rectTransform.sizeDelta = new Vector2(0f, 50f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -100f);

            int index = 0;
            if (room.Members != null)
            {
                foreach (RoomMemberSnapshot member in room.Members)
                {
                    Image row = UiKit.RoundedPanel(body, "Member" + index, UiKit.Card, 22f);
                    RectTransform rect = row.rectTransform;
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(0.5f, 1f);
                    rect.sizeDelta = new Vector2(0f, 116f);
                    rect.anchoredPosition = new Vector2(0f, -170f - index * 130f);

                    Image swatch = UiKit.Panel(row.transform, "Dot", UiKit.ColorFor(member.Seat));
                    swatch.sprite = Sprites.Circle();
                    RectTransform swatchRect = swatch.rectTransform;
                    swatchRect.anchorMin = new Vector2(0f, 0.5f);
                    swatchRect.anchorMax = new Vector2(0f, 0.5f);
                    swatchRect.pivot = new Vector2(0f, 0.5f);
                    swatchRect.sizeDelta = new Vector2(54f, 54f);
                    swatchRect.anchoredPosition = new Vector2(28f, 0f);

                    Text name = UiKit.Label(row.transform,
                        member.DisplayName + (member.IsBot ? "  (bot)" : string.Empty) +
                        (member.Connected ? string.Empty : "  -  offline"),
                        30, UiKit.Ink, TextAnchor.MiddleLeft);
                    name.rectTransform.offsetMin = new Vector2(100f, 0f);
                    name.rectTransform.offsetMax = new Vector2(-160f, 0f);

                    Text state = UiKit.Label(row.transform, "Lv " + member.Level, 24, UiKit.Muted, TextAnchor.MiddleRight);
                    state.rectTransform.offsetMin = new Vector2(0f, 0f);
                    state.rectTransform.offsetMax = new Vector2(-28f, 0f);

                    index++;
                }
            }

            float bottom = -190f - index * 130f;
            bool host = room.HostPlayerId == _session.Profile?.accountId;
            bool full = room.Members != null && room.Members.Length >= room.SeatCount;

            Button start = UiKit.Button(body, full ? "Start match" : "Waiting for players", () =>
            {
                _session.Socket.StartGame();
            }, full && host ? UiKit.Success : UiKit.Muted, 32);
            RectTransform startRect = start.GetComponent<RectTransform>();
            startRect.anchorMin = new Vector2(0f, 1f);
            startRect.anchorMax = new Vector2(1f, 1f);
            startRect.pivot = new Vector2(0.5f, 1f);
            startRect.sizeDelta = new Vector2(0f, 140f);
            startRect.anchoredPosition = new Vector2(0f, bottom);
            start.interactable = full && host;

            Button addBot = UiKit.Button(body, "Add bot", () =>
            {
                _session.Socket.AddBot();
            }, UiKit.PrimaryDark, 28);
            RectTransform botRect = addBot.GetComponent<RectTransform>();
            botRect.anchorMin = new Vector2(0f, 1f);
            botRect.anchorMax = new Vector2(0.5f, 1f);
            botRect.pivot = new Vector2(0.5f, 1f);
            botRect.sizeDelta = new Vector2(-16f, 110f);
            botRect.anchoredPosition = new Vector2(-8f, bottom - 156f);
            addBot.interactable = host && !full;

            Button leave = UiKit.Button(body, "Leave", () =>
            {
                _session.Socket.LeaveRoom();
                _session.Room = null;
                ShowModes();
            }, UiKit.Danger, 28);
            RectTransform leaveRect = leave.GetComponent<RectTransform>();
            leaveRect.anchorMin = new Vector2(0.5f, 1f);
            leaveRect.anchorMax = new Vector2(1f, 1f);
            leaveRect.pivot = new Vector2(0.5f, 1f);
            leaveRect.sizeDelta = new Vector2(-16f, 110f);
            leaveRect.anchoredPosition = new Vector2(8f, bottom - 156f);
        }

        // ------------------------------------------------------------- game

        public void ShowGame()
        {
            if (_gameScreen != null)
            {
                return;
            }

            _current = AppScreen.Game;
            Clear();

            RectTransform container = UiKit.Rect(_app.ScreenRoot, "GameScreen");
            _gameScreen = container.gameObject.AddComponent<GameScreen>();
            _gameScreen.Initialize(this, _session, _session.Game, container);
        }

        public void StartOfflineVsBots()
        {
            _current = AppScreen.Game;
            Clear();

            RectTransform container = UiKit.Rect(_app.ScreenRoot, "GameScreen");
            _gameScreen = container.gameObject.AddComponent<GameScreen>();
            _gameScreen.Initialize(this, _session, _session.Game, container);

            bool[] bots = { false, true, true, true };
            _session.Game.StartOffline(2, false, bots);
        }

        public void StartPassAndPlay()
        {
            _current = AppScreen.Game;
            Clear();

            RectTransform container = UiKit.Rect(_app.ScreenRoot, "GameScreen");
            _gameScreen = container.gameObject.AddComponent<GameScreen>();
            _gameScreen.Initialize(this, _session, _session.Game, container);

            bool[] bots = { false, false, false, false };
            _session.Game.StartOffline(2, true, bots);
        }

        public void ShowResult(GameSnapshot finalState, bool offline)
        {
            _current = AppScreen.Result;

            int mySeat = _session.Game.MySeat;
            int winner = finalState.WinnerSeat;
            bool won = winner == mySeat;

            RectTransform body = Frame(won ? "Victory" : "Match over", false);

            Image banner = UiKit.RoundedPanel(body, "Banner", won ? UiKit.Success : UiKit.Primary, 32f);
            RectTransform bannerRect = banner.rectTransform;
            bannerRect.anchorMin = new Vector2(0f, 1f);
            bannerRect.anchorMax = new Vector2(1f, 1f);
            bannerRect.pivot = new Vector2(0.5f, 1f);
            bannerRect.sizeDelta = new Vector2(0f, 240f);

            string headline = winner >= 0 && winner < finalState.Seats.Length
                ? finalState.Seats[winner].DisplayName + " wins!"
                : "Match ended";

            UiKit.Label(banner.transform, headline, 44, Color.white);

            // Every seat appears: finishers in rank order, then players who were still
            // playing when the match ended (rank 0), ordered by tokens home.
            var ordered = new List<SeatSnapshot>(finalState.Seats);
            ordered.Sort((left, right) =>
            {
                int leftRank = left.Rank > 0 ? left.Rank : int.MaxValue;
                int rightRank = right.Rank > 0 ? right.Rank : int.MaxValue;
                if (leftRank != rightRank)
                {
                    return leftRank.CompareTo(rightRank);
                }

                return right.TokensHome.CompareTo(left.TokensHome);
            });

            for (int row = 0; row < ordered.Count; row++)
            {
                SeatSnapshot seat = ordered[row];

                Image line = UiKit.RoundedPanel(body, "Seat" + row, UiKit.Card, 20f);
                RectTransform lineRect = line.rectTransform;
                lineRect.anchorMin = new Vector2(0f, 1f);
                lineRect.anchorMax = new Vector2(1f, 1f);
                lineRect.pivot = new Vector2(0.5f, 1f);
                lineRect.sizeDelta = new Vector2(0f, 110f);
                lineRect.anchoredPosition = new Vector2(0f, -270f - row * 124f);

                string prefix = seat.Rank > 0 ? seat.Rank + ".  " : "-  ";
                Text label = UiKit.Label(line.transform,
                    prefix + seat.DisplayName + "  -  " + seat.TokensHome + "/4 tokens home",
                    28, UiKit.Ink, TextAnchor.MiddleLeft);
                label.rectTransform.offsetMin = new Vector2(28f, 0f);
                label.rectTransform.offsetMax = new Vector2(-28f, 0f);
            }

            if (!offline)
            {
                StartCoroutine(RefreshProfileRoutine());
            }

            Button again = UiKit.Button(body, "Back to menu", () =>
            {
                // Leave the finished room server-side too, so the next search never hands
                // this player back the match that just ended.
                if (!offline)
                {
                    _session.Socket.LeaveRoom();
                }

                _session.Room = null;
                _session.MySeat = -1;
                _gameScreen = null;
                ShowMenu();
            }, UiKit.Primary, 32);
            RectTransform againRect = again.GetComponent<RectTransform>();
            againRect.anchorMin = new Vector2(0f, 0f);
            againRect.anchorMax = new Vector2(1f, 0f);
            againRect.pivot = new Vector2(0.5f, 0f);
            againRect.sizeDelta = new Vector2(0f, 150f);
            againRect.anchoredPosition = new Vector2(0f, 20f);
        }

        private void TeardownGame()
        {
            if (_gameScreen != null)
            {
                _gameScreen.Dispose();
                _gameScreen = null;
            }
        }

        // ------------------------------------------------------------- shop

        public void ShowShop()
        {
            _current = AppScreen.Shop;
            RectTransform body = Frame("Shop", true);
            UiKit.Scroll(body, out RectTransform content);

            StartCoroutine(_session.Api.GetShop(
                shop =>
                {
                    if (_current != AppScreen.Shop)
                    {
                        return;
                    }

                    _session.SetCoins(shop.balance);
                    RefreshCoins();

                    foreach (ShopItemDto item in shop.items)
                    {
                        bool owned = Array.IndexOf(shop.owned ?? Array.Empty<string>(), item.id) >= 0;
                        Image card = UiKit.RoundedPanel(content, "Item" + item.id, UiKit.Card, 24f);
                        UiKit.Sized(card.gameObject, 190f);

                        Text title = UiKit.Label(card.transform, item.title, 32, UiKit.Ink, TextAnchor.MiddleLeft);
                        title.rectTransform.offsetMin = new Vector2(28f, 90f);
                        title.rectTransform.offsetMax = new Vector2(-280f, -16f);

                        Text description = UiKit.Label(card.transform, item.description, 24, UiKit.Muted, TextAnchor.MiddleLeft);
                        description.rectTransform.offsetMin = new Vector2(28f, 20f);
                        description.rectTransform.offsetMax = new Vector2(-280f, 90f);

                        Button buy = UiKit.Button(card.transform, owned ? "Owned" : item.priceCoins + " coins", () =>
                        {
                            if (owned)
                            {
                                return;
                            }

                            StartCoroutine(PurchaseRoutine(item.id));
                        }, owned ? UiKit.Muted : UiKit.Accent, 26);
                        RectTransform buyRect = buy.GetComponent<RectTransform>();
                        buyRect.anchorMin = new Vector2(1f, 0.5f);
                        buyRect.anchorMax = new Vector2(1f, 0.5f);
                        buyRect.pivot = new Vector2(1f, 0.5f);
                        buyRect.sizeDelta = new Vector2(240f, 84f);
                        buyRect.anchoredPosition = new Vector2(-24f, 0f);
                        buy.interactable = !owned;
                    }

                    Text note = UiKit.Label(content,
                        "Coins are a virtual reward currency with no real-money value and no cash-out. Purchases are cosmetic only.",
                        22, UiKit.Muted);
                    UiKit.Sized(note.gameObject, 120f);
                },
                error => Toast(error)));
        }

        private IEnumerator PurchaseRoutine(string itemId)
        {
            bool done = false;
            yield return _session.Api.Purchase(itemId,
                wallet =>
                {
                    _session.SetCoins(wallet.balance);
                    Toast("Purchased! Balance: " + wallet.balance);
                    done = true;
                },
                error =>
                {
                    Toast(error);
                    done = true;
                });

            if (done)
            {
                ShowShop();
            }
        }

        // ------------------------------------------------------------- daily + missions

        public void ShowDaily()
        {
            _current = AppScreen.Daily;
            RectTransform body = Frame("Daily rewards", true);
            UiKit.Scroll(body, out RectTransform content);

            StartCoroutine(_session.Api.GetDaily(
                daily =>
                {
                    if (_current != AppScreen.Daily)
                    {
                        return;
                    }

                    Image card = UiKit.RoundedPanel(content, "Daily", UiKit.Card, 24f);
                    UiKit.Sized(card.gameObject, 300f);

                    Text streak = UiKit.Label(card.transform,
                        daily.claimedToday ? "Already claimed today" : "Day " + daily.nextStreakDay + " ready",
                        34, UiKit.Ink);
                    streak.rectTransform.offsetMin = new Vector2(24f, 190f);
                    streak.rectTransform.offsetMax = new Vector2(-24f, -20f);

                    long[] ladder = daily.rewards ?? Array.Empty<long>();
                    int shown = Mathf.Min(7, ladder.Length);
                    for (int i = 0; i < shown; i++)
                    {
                        bool reached = i + 1 <= daily.nextStreakDay - (daily.claimedToday ? 1 : 0);
                        Image chip = UiKit.RoundedPanel(card.transform, "Day" + i,
                            reached ? UiKit.Success : new Color(UiKit.Muted.r, UiKit.Muted.g, UiKit.Muted.b, 0.25f), 16f);
                        RectTransform chipRect = chip.rectTransform;
                        chipRect.anchorMin = new Vector2(i / (float)shown, 0f);
                        chipRect.anchorMax = new Vector2((i + 1) / (float)shown, 0f);
                        chipRect.pivot = new Vector2(0.5f, 0f);
                        chipRect.sizeDelta = new Vector2(-12f, 120f);
                        chipRect.anchoredPosition = new Vector2(0f, 24f);

                        UiKit.Label(chip.transform, (i + 1) + "\n" + ladder[i], 22, Color.white);
                    }

                    Button claim = UiKit.Button(content,
                        daily.claimedToday ? "Come back tomorrow" : "Claim " + daily.nextReward + " coins",
                        () => StartCoroutine(ClaimDailyRoutine()),
                        daily.claimedToday ? UiKit.Muted : UiKit.Success, 30);
                    UiKit.Sized(claim.gameObject, 140f);
                    claim.interactable = !daily.claimedToday;

                    BuildMissionList(content);
                },
                error => Toast(error)));
        }

        private IEnumerator ClaimDailyRoutine()
        {
            yield return _session.Api.ClaimDaily(
                daily =>
                {
                    _session.SetCoins(daily.balance);
                    Toast("+" + (daily.rewards != null && daily.rewards.Length > 0 ? daily.rewards[Mathf.Clamp(daily.nextStreakDay - 1, 0, daily.rewards.Length - 1)] : 0) + " coins claimed");
                },
                Toast);

            ShowDaily();
        }

        private void BuildMissionList(RectTransform content)
        {
            StartCoroutine(_session.Api.GetMissions(
                list =>
                {
                    if (_current != AppScreen.Daily)
                    {
                        return;
                    }

                    Text heading = UiKit.Label(content, "Missions", 32, UiKit.Ink, TextAnchor.MiddleLeft);
                    UiKit.Sized(heading.gameObject, 70f);

                    foreach (MissionDto mission in list.items)
                    {
                        Image card = UiKit.RoundedPanel(content, "Mission" + mission.key, UiKit.Card, 20f);
                        UiKit.Sized(card.gameObject, 150f);

                        Text title = UiKit.Label(card.transform,
                            mission.title + "  (" + mission.progress + "/" + mission.target + ")",
                            26, UiKit.Ink, TextAnchor.MiddleLeft);
                        title.rectTransform.offsetMin = new Vector2(24f, 60f);
                        title.rectTransform.offsetMax = new Vector2(-260f, -14f);

                        Text period = UiKit.Label(card.transform, mission.period, 22, UiKit.Muted, TextAnchor.MiddleLeft);
                        period.rectTransform.offsetMin = new Vector2(24f, 16f);
                        period.rectTransform.offsetMax = new Vector2(-260f, 60f);

                        bool claimable = !mission.claimed && mission.progress >= mission.target;
                        Button claim = UiKit.Button(card.transform, mission.claimed ? "Claimed" : "+" + mission.reward, () =>
                        {
                            StartCoroutine(ClaimMissionRoutine(mission.key));
                        }, claimable ? UiKit.Success : UiKit.Muted, 24);
                        RectTransform claimRect = claim.GetComponent<RectTransform>();
                        claimRect.anchorMin = new Vector2(1f, 0.5f);
                        claimRect.anchorMax = new Vector2(1f, 0.5f);
                        claimRect.pivot = new Vector2(1f, 0.5f);
                        claimRect.sizeDelta = new Vector2(210f, 76f);
                        claimRect.anchoredPosition = new Vector2(-20f, 0f);
                        claim.interactable = claimable;
                    }
                },
                error => Debug.LogWarning("Missions: " + error)));
        }

        private IEnumerator ClaimMissionRoutine(string key)
        {
            yield return _session.Api.ClaimMission(key,
                wallet =>
                {
                    _session.SetCoins(wallet.balance);
                    Toast("Mission reward claimed.");
                },
                Toast);

            ShowDaily();
        }

        // ------------------------------------------------------------- leaderboard

        public void ShowLeaderboard()
        {
            _current = AppScreen.Leaderboard;
            RectTransform body = Frame("Leaderboard", true);

            string[] scopes = { "global", "weekly", "friends" };
            for (int i = 0; i < scopes.Length; i++)
            {
                string scope = scopes[i];
                Button tab = UiKit.Button(body, scope, () =>
                {
                    _leaderboardScope = scope;
                    ShowLeaderboard();
                }, _leaderboardScope == scope ? UiKit.Primary : UiKit.Muted, 26);
                RectTransform rect = tab.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(i / 3f, 1f);
                rect.anchorMax = new Vector2((i + 1) / 3f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(-16f, 120f);
            }

            RectTransform list = UiKit.Rect(body, "List");
            list.anchorMin = Vector2.zero;
            list.anchorMax = Vector2.one;
            list.offsetMax = new Vector2(0f, -140f);

            UiKit.Scroll(list, out RectTransform content);

            StartCoroutine(_session.Api.GetLeaderboard(_leaderboardScope,
                board =>
                {
                    if (_current != AppScreen.Leaderboard)
                    {
                        return;
                    }

                    if (board.items == null || board.items.Length == 0)
                    {
                        Text empty = UiKit.Label(content, "No results yet. Play a match!", 26, UiKit.Muted);
                        UiKit.Sized(empty.gameObject, 120f);
                        return;
                    }

                    foreach (LeaderboardEntryDto entry in board.items)
                    {
                        Image row = UiKit.RoundedPanel(content, "Entry" + entry.accountId, UiKit.Card, 18f);
                        UiKit.Sized(row.gameObject, 112f);

                        Text position = UiKit.Label(row.transform, "#" + entry.position, 30, UiKit.Primary, TextAnchor.MiddleLeft);
                        position.rectTransform.offsetMin = new Vector2(24f, 0f);
                        position.rectTransform.offsetMax = new Vector2(-820f, 0f);

                        Text name = UiKit.Label(row.transform, entry.displayName + "  -  Lv " + entry.level, 26, UiKit.Ink, TextAnchor.MiddleLeft);
                        name.rectTransform.offsetMin = new Vector2(140f, 0f);
                        name.rectTransform.offsetMax = new Vector2(-300f, 0f);

                        Text value = UiKit.Label(row.transform,
                            _leaderboardScope == "weekly" ? entry.value + " wins" : entry.value + " trophies",
                            26, UiKit.Muted, TextAnchor.MiddleRight);
                        value.rectTransform.offsetMin = new Vector2(0f, 0f);
                        value.rectTransform.offsetMax = new Vector2(-24f, 0f);
                    }
                },
                error => Toast(error)));
        }

        // ------------------------------------------------------------- friends

        public void ShowFriends()
        {
            _current = AppScreen.Friends;
            RectTransform body = Frame("Friends", true);
            UiKit.Scroll(body, out RectTransform content);

            StartCoroutine(_session.Api.GetFriends(
                list =>
                {
                    if (_current != AppScreen.Friends)
                    {
                        return;
                    }

                    Text code = UiKit.Label(content, "Your code: " + list.myPlayerCode, 30, UiKit.Ink);
                    UiKit.Sized(code.gameObject, 80f);

                    if (list.incoming != null && list.incoming.Length > 0)
                    {
                        Text heading = UiKit.Label(content, "Requests", 28, UiKit.Muted, TextAnchor.MiddleLeft);
                        UiKit.Sized(heading.gameObject, 60f);

                        foreach (FriendDto request in list.incoming)
                        {
                            Image row = UiKit.RoundedPanel(content, "Req" + request.accountId, UiKit.Card, 18f);
                            UiKit.Sized(row.gameObject, 130f);

                            Text name = UiKit.Label(row.transform, request.displayName, 26, UiKit.Ink, TextAnchor.MiddleLeft);
                            name.rectTransform.offsetMin = new Vector2(24f, 0f);
                            name.rectTransform.offsetMax = new Vector2(-420f, 0f);

                            Button accept = UiKit.Button(row.transform, "Accept", () =>
                            {
                                StartCoroutine(FriendResponseRoutine(request.friendshipId, true));
                            }, UiKit.Success, 22);
                            RectTransform acceptRect = accept.GetComponent<RectTransform>();
                            acceptRect.anchorMin = new Vector2(1f, 0.5f);
                            acceptRect.anchorMax = new Vector2(1f, 0.5f);
                            acceptRect.pivot = new Vector2(1f, 0.5f);
                            acceptRect.sizeDelta = new Vector2(180f, 72f);
                            acceptRect.anchoredPosition = new Vector2(-20f, 0f);

                            Button decline = UiKit.Button(row.transform, "No", () =>
                            {
                                StartCoroutine(FriendResponseRoutine(request.friendshipId, false));
                            }, UiKit.Muted, 22);
                            RectTransform declineRect = decline.GetComponent<RectTransform>();
                            declineRect.anchorMin = new Vector2(1f, 0.5f);
                            declineRect.anchorMax = new Vector2(1f, 0.5f);
                            declineRect.pivot = new Vector2(1f, 0.5f);
                            declineRect.sizeDelta = new Vector2(120f, 72f);
                            declineRect.anchoredPosition = new Vector2(-210f, 0f);
                        }
                    }

                    if (list.friends != null && list.friends.Length > 0)
                    {
                        Text heading = UiKit.Label(content, "Friends", 28, UiKit.Muted, TextAnchor.MiddleLeft);
                        UiKit.Sized(heading.gameObject, 60f);

                        foreach (FriendDto friend in list.friends)
                        {
                            Image row = UiKit.RoundedPanel(content, "Friend" + friend.accountId, UiKit.Card, 18f);
                            UiKit.Sized(row.gameObject, 120f);

                            Image dot = UiKit.Panel(row.transform, "Dot", friend.online ? UiKit.Success : UiKit.Muted);
                            dot.sprite = Sprites.Circle();
                            RectTransform dotRect = dot.rectTransform;
                            dotRect.anchorMin = new Vector2(0f, 0.5f);
                            dotRect.anchorMax = new Vector2(0f, 0.5f);
                            dotRect.pivot = new Vector2(0f, 0.5f);
                            dotRect.sizeDelta = new Vector2(34f, 34f);
                            dotRect.anchoredPosition = new Vector2(24f, 0f);

                            Text name = UiKit.Label(row.transform,
                                friend.displayName + (friend.online ? "  -  online" : "  -  offline"),
                                26, UiKit.Ink, TextAnchor.MiddleLeft);
                            name.rectTransform.offsetMin = new Vector2(76f, 0f);
                            name.rectTransform.offsetMax = new Vector2(-120f, 0f);
                        }
                    }
                    else
                    {
                        Text empty = UiKit.Label(content, "No friends yet. Share your code above.", 24, UiKit.Muted);
                        UiKit.Sized(empty.gameObject, 100f);
                    }

                    InputField codeInput = UiKit.Input(content, "FRIEND CODE");
                    UiKit.Sized(codeInput.gameObject, 110f);

                    Button add = UiKit.Button(content, "Send friend request", () =>
                    {
                        string entered = (codeInput.text ?? string.Empty).Trim().ToUpperInvariant();
                        if (entered.Length != 6)
                        {
                            Toast("Friend codes are 6 characters.");
                            return;
                        }

                        StartCoroutine(AddFriendRoutine(entered));
                    }, UiKit.Primary, 28);
                    UiKit.Sized(add.gameObject, 130f);
                },
                error => Toast(error)));
        }

        private IEnumerator FriendResponseRoutine(string friendshipId, bool accept)
        {
            yield return _session.Api.RespondToFriend(friendshipId, accept,
                () => Toast(accept ? "Friend added." : "Request declined."),
                Toast);
            ShowFriends();
        }

        private IEnumerator AddFriendRoutine(string code)
        {
            yield return _session.Api.AddFriend(code, () => Toast("Request sent."), Toast);
            ShowFriends();
        }

        // ------------------------------------------------------------- profile

        public void ShowProfile()
        {
            _current = AppScreen.Profile;
            RectTransform body = Frame("Profile", true);
            UiKit.Scroll(body, out RectTransform content);

            StartCoroutine(_session.Api.GetMe(
                me =>
                {
                    if (_current != AppScreen.Profile)
                    {
                        return;
                    }

                    _session.Profile = me.profile;
                    _session.Stats = me.stats;

                    Image card = UiKit.RoundedPanel(content, "Profile", UiKit.Card, 26f);
                    UiKit.Sized(card.gameObject, 420f);

                    Text name = UiKit.Label(card.transform, me.profile.displayName, 40, UiKit.Ink);
                    name.rectTransform.offsetMin = new Vector2(20f, 300f);
                    name.rectTransform.offsetMax = new Vector2(-20f, -20f);

                    Text code = UiKit.Label(card.transform, "Player code " + me.profile.playerCode, 24, UiKit.Muted);
                    code.rectTransform.offsetMin = new Vector2(20f, 240f);
                    code.rectTransform.offsetMax = new Vector2(-20f, 300f);

                    // XP bar
                    Image track = UiKit.RoundedPanel(card.transform, "XpTrack", new Color(0f, 0f, 0f, 0.08f), 14f);
                    RectTransform trackRect = track.rectTransform;
                    trackRect.anchorMin = new Vector2(0f, 1f);
                    trackRect.anchorMax = new Vector2(1f, 1f);
                    trackRect.pivot = new Vector2(0.5f, 1f);
                    trackRect.sizeDelta = new Vector2(-48f, 34f);
                    trackRect.anchoredPosition = new Vector2(0f, -300f);

                    float ratio = me.profile.xpForNextLevel <= 0 ? 0f : Mathf.Clamp01(me.profile.xpIntoLevel / (float)me.profile.xpForNextLevel);
                    Image fill = UiKit.RoundedPanel(track.transform, "XpFill", UiKit.Accent, 14f);
                    RectTransform fillRect = fill.rectTransform;
                    fillRect.anchorMin = new Vector2(0f, 0f);
                    fillRect.anchorMax = new Vector2(ratio, 1f);
                    fillRect.offsetMin = Vector2.zero;
                    fillRect.offsetMax = Vector2.zero;

                    Text level = UiKit.Label(card.transform,
                        "Level " + me.profile.level + "  -  " + me.profile.xpIntoLevel + "/" + me.profile.xpForNextLevel + " XP",
                        24, UiKit.Muted);
                    level.rectTransform.offsetMin = new Vector2(20f, 120f);
                    level.rectTransform.offsetMax = new Vector2(-20f, 170f);

                    Text stats = UiKit.Label(card.transform,
                        me.stats.gamesPlayed + " games  -  " + me.stats.wins + "W / " + me.stats.losses + "L  -  " +
                        me.stats.winRate + "% win rate  -  " + me.profile.trophies + " trophies",
                        24, UiKit.Ink);
                    stats.rectTransform.offsetMin = new Vector2(20f, 60f);
                    stats.rectTransform.offsetMax = new Vector2(-20f, 110f);

                    Text coins = UiKit.Label(card.transform, me.profile.coins + " coins", 26, UiKit.Muted);
                    coins.rectTransform.offsetMin = new Vector2(20f, 10f);
                    coins.rectTransform.offsetMax = new Vector2(-20f, 56f);

                    RefreshCoins();
                    BuildAchievementList(content);
                },
                error => Toast(error)));
        }

        private void BuildAchievementList(RectTransform content)
        {
            StartCoroutine(_session.Api.GetAchievements(
                list =>
                {
                    if (_current != AppScreen.Profile)
                    {
                        return;
                    }

                    Text heading = UiKit.Label(content, "Achievements", 30, UiKit.Ink, TextAnchor.MiddleLeft);
                    UiKit.Sized(heading.gameObject, 70f);

                    foreach (AchievementDto achievement in list.items)
                    {
                        Image row = UiKit.RoundedPanel(content, "Ach" + achievement.key,
                            achievement.unlocked ? UiKit.Card : new Color(0f, 0f, 0f, 0.05f), 18f);
                        UiKit.Sized(row.gameObject, 130f);

                        Text title = UiKit.Label(row.transform,
                            achievement.title + "  (" + achievement.current + "/" + achievement.threshold + ")",
                            24, achievement.unlocked ? UiKit.Ink : UiKit.Muted, TextAnchor.MiddleLeft);
                        title.rectTransform.offsetMin = new Vector2(24f, 0f);
                        title.rectTransform.offsetMax = new Vector2(-240f, 0f);

                        bool claimable = achievement.unlocked && !achievement.claimed;
                        Button claim = UiKit.Button(row.transform, achievement.claimed ? "Claimed" : "+" + achievement.reward, () =>
                        {
                            StartCoroutine(ClaimAchievementRoutine(achievement.key));
                        }, claimable ? UiKit.Success : UiKit.Muted, 22);
                        RectTransform claimRect = claim.GetComponent<RectTransform>();
                        claimRect.anchorMin = new Vector2(1f, 0.5f);
                        claimRect.anchorMax = new Vector2(1f, 0.5f);
                        claimRect.pivot = new Vector2(1f, 0.5f);
                        claimRect.sizeDelta = new Vector2(200f, 76f);
                        claimRect.anchoredPosition = new Vector2(-20f, 0f);
                        claim.interactable = claimable;
                    }
                },
                error => Debug.LogWarning("Achievements: " + error)));
        }

        private IEnumerator ClaimAchievementRoutine(string key)
        {
            yield return _session.Api.ClaimAchievement(key,
                wallet =>
                {
                    _session.SetCoins(wallet.balance);
                    Toast("Achievement reward claimed.");
                },
                Toast);
            ShowProfile();
        }

        // ------------------------------------------------------------- history

        public void ShowHistory()
        {
            _current = AppScreen.History;
            RectTransform body = Frame("Match history", true);
            UiKit.Scroll(body, out RectTransform content);

            StartCoroutine(_session.Api.GetHistory(
                history =>
                {
                    if (_current != AppScreen.History)
                    {
                        return;
                    }

                    if (history.items == null || history.items.Length == 0)
                    {
                        Text empty = UiKit.Label(content, "No matches played yet.", 26, UiKit.Muted);
                        UiKit.Sized(empty.gameObject, 120f);
                        return;
                    }

                    foreach (MatchHistoryDto match in history.items)
                    {
                        Image row = UiKit.RoundedPanel(content, "Match" + match.matchId, UiKit.Card, 18f);
                        UiKit.Sized(row.gameObject, 140f);

                        Text title = UiKit.Label(row.transform,
                            (match.won ? "Victory" : match.aborted ? "Aborted" : "Defeat") +
                            "  -  " + match.seatCount + "P  -  rank " + match.rank,
                            26, match.won ? UiKit.Success : UiKit.Ink, TextAnchor.MiddleLeft);
                        title.rectTransform.offsetMin = new Vector2(24f, 60f);
                        title.rectTransform.offsetMax = new Vector2(-24f, -12f);

                        Text detail = UiKit.Label(row.transform,
                            "Entry " + match.entryFee + "  -  " + (match.coinsDelta >= 0 ? "+" : "") + match.coinsDelta + " coins  -  " + match.tokensHome + "/4 home",
                            22, UiKit.Muted, TextAnchor.MiddleLeft);
                        detail.rectTransform.offsetMin = new Vector2(24f, 14f);
                        detail.rectTransform.offsetMax = new Vector2(-24f, 60f);
                    }
                },
                error => Toast(error)));
        }

        // ------------------------------------------------------------- settings

        public void ShowSettings()
        {
            _current = AppScreen.Settings;
            RectTransform body = Frame("Settings", true);
            UiKit.Scroll(body, out RectTransform content);

            AddToggle(content, "Sound effects", AudioService.Instance.SfxEnabled, value => AudioService.Instance.SetSfx(value));
            AddToggle(content, "Music", AudioService.Instance.MusicEnabled, value => AudioService.Instance.SetMusic(value));
            AddToggle(content, "Haptic feedback", AudioService.Instance.HapticsEnabled, value => AudioService.Instance.SetHaptics(value));

            Button history = UiKit.Button(content, "Match history", ShowHistory, UiKit.PrimaryDark, 28);
            UiKit.Sized(history.gameObject, 130f);

            Image info = UiKit.RoundedPanel(content, "Info", UiKit.Card, 20f);
            UiKit.Sized(info.gameObject, 260f);

            Text server = UiKit.Label(info.transform,
                "Server\n" + AppConfig.Current.apiBaseUrl +
                "\n\nSigned in as " + (_session.Profile?.displayName ?? "-") +
                (_session.Profile != null && _session.Profile.isGuest ? " (guest)" : string.Empty),
                22, UiKit.Muted, TextAnchor.UpperLeft);
            server.rectTransform.offsetMin = new Vector2(24f, 20f);
            server.rectTransform.offsetMax = new Vector2(-24f, -20f);

            Button signOut = UiKit.Button(content, "Sign out", () =>
            {
                _session.SignOut();
                ShowLogin();
            }, UiKit.Muted, 28);
            UiKit.Sized(signOut.gameObject, 130f);

            Button delete = UiKit.Button(content, "Delete my account", () =>
            {
                StartCoroutine(DeleteAccountRoutine());
            }, UiKit.Danger, 26);
            UiKit.Sized(delete.gameObject, 130f);

            Text legal = UiKit.Label(content,
                "Coins are virtual, have no cash value, and cannot be exchanged for money. " +
                "Ludo Legends is an original game and is not affiliated with any other title.",
                20, UiKit.Muted);
            UiKit.Sized(legal.gameObject, 150f);
        }

        private void AddToggle(RectTransform content, string label, bool initial, Action<bool> onChange)
        {
            Image row = UiKit.RoundedPanel(content, "Toggle" + label, UiKit.Card, 18f);
            UiKit.Sized(row.gameObject, 120f);

            Text text = UiKit.Label(row.transform, label, 28, UiKit.Ink, TextAnchor.MiddleLeft);
            text.rectTransform.offsetMin = new Vector2(24f, 0f);
            text.rectTransform.offsetMax = new Vector2(-220f, 0f);

            bool state = initial;
            Button button = UiKit.Button(row.transform, initial ? "ON" : "OFF", () =>
            {
                state = !state;
                onChange(state);
            }, initial ? UiKit.Success : UiKit.Muted, 24);

            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(180f, 78f);
            rect.anchoredPosition = new Vector2(-20f, 0f);

            // Rebuild the screen after a toggle so the label reflects the new state.
            button.onClick.AddListener(() =>
            {
                Text label2 = button.GetComponentInChildren<Text>();
                if (label2 != null)
                {
                    label2.text = state ? "ON" : "OFF";
                }

                Image image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = state ? UiKit.Success : UiKit.Muted;
                }
            });
        }

        private IEnumerator DeleteAccountRoutine()
        {
            yield return _session.Api.DeleteAccount(
                () =>
                {
                    Toast("Account deleted.");
                    _session.SignOut();
                    ShowLogin();
                },
                Toast);
        }

        // ------------------------------------------------------------- socket routing

        private void OnEnvelope(ServerEnvelope envelope)
        {
            if (envelope == null)
            {
                return;
            }

            switch (envelope.Type)
            {
                case MessageTypes.Welcome:
                    // Authoritative balance as soon as the socket opens, so a reward claimed
                    // in another session is reflected without an extra REST round trip.
                    if (envelope.Balance > 0)
                    {
                        _session.SetCoins(envelope.Balance);
                    }

                    break;

                case MessageTypes.MatchCancelled:
                    _session.Room = null;
                    _session.MySeat = -1;
                    Toast("Search cancelled.");
                    if (_current == AppScreen.Matchmaking)
                    {
                        ShowMatchmaking();
                    }

                    break;

                case MessageTypes.RoomState:
                case MessageTypes.Queued:
                case MessageTypes.MatchFound:
                    if (envelope.Room != null)
                    {
                        _session.Room = envelope.Room;

                        foreach (RoomMemberSnapshot member in envelope.Room.Members ?? Array.Empty<RoomMemberSnapshot>())
                        {
                            if (member.PlayerId == _session.Profile?.accountId)
                            {
                                _session.MySeat = member.Seat;
                            }
                        }
                    }

                    if (_current == AppScreen.Room || _current == AppScreen.Matchmaking)
                    {
                        if (envelope.Room != null && envelope.Room.Status == RoomStatusCodes.Playing)
                        {
                            ShowGame();
                        }
                        else if (_current == AppScreen.Room)
                        {
                            ShowRoom();
                        }
                        else
                        {
                            Toast(envelope.Room != null
                                ? envelope.Room.Members.Length + "/" + envelope.Room.SeatCount + " players - code " + envelope.Room.Code
                                : "Searching for a table...");
                        }
                    }

                    break;

                case MessageTypes.GameStateMsg:
                    if (_current != AppScreen.Game && envelope.State != null)
                    {
                        ShowGame();
                    }

                    break;

                case MessageTypes.Error:
                case MessageTypes.CommandRejected:
                    if (!string.IsNullOrEmpty(envelope.Error))
                    {
                        Toast(envelope.Error);
                    }

                    break;
            }
        }
    }
}
