using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Core;
using Xunit;
using Xunit.Abstractions;

namespace LudoLegends.Integration.Tests
{
    /// <summary>
    /// Multiplayer tests against the real server binary over real WebSocket connections.
    /// Every "player" here is an independent TCP client with its own guest account and its
    /// own socket, so these exercise the same code path four phones would.
    /// </summary>
    [Collection("live server")]
    public sealed class MultiplayerTests
    {
        private readonly LudoServerFixture _server;
        private readonly ITestOutputHelper _output;

        public MultiplayerTests(LudoServerFixture server, ITestOutputHelper output)
        {
            _server = server;
            _output = output;
        }

        // ---------------------------------------------------------------- helpers

        private async Task<TestClient> NewClientAsync(string name)
        {
            (string token, string playerId, _) = await _server.GuestAsync(name);
            return await TestClient.ConnectAsync(_server.BaseUrl, name, token, playerId);
        }

        private static string Trace(TestClient client)
        {
            return string.Join(" | ", client.Received.Select(e =>
                e.Type + (e.ErrorCode != null ? "(" + e.ErrorCode + ")" : string.Empty) +
                (e.State != null ? "v" + e.State.Version + "/a" + e.State.ActiveSeat + "/p" + e.State.Phase : string.Empty)));
        }

        private static void WaitForRoomPopulation(IEnumerable<TestClient> clients, int players)
        {
            foreach (TestClient client in clients)
            {
                client.WaitFor(e => e.Type == MessageTypes.RoomState && e.Room != null && e.Room.Members.Length == players,
                    because: client.Name + " never saw a room with " + players + " players");
            }
        }

        private static void WaitForMatchStart(IEnumerable<TestClient> clients)
        {
            foreach (TestClient client in clients)
            {
                client.WaitFor(e => e.Type == MessageTypes.GameStateMsg && e.State != null,
                    because: client.Name + " never received the opening snapshot");
            }
        }

        /// <summary>
        /// Drives every scripted player until the match ends. Each client acts only when
        /// the authoritative snapshot says it is its turn, and only from a snapshot whose
        /// version it has not acted on yet - the same discipline a real client uses.
        /// </summary>
        private async Task<GameSnapshot> PlayToEndAsync(IReadOnlyList<TestClient> clients, int timeoutSeconds = 150)
        {
            var actedVersion = clients.ToDictionary(c => c, _ => -1);
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            int actions = 0;

            while (DateTime.UtcNow < deadline)
            {
                GameSnapshot reference = clients.Select(c => c.Latest).FirstOrDefault(s => s != null);
                if (reference == null)
                {
                    await Task.Delay(5);
                    continue;
                }

                if (reference.WinnerSeat >= 0 && reference.FinishedCount >= reference.Seats.Length - 1)
                {
                    _output.WriteLine("Match finished after " + actions + " actions (version " + reference.Version + ")");
                    return reference;
                }

                bool acted = false;
                foreach (TestClient client in clients)
                {
                    GameSnapshot snapshot = client.Latest;
                    if (snapshot == null || client.Seat < 0 || snapshot.ActiveSeat != client.Seat)
                    {
                        continue;
                    }

                    if (snapshot.WinnerSeat >= 0 && snapshot.FinishedCount >= snapshot.Seats.Length - 1)
                    {
                        return snapshot;
                    }

                    if (actedVersion[client] >= snapshot.Version)
                    {
                        continue;
                    }

                    actedVersion[client] = snapshot.Version;
                    acted = true;
                    actions++;

                    if (snapshot.LegalTokens != null && snapshot.LegalTokens.Length > 0)
                    {
                        await client.MoveAsync(snapshot.LegalTokens[0]);
                    }
                    else
                    {
                        await client.RollAsync();
                    }

                    break;
                }

                await Task.Delay(acted ? 2 : 4);
            }

            GameSnapshot last = clients.Select(c => c.Latest).FirstOrDefault(s => s != null);
            throw new TimeoutException("Match did not finish in time. Last version: " + (last?.Version ?? -1) +
                                       ", finished: " + (last?.FinishedCount ?? -1));
        }

        // ---------------------------------------------------------------- tests

