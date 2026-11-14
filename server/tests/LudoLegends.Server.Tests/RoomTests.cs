using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LudoLegends.Core;
using LudoLegends.Server.Config;
using LudoLegends.Server.Net;
using LudoLegends.Server.Rooms;
using LudoLegends.Server.Economy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LudoLegends.Server.Tests
{
    public class GameRoomTests
    {
        private static GameRoom NewRoom(FakeClock clock, IRandomSource random, int seats = 2, long fee = 0)
        {
            var room = new GameRoom(
                "ROOM01",
                seats,
                fee,
                "p1",
                GameRules.Standard(),
                random,
                clock.Read,
                25,
                60);

            for (int i = 1; i <= seats; i++)
            {
                room.AddPlayer(new RoomPlayerInfo { AccountId = "p" + i, DisplayName = "Player " + i, Level = 1 });
            }

            return room;
        }

        private static GameRoom StartedRoom(FakeClock clock, IRandomSource random, int seats = 2)
        {
            GameRoom room = NewRoom(clock, random, seats);
            (bool ok, string error) = room.TryPrepareStart("p1");
            Assert.True(ok, error);
            room.StartMatch("match-" + seats);
            return room;
        }

        [Fact]
        public void Seats_are_assigned_in_join_order()
        {
            GameRoom room = NewRoom(new FakeClock(), new ScriptedRandom(6));

            RoomSnapshot snapshot = room.Snapshot();

            Assert.Equal(2, snapshot.Members.Length);
            Assert.Equal("p1", snapshot.Members[0].PlayerId);
            Assert.Equal(0, snapshot.Members[0].Seat);
            Assert.Equal("p2", snapshot.Members[1].PlayerId);
            Assert.Equal(1, snapshot.Members[1].Seat);
            Assert.Equal("p1", snapshot.HostPlayerId);
        }

        [Fact]
        public void Start_requires_every_seat_to_be_filled()
        {
            var clock = new FakeClock();
            var room = new GameRoom("ROOM02", 4, 0, "p1", GameRules.Standard(), new ScriptedRandom(6), clock.Read, 25, 60);
            room.AddPlayer(new RoomPlayerInfo { AccountId = "p1", DisplayName = "One" });
            room.AddPlayer(new RoomPlayerInfo { AccountId = "p2", DisplayName = "Two" });

            (bool ok, string error) = room.TryPrepareStart("p1");

            Assert.False(ok);
            Assert.Contains("more player", error);
            Assert.Equal(RoomStatus.Lobby, room.Status);
        }

        [Fact]
        public void Only_the_host_can_start_a_private_room()
        {
            var clock = new FakeClock();
            GameRoom room = NewRoom(clock, new ScriptedRandom(6));

            (bool ok, string error) = room.TryPrepareStart("p2");

            Assert.False(ok);
            Assert.Contains("host", error);
        }

        [Fact]
        public void A_roll_from_the_waiting_player_is_rejected()
        {
            GameRoom room = StartedRoom(new FakeClock(), new ScriptedRandom(6));

            RoomCommandResult result = room.HandleCommand("p2", new ClientEnvelope
            {
                Type = MessageTypes.Roll,
                CommandId = "c1"
            });

            Assert.False(result.Accepted);
            Assert.NotNull(result.Direct);
            Assert.Equal("not_your_turn", result.Direct.ErrorCode);
            Assert.Equal(GamePhase.AwaitingRoll, room.Game.State.Phase);
            Assert.Equal(0, room.Game.State.ActiveSeat);
        }

        [Fact]
        public void A_retried_roll_command_does_not_roll_twice()
        {
            GameRoom room = StartedRoom(new FakeClock(), new ScriptedRandom(6, 6, 6));

            RoomCommandResult first = room.HandleCommand("p1", new ClientEnvelope { Type = MessageTypes.Roll, CommandId = "same" });
            int versionAfterFirst = room.Game.State.Version;
            int dieAfterFirst = room.Game.State.LastDie;

            RoomCommandResult retry = room.HandleCommand("p1", new ClientEnvelope { Type = MessageTypes.Roll, CommandId = "same" });

            Assert.True(first.Accepted);
            Assert.True(retry.Accepted); // reported as duplicate, not an error
            Assert.Equal(versionAfterFirst, room.Game.State.Version);
            Assert.Equal(dieAfterFirst, room.Game.State.LastDie);
            Assert.Empty(retry.Broadcast);
            Assert.NotNull(retry.Direct);
            Assert.Equal(versionAfterFirst, retry.Direct.State.Version);
        }

        [Fact]
        public void An_illegal_token_index_is_rejected_and_turn_continues()
        {
            GameRoom room = StartedRoom(new FakeClock(), new ScriptedRandom(6));
            GameState state = room.Game.State;

            // Force a specific legal-move situation: only token 0 is on the board.
            state.Seats[0].Tokens = new[] { 10, -1, -1, -1 };
            state.Phase = GamePhase.AwaitingMove;
            state.LastDie = 3;
            state.ActiveSeat = 0;

            RoomCommandResult result = room.HandleCommand("p1", new ClientEnvelope
            {
                Type = MessageTypes.MoveToken,
                TokenIndex = 2,
                CommandId = "m1"
            });

            Assert.False(result.Accepted);
            Assert.Equal("illegal_move", result.Direct.ErrorCode);
            Assert.Equal(10, state.Seats[0].Tokens[0]);
            Assert.Equal(-1, state.Seats[0].Tokens[2]);
        }

        [Fact]
        public void Moves_are_validated_against_the_pending_die_value()
        {
            GameRoom room = StartedRoom(new FakeClock(), new ScriptedRandom(6));
            GameState state = room.Game.State;

            state.Seats[0].Tokens = new[] { 55, -1, -1, -1 };
            state.Phase = GamePhase.AwaitingMove;
            state.LastDie = 3; // would overshoot home
            state.ActiveSeat = 0;

            RoomCommandResult result = room.HandleCommand("p1", new ClientEnvelope
            {
                Type = MessageTypes.MoveToken,
                TokenIndex = 0,
                CommandId = "m2"
            });

            Assert.False(result.Accepted);
            Assert.Equal("illegal_move", result.Direct.ErrorCode);
            Assert.Equal(55, state.Seats[0].Tokens[0]);
        }

        [Fact]
        public void Reconnecting_replays_the_authoritative_state()
        {
            GameRoom room = StartedRoom(new FakeClock(), new ScriptedRandom(6));

            room.HandleCommand("p1", new ClientEnvelope { Type = MessageTypes.Roll, CommandId = "r1" });
            room.Detach("p1");

            List<ServerEnvelope> envelopes = room.Attach("p1");

            Assert.Contains(envelopes, e => e.Type == MessageTypes.RoomState);
            ServerEnvelope gameState = Assert.Single(envelopes, e => e.Type == MessageTypes.GameStateMsg);
            Assert.NotNull(gameState.State);
            Assert.Equal(room.Game.State.Version, gameState.State.Version);
            Assert.Equal(room.Game.State.ActiveSeat, gameState.State.ActiveSeat);
            Assert.True(room.Players.Single(p => p.AccountId == "p1").Connected);
        }

        [Fact]
        public void Server_auto_plays_when_the_turn_deadline_passes()
        {
            var clock = new FakeClock();
            var room = StartedRoom(clock, new ScriptedRandom(6));
            int versionBefore = room.Game.State.Version;

            clock.Advance(26_000);
            List<ServerEnvelope> envelopes = room.Tick();

            Assert.NotEmpty(envelopes);
            Assert.True(room.Game.State.Version > versionBefore);
        }

        [Fact]
        public void Disconnected_player_is_auto_played_after_the_grace_period()
        {
            var clock = new FakeClock();
            var room = StartedRoom(clock, new ScriptedRandom(6));

            room.Detach("p1");
            Assert.False(room.Players.Single(p => p.AccountId == "p1").Connected);

            clock.Advance(61_000);
            List<ServerEnvelope> envelopes = room.Tick();

            Assert.NotEmpty(envelopes);
            Assert.True(room.Players.Single(p => p.AccountId == "p1").AutoPilot);
            Assert.Equal(RoomStatus.Playing, room.Status);
        }

        [Fact]
        public void Leaving_mid_match_keeps_the_seat_and_never_deletes_tokens()
        {
            GameRoom room = StartedRoom(new FakeClock(), new ScriptedRandom(6));
            room.Game.State.Seats[1].Tokens = new[] { 12, 20, -1, -1 };

            (bool ok, string error) = room.RemovePlayer("p2");

            Assert.True(ok, error);
            Assert.Equal(2, room.Snapshot().Members.Length);
            Assert.Equal(12, room.Game.State.Seats[1].Tokens[0]);
            Assert.Equal(20, room.Game.State.Seats[1].Tokens[1]);
            Assert.True(room.Players.Single(p => p.AccountId == "p2").AutoPilot);
        }

        [Fact]
        public void Finishing_produces_a_settlement_with_ranks_and_winner()
        {
            GameRoom room = StartedRoom(new FakeClock(), new ScriptedRandom(6));
            GameState state = room.Game.State;

            state.Seats[0].Tokens = new[] { 57, 57, 57, 56 };
            state.Phase = GamePhase.AwaitingMove;
            state.LastDie = 1;
            state.ActiveSeat = 0;

            RoomCommandResult result = room.HandleCommand("p1", new ClientEnvelope
            {
                Type = MessageTypes.MoveToken,
                TokenIndex = 3,
                CommandId = "win"
            });

            Assert.True(result.Accepted);
            Assert.True(room.Game.State.IsOver);
            Assert.Equal(RoomStatus.Finished, room.Status);

            MatchOutcome outcome = room.DrainOutcome();
            Assert.NotNull(outcome);
            Assert.Equal("p1", outcome.WinnerAccountId);
            Assert.Equal(1, outcome.Seats.Single(s => s.AccountId == "p1").Rank);
            Assert.Equal(4, outcome.Seats.Single(s => s.AccountId == "p1").TokensHome);
            Assert.Equal(2, outcome.Seats.Single(s => s.AccountId == "p2").Rank);

            Assert.Null(room.DrainOutcome());
        }

        [Fact]
        public void Aborting_a_room_produces_a_refundable_outcome()
        {
            var clock = new FakeClock();
            GameRoom room = NewRoom(clock, new ScriptedRandom(6), 2, 150);
            room.StartMatch("match-abort");

            MatchOutcome outcome = room.Abort();

            Assert.NotNull(outcome);
            Assert.True(outcome.Aborted);
            Assert.Equal(150, outcome.EntryFee);
            Assert.Null(outcome.WinnerAccountId);
        }

        [Fact]
        public void Room_is_cleaned_up_when_everyone_is_gone()
        {
            var clock = new FakeClock();
            var room = new GameRoom("ROOM03", 2, 0, "p1", GameRules.Standard(), new ScriptedRandom(6), clock.Read, 25, 60);

            clock.Advance(11 * 60 * 1000);
            Assert.True(room.ShouldBeCleanedUp(clock.NowMs, 10));
        }

        [Fact]
        public void Client_protocol_has_no_field_for_dice_or_results()
        {
            // Anti-cheat by construction: there is no place in the protocol to assert a die
            // value, a token position or a match result. Everything the client sends is an
            // intent (type, room, token index, idempotency key).
            var suspicious = typeof(ClientEnvelope)
                .GetProperties()
                .Where(p => p.Name.IndexOf("die", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            p.Name.IndexOf("dice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            p.Name.IndexOf("result", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            p.Name.IndexOf("winner", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            Assert.Empty(suspicious);
        }
    }

    public class MatchmakingTests
    {
        private static RoomManager NewManager()
        {
            return new RoomManager(TestOptions.Create(), NullLogger<RoomManager>.Instance);
        }

        [Fact]
        public void Players_with_the_same_table_settings_share_a_room()
        {
            RoomManager manager = NewManager();

            (GameRoom first, string error1) = manager.FindOrCreateMatch(
                new RoomPlayerInfo { AccountId = "p1", DisplayName = "One" }, 2, 0);
            (GameRoom second, string error2) = manager.FindOrCreateMatch(
                new RoomPlayerInfo { AccountId = "p2", DisplayName = "Two" }, 2, 0);

            Assert.Null(error1);
            Assert.Null(error2);
            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.Equal(2, first.Players.Count);
            Assert.True(first.AutoStart);
        }

        [Fact]
        public void Different_seat_counts_get_different_tables()
        {
            RoomManager manager = NewManager();

            (GameRoom two, _) = manager.FindOrCreateMatch(new RoomPlayerInfo { AccountId = "p1" }, 2, 0);
            (GameRoom three, _) = manager.FindOrCreateMatch(new RoomPlayerInfo { AccountId = "p2" }, 3, 0);

            Assert.NotEqual(two.Code, three.Code);
            Assert.Equal(3, three.SeatCount);
        }

        [Fact]
        public void Private_room_codes_join_the_same_room_and_reject_strangers_to_missing_codes()
        {
            RoomManager manager = NewManager();

            GameRoom room = manager.CreatePrivateRoom(new RoomPlayerInfo { AccountId = "p1", DisplayName = "One" }, 4, 0, out string error);
            Assert.Null(error);

            (GameRoom joined, string joinError) = manager.JoinPrivateRoom(room.Code, new RoomPlayerInfo { AccountId = "p2", DisplayName = "Two" });
            Assert.Null(joinError);
            Assert.Same(room, joined);

            (GameRoom missing, string missingError) = manager.JoinPrivateRoom("NOPE00", new RoomPlayerInfo { AccountId = "p3" });
            Assert.Null(missing);
            Assert.Contains("not found", missingError);
        }

        [Fact]
        public void A_player_is_always_returned_to_their_existing_room()
        {
            RoomManager manager = NewManager();
            GameRoom privateRoom = manager.CreatePrivateRoom(new RoomPlayerInfo { AccountId = "p1", DisplayName = "One" }, 4, 0, out string error);
            Assert.Null(error);

            // Asking for matchmaking again (or reconnecting) must not create a second room.
            (GameRoom second, string secondError) = manager.FindOrCreateMatch(new RoomPlayerInfo { AccountId = "p1" }, 2, 0);

            Assert.Null(secondError);
            Assert.Same(privateRoom, second);
            Assert.Equal(1, manager.RoomCount);
        }
    }

    public class RateLimiterTests
    {
        [Fact]
        public void Burst_is_allowed_then_the_limiter_blocks()
        {
            var limiter = new SocketRateLimiter(2);
            int allowed = 0;

            for (int i = 0; i < 30; i++)
            {
                if (limiter.TryAcquire())
                {
                    allowed++;
                }
            }

            Assert.InRange(allowed, 1, 8);
        }

        [Fact]
        public void Limiter_refills_over_time()
        {
            var limiter = new SocketRateLimiter(10);
            for (int i = 0; i < 100; i++)
            {
                limiter.TryAcquire();
            }

            Thread.Sleep(300);
            Assert.True(limiter.TryAcquire());
        }
    }
}