        [Fact]
        public async Task Four_real_clients_play_a_full_match_and_agree_on_every_result()
        {
            var clients = new List<TestClient>();
            for (int i = 0; i < 4; i++)
            {
                clients.Add(await NewClientAsync("P4-" + (i + 1)));
            }

            foreach (TestClient client in clients)
            {
                await client.FindMatchAsync(4, 0);
            }

            WaitForMatchStart(clients);

            // Room snapshots were consumed while waiting for the opening state; the
            // authoritative proof of seating is the snapshot itself.
            GameSnapshot opening = clients[0].Latest;
            Assert.Equal(4, opening.Seats.Length);
            Assert.All(clients, c => Assert.Equal(4, c.Latest.Seats.Length));
            Assert.Empty(opening.Seats.Select(s => s.Color).Distinct().Except(new[] { 0, 1, 2, 3 }));

            // Every client must see exactly the same opening state, or dice sync is broken.
            foreach (TestClient client in clients)
            {
                Assert.Equal(opening.Version, client.Latest.Version);
                Assert.Equal(opening.ActiveSeat, client.Latest.ActiveSeat);
            }

            GameSnapshot final = await PlayToEndAsync(clients);

            Assert.True(final.WinnerSeat >= 0, "a completed match must have a winner");
            Assert.Equal(3, final.FinishedCount);
            // FinishOrder holds the seats that finished, in order: three of four here.
            Assert.Equal(3, final.FinishOrder.Length);
            Assert.Equal(3, final.FinishOrder.Distinct().Count());

            // All four clients converge on the same authoritative final state.
            foreach (TestClient client in clients)
            {
                client.WaitFor(e => e.Type == MessageTypes.GameStateMsg && e.State.Version == final.Version,
                    because: client.Name + " never saw final version " + final.Version);
                Assert.Equal(final.WinnerSeat, client.Latest.WinnerSeat);
                Assert.Equal(final.FinishOrder, client.Latest.FinishOrder);
            }

            foreach (TestClient client in clients)
            {
                await client.DisposeAsync();
            }
        }

        [Fact]
        public async Task Two_player_paid_match_charges_once_and_pays_the_pot_once()
        {
            TestClient host = await NewClientAsync("PaidHost");
            TestClient guest = await NewClientAsync("PaidGuest");

            long startHost = await _server.BalanceAsync(host.Token);
            long startGuest = await _server.BalanceAsync(guest.Token);
            Assert.Equal(500, startHost);
            Assert.Equal(500, startGuest);

            await host.CreateRoomAsync(2, 100);
            ServerEnvelope roomEnvelope = host.WaitFor(e => e.Type == MessageTypes.RoomState && e.Room != null);
            string code = roomEnvelope.Room.Code;
            Assert.Equal(6, code.Length);

            await guest.JoinRoomAsync(code);
            WaitForRoomPopulation(new[] { host, guest }, 2);

            await host.StartGameAsync();
            WaitForMatchStart(new[] { host, guest });

            Assert.Equal(startHost - 100, await _server.BalanceAsync(host.Token));
            Assert.Equal(startGuest - 100, await _server.BalanceAsync(guest.Token));

            GameSnapshot final = await PlayToEndAsync(new[] { host, guest });
            Assert.True(final.WinnerSeat >= 0);

            // Settlement is asynchronous (it runs in the command handler), so poll briefly.
            long hostAfter = 0;
            long guestAfter = 0;
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                hostAfter = await _server.BalanceAsync(host.Token);
                guestAfter = await _server.BalanceAsync(guest.Token);
                if (hostAfter + guestAfter == startHost + startGuest)
                {
                    break;
                }

                await Task.Delay(200);
            }

            Assert.Equal(startHost + startGuest, hostAfter + guestAfter); // coins conserved
            Assert.Equal(600, Math.Max(hostAfter, guestAfter));           // pot paid to the winner
            Assert.Equal(400, Math.Min(hostAfter, guestAfter));

            // Waiting longer must not pay the pot a second time.
            await Task.Delay(2500);
            Assert.Equal(hostAfter, await _server.BalanceAsync(host.Token));
            Assert.Equal(guestAfter, await _server.BalanceAsync(guest.Token));

            await host.DisposeAsync();
            await guest.DisposeAsync();
        }

        [Fact]
        public async Task A_finished_match_does_not_trap_players_in_the_old_room()
        {
            TestClient a = await NewClientAsync("TrapA");
            TestClient b = await NewClientAsync("TrapB");

            await a.CreateRoomAsync(2, 0);
            string code = a.WaitFor(e => e.Type == MessageTypes.RoomState && e.Room != null).Room.Code;
            await b.JoinRoomAsync(code);
            WaitForRoomPopulation(new[] { a, b }, 2);
            await a.StartGameAsync();
            WaitForMatchStart(new[] { a, b });

            await PlayToEndAsync(new[] { a, b });

            // The player is still a member of the finished room at this point. A new search
            // must not hand back that dead match.
            await a.FindMatchAsync(2, 0);
            // The finished-room broadcast is still queued in the inbox, so ask specifically
            // for a lobby snapshot: that can only come from a genuinely new table.
            ServerEnvelope queued = a.WaitFor(e => (e.Type == MessageTypes.Queued || e.Type == MessageTypes.RoomState) &&
                                                   e.Room != null && e.Room.Status == RoomStatusCodes.Lobby,
                because: "a new search after a finished match must be answered with a lobby");

            Assert.Equal(RoomStatusCodes.Lobby, queued.Room.Status);
            Assert.NotEqual(code, queued.Room.Code);
            Assert.Single(queued.Room.Members);

            await a.DisposeAsync();
            await b.DisposeAsync();
        }

        [Fact]
        public async Task Three_player_private_room_starts_with_three_distinct_colors_and_is_full()
        {
            TestClient host = await NewClientAsync("RoomHost");
            TestClient b = await NewClientAsync("RoomTwo");
            TestClient c = await NewClientAsync("RoomThree");
            TestClient late = await NewClientAsync("RoomFour");

            await host.CreateRoomAsync(3, 0);
            string code = host.WaitFor(e => e.Type == MessageTypes.RoomState && e.Room != null).Room.Code;

            await b.JoinRoomAsync(code);
            await c.JoinRoomAsync(code);
            WaitForRoomPopulation(new[] { host, b, c }, 3);

            await late.JoinRoomAsync(code);
            ServerEnvelope rejection = late.WaitFor(e => e.Type == MessageTypes.CommandRejected,
                because: "a fourth player must not fit in a three-seat room");
            Assert.Contains("full", rejection.Error, StringComparison.OrdinalIgnoreCase);

            await host.StartGameAsync();
            WaitForMatchStart(new[] { host, b, c });
            await late.ResyncAsync();
            late.WaitFor(e => e.Type == MessageTypes.CommandRejected || e.Type == MessageTypes.RoomState);

            GameSnapshot opening = host.Latest;
            Assert.Equal(3, opening.Seats.Length);
            Assert.Equal(3, opening.Seats.Select(s => s.Color).Distinct().Count());
            Assert.Equal(0, opening.FinishedCount);

            await host.DisposeAsync();
            await b.DisposeAsync();
            await c.DisposeAsync();
            await late.DisposeAsync();
        }

        [Fact]
        public async Task Disconnected_player_reconnects_into_the_same_match()
        {
            TestClient a = await NewClientAsync("ReconA");
            TestClient b = await NewClientAsync("ReconB");

            await a.CreateRoomAsync(2, 0);
            string code = a.WaitFor(e => e.Type == MessageTypes.RoomState && e.Room != null).Room.Code;
            await b.JoinRoomAsync(code);
            WaitForRoomPopulation(new[] { a, b }, 2);
            await a.StartGameAsync();
            WaitForMatchStart(new[] { a, b });

            // Play one turn so there is state worth restoring.
            TestClient active = new[] { a, b }.First(x => x.Latest.ActiveSeat == x.Seat);
            Assert.True(active.Latest.Phase == (int)GamePhase.AwaitingRoll,
                "a fresh match must open in AwaitingRoll; A[" + Trace(a) + "] B[" + Trace(b) + "]");
            int versionBefore = active.Latest.Version;
            await active.RollAsync();
            active.WaitForState(versionBefore);

            GameSnapshot beforeDrop = b.Latest;

            // Hard-drop one socket; the opponent must be told the seat went offline.
            active.Kill();
            b.WaitFor(e => e.Type == MessageTypes.PlayerConnection && e.Room != null &&
                           e.Room.Members.Any(m => !m.Connected),
                because: "opponent was not told about the dropped connection");

            // Reconnect with the same account: same room, same game, same seat.
            TestClient reconnected = await TestClient.ConnectAsync(_server.BaseUrl, active.Name, active.Token, active.PlayerId);
            ServerEnvelope restored = reconnected.WaitFor(e => e.Type == MessageTypes.GameStateMsg && e.State != null,
                because: "reconnect did not replay the game state");

            Assert.True(restored.State.Version >= beforeDrop.Version);
            Assert.Equal(beforeDrop.Seats.Length, restored.State.Seats.Length);
            Assert.Equal(beforeDrop.Seats.Select(s => s.PlayerId), restored.State.Seats.Select(s => s.PlayerId));

            // And the returned player can still act when the turn comes back to it. A roll
            // that cannot move passes the turn, so the opponent gets to play in between.
            int opponentActedVersion = -1;
            DateTime until = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < until)
            {
                GameSnapshot mine = reconnected.Latest;
                if (mine != null && mine.ActiveSeat == reconnected.Seat && mine.WinnerSeat < 0)
                {
                    break;
                }

                GameSnapshot theirs = b.Latest;
                if (theirs != null && theirs.ActiveSeat == b.Seat && theirs.Version > opponentActedVersion)
                {
                    opponentActedVersion = theirs.Version;
                    if (theirs.LegalTokens != null && theirs.LegalTokens.Length > 0)
                    {
                        await b.MoveAsync(theirs.LegalTokens[0]);
                    }
                    else
                    {
                        await b.RollAsync();
                    }
                }

                await Task.Delay(4);
            }

            bool gotTurn = reconnected.WaitForMyTurn();

            // Act on whatever the authoritative phase actually is: the seat may already be
            // waiting for a move if the server rolled for it.
            GameSnapshot afterReconnect = reconnected.Latest;
            Assert.True(gotTurn,
                "reconnected player never got control of its seat (my seat " + reconnected.Seat +
                ", active " + (afterReconnect?.ActiveSeat ?? -1) +
                ", phase " + (afterReconnect?.Phase ?? -1) +
                ", winner " + (afterReconnect?.WinnerSeat ?? -99) +
                ", version " + (afterReconnect?.Version ?? -1) +
                ", seats " + active.Seat + "/" + b.Seat +
                ") trace[" + Trace(reconnected) + "] other[" + Trace(b) + "]\nserver log:\n" + _server.LogTail());
            int versionNow = reconnected.Latest.Version;
            if (reconnected.Latest.Phase == (int)GamePhase.AwaitingMove &&
                reconnected.Latest.LegalTokens != null && reconnected.Latest.LegalTokens.Length > 0)
            {
                await reconnected.MoveAsync(reconnected.Latest.LegalTokens[0]);
            }
            else
            {
                await reconnected.RollAsync();
            }

            reconnected.WaitForState(versionNow);

            b.WaitFor(e => e.Type == MessageTypes.PlayerConnection && e.Room != null &&
                           e.Room.Members.All(m => m.Connected),
                because: "opponent was not told about the reconnect");

            await a.DisposeAsync();
            await b.DisposeAsync();
            await reconnected.DisposeAsync();
        }

        [Fact]
        public async Task Server_rejects_out_of_turn_rolls_forged_moves_and_duplicate_commands()
        {
            TestClient a = await NewClientAsync("CheatA");
            TestClient b = await NewClientAsync("CheatB");

            await a.CreateRoomAsync(2, 0);
            string code = a.WaitFor(e => e.Type == MessageTypes.RoomState && e.Room != null).Room.Code;
            await b.JoinRoomAsync(code);
            WaitForRoomPopulation(new[] { a, b }, 2);
            await a.StartGameAsync();
            WaitForMatchStart(new[] { a, b });

            TestClient active = new[] { a, b }.First(x => x.Latest.ActiveSeat == x.Seat);
            TestClient passive = ReferenceEquals(active, a) ? b : a;

            // 1. Out of turn, including a forged die value in the payload. The extra "die"
            //    field must be ignored - the server never reads a dice value from a client.
            await passive.SendAsync(new { type = MessageTypes.Roll, commandId = TestClient.NewId(), die = 6 });
            ServerEnvelope outOfTurn = passive.WaitFor(e => e.Type == MessageTypes.CommandRejected);
            Assert.Equal(ErrorCodes.NotYourTurn, outOfTurn.ErrorCode);
            Assert.False(string.IsNullOrWhiteSpace(outOfTurn.Error));

            // 2. Forged move: token index out of range.
            int version = active.Latest.Version;
            await active.MoveAsync(99);
            ServerEnvelope forged = active.WaitFor(e => e.Type == MessageTypes.CommandRejected);
            Assert.Equal(ErrorCodes.InvalidPayload, forged.ErrorCode);
            Assert.Equal(version, active.Latest.Version); // nothing changed

            // 3. Duplicate command id: the retry must neither roll nor move twice.
            string commandId = TestClient.NewId();
            await active.RollAsync(commandId);
            ServerEnvelope first = active.WaitFor(e => e.Type == MessageTypes.GameStateMsg && e.State.Version > version);
            int afterRoll = first.State.Version;

            await active.RollAsync(commandId);
            ServerEnvelope duplicate = active.WaitFor(e => e.Type == MessageTypes.GameStateMsg &&
                                                           e.CommandId == null && e.State != null);
            Assert.Equal(afterRoll, duplicate.State.Version);

            // 4. Illegal token choice (a token the engine says cannot move now).
            GameSnapshot snapshot = active.Latest;
            if (snapshot.LegalTokens != null && snapshot.LegalTokens.Length > 0)
            {
                int illegal = Enumerable.Range(0, 4).FirstOrDefault(i => !snapshot.LegalTokens.Contains(i));
                if (!snapshot.LegalTokens.Contains(illegal))
                {
                    await active.MoveAsync(illegal);
                    ServerEnvelope illegalMove = active.WaitFor(e => e.Type == MessageTypes.CommandRejected);
                    Assert.Equal(ErrorCodes.IllegalMove, illegalMove.ErrorCode);
                }
            }

            // 5. A player who is not in a room cannot roll at all.
            TestClient outsider = await NewClientAsync("CheatOutsider");
            await outsider.RollAsync();
            ServerEnvelope notInRoom = outsider.WaitFor(e => e.Type == MessageTypes.CommandRejected);
            Assert.Equal(ErrorCodes.NotInRoom, notInRoom.ErrorCode);

            await a.DisposeAsync();
            await b.DisposeAsync();
            await outsider.DisposeAsync();
        }

        [Fact]
        public async Task Matchmaking_cancel_returns_the_player_to_the_queue_cleanly()
        {
            TestClient client = await NewClientAsync("QueueGuy");

            await client.FindMatchAsync(4, 0);
            client.WaitFor(e => e.Type == MessageTypes.Queued || e.Type == MessageTypes.RoomState);

            await client.CancelMatchAsync();
            ServerEnvelope cancelled = client.WaitFor(e => e.Type == MessageTypes.MatchCancelled,
                because: "cancelMatch must be answered");
            Assert.Null(cancelled.Room);

            // A second search must work after cancelling.
            await client.FindMatchAsync(2, 0);
            client.WaitFor(e => e.Type == MessageTypes.Queued || e.Type == MessageTypes.RoomState ||
                                e.Type == MessageTypes.MatchFound);

            await client.DisposeAsync();
        }

        [Fact]
        public void Client_protocol_has_no_field_for_dice_or_results()
        {
            var properties = typeof(ClientEnvelope).GetProperties().Select(p => p.Name).ToList();

            Assert.DoesNotContain("Die", properties);
            Assert.DoesNotContain("Dice", properties);
            Assert.DoesNotContain("Result", properties);
            Assert.DoesNotContain("Winner", properties);
            Assert.DoesNotContain("State", properties);
            Assert.Contains("CommandId", properties);
        }
    }
}
