using System;
using System.Collections.Generic;
using System.Linq;
using LudoLegends.Core;
using Xunit;

namespace LudoLegends.Core.Tests
{
    public class BoardTopologyTests
    {
        [Fact]
        public void Ring_has_52_cells_and_every_step_is_a_neighbour()
        {
            int orthogonal = 0;
            int diagonalCorners = 0;

            for (int i = 0; i < BoardTopology.TrackCells; i++)
            {
                int next = (i + 1) % BoardTopology.TrackCells;
                int dx = Math.Abs(BoardTopology.TrackCellX(i) - BoardTopology.TrackCellX(next));
                int dy = Math.Abs(BoardTopology.TrackCellY(i) - BoardTopology.TrackCellY(next));

                if (dx + dy == 1)
                {
                    orthogonal++;
                }
                else if (dx == 1 && dy == 1)
                {
                    // The four inner corners of the cross turn diagonally around the centre
                    // triangle; this matches the printed board.
                    diagonalCorners++;
                }
                else
                {
                    throw new Xunit.Sdk.XunitException(
                        "Ring is not contiguous between cell " + i + " and " + next);
                }
            }

            Assert.Equal(52, BoardTopology.TrackCells);
            Assert.Equal(48, orthogonal);
            Assert.Equal(4, diagonalCorners);
        }

        [Fact]
        public void Ring_cells_are_unique()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < BoardTopology.TrackCells; i++)
            {
                string key = BoardTopology.TrackCellX(i) + "," + BoardTopology.TrackCellY(i);
                Assert.True(seen.Add(key), "Duplicate ring cell at index " + i);
            }
        }

        [Fact]
        public void Start_offsets_match_colour_yard_corners()
        {
            Assert.Equal(41, BoardTopology.StartOffset(PlayerColor.Red));
            Assert.Equal(2, BoardTopology.StartOffset(PlayerColor.Blue));
            Assert.Equal(15, BoardTopology.StartOffset(PlayerColor.Green));
            Assert.Equal(28, BoardTopology.StartOffset(PlayerColor.Yellow));
        }

        [Fact]
        public void Progress_left_loop_is_cell_before_start_and_enters_home_column()
        {
            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                int start = BoardTopology.StartOffset(color);
                int beforeStart = BoardTopology.AbsoluteCell(color, BoardTopology.LastTrackProgress);
                Assert.Equal((start + 51) % 52, beforeStart);

                // progress 52 is the first home-column cell
                int progress52 = 52;
                int index = progress52 - (BoardTopology.LastTrackProgress + 1);
                BoardTopology.ProgressToCell(color, progress52, out int x, out int y);
                Assert.Equal(BoardTopology.HomeColumnCellX(color, index), x);
                Assert.Equal(BoardTopology.HomeColumnCellY(color, index), y);

                // progress 57 is home (the centre)
                BoardTopology.ProgressToCell(color, BoardTopology.ProgressHome, out int hx, out int hy);
                Assert.Equal(BoardTopology.CenterCell, hx);
                Assert.Equal(BoardTopology.CenterCell, hy);
            }
        }

        [Fact]
        public void Total_steps_from_start_to_home_is_57()
        {
            Assert.Equal(57, BoardTopology.ProgressHome);
            Assert.Equal(52, BoardTopology.TrackCells);
            Assert.Equal(5, BoardTopology.HomeColumnCells);
        }

        [Fact]
        public void Eight_safe_cells_covering_all_start_squares()
        {
            int[] safe = BoardTopology.SafeCellList();
            Assert.Equal(8, safe.Length);
            Assert.Equal(8, safe.Distinct().Count());

            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                Assert.Contains(BoardTopology.StartOffset(color), safe);
            }

            for (int i = 0; i < safe.Length; i++)
            {
                Assert.True(BoardTopology.IsSafeCell(safe[i]));
            }

            Assert.False(BoardTopology.IsSafeCell(3));
        }

        [Fact]
        public void Home_column_cells_are_not_on_the_ring()
        {
            var ring = new HashSet<string>();
            for (int i = 0; i < BoardTopology.TrackCells; i++)
            {
                ring.Add(BoardTopology.TrackCellX(i) + "," + BoardTopology.TrackCellY(i));
            }

            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                for (int i = 0; i < BoardTopology.HomeColumnCells; i++)
                {
                    string key = BoardTopology.HomeColumnCellX(color, i) + "," + BoardTopology.HomeColumnCellY(color, i);
                    Assert.DoesNotContain(key, ring);
                }
            }
        }

        [Fact]
        public void Seat_colours_use_opposite_corners_for_two_players()
        {
            Assert.Equal(new[] { PlayerColor.Red, PlayerColor.Green }, BoardTopology.ColorsForSeatCount(2));
            Assert.Equal(3, BoardTopology.ColorsForSeatCount(3).Length);
            Assert.Equal(4, BoardTopology.ColorsForSeatCount(4).Length);
        }
    }

    public class MovementTests
    {
        [Fact]
        public void Token_needs_a_six_to_leave_the_base()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, -1, -1, -1, -1);

            CommandResult rolled = game.RollWithDie(0, 3, "c1");
            Assert.Equal(CommandStatus.Ok, rolled.Status);
            Assert.Empty(rolled.LegalTokens);
            Assert.Equal(GamePhase.AwaitingRoll, game.State.Phase);
            Assert.Equal(1, game.State.ActiveSeat);

            CommandResult rejected = game.RollWithDie(0, 3, "c2");
            Assert.Equal(CommandStatus.InvalidTurn, rejected.Status);
        }

        [Fact]
        public void Six_places_token_on_start_cell_and_grants_another_roll()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, -1, -1, -1, -1);

            game.RollWithDie(0, 6, "c1");
            Assert.Equal(GamePhase.AwaitingMove, game.State.Phase);
            Assert.Equal(new[] { 0, 1, 2, 3 }, game.LegalMoves(0).Select(m => m.TokenIndex).ToArray());

            CommandResult moved = game.MoveToken(0, 0, "c2");
            Assert.Equal(CommandStatus.Ok, moved.Status);
            Assert.Equal(0, game.State.Seats[0].Tokens[0]);
            Assert.Equal(GamePhase.AwaitingRoll, game.State.Phase);
            Assert.Equal(0, game.State.ActiveSeat);
            Assert.Contains(moved.Events, e => e.Kind == GameEventKind.TurnChanged && e.Seat == 0);
        }

        [Fact]
        public void Movement_without_a_pending_roll_is_rejected()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 0, -1, -1, -1);

            CommandResult result = game.MoveToken(0, 0, "c1");
            Assert.Equal(CommandStatus.InvalidPhase, result.Status);
        }

        [Fact]
        public void Out_of_turn_roll_is_rejected()
        {
            LudoGame game = TestFactory.TwoPlayer();
            CommandResult result = game.RollWithDie(1, 4, "c1");
            Assert.Equal(CommandStatus.InvalidTurn, result.Status);
            Assert.Equal(0, game.State.ActiveSeat);
        }

        [Fact]
        public void Illegal_die_value_is_rejected()
        {
            LudoGame game = TestFactory.TwoPlayer();
            Assert.Equal(CommandStatus.InvalidValue, game.RollWithDie(0, 0, "c1").Status);
            Assert.Equal(CommandStatus.InvalidValue, game.RollWithDie(0, 7, "c2").Status);
            Assert.Equal(1, game.State.Version);
        }

        [Fact]
        public void Rolling_twice_in_a_row_is_rejected()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 5, -1, -1, -1);

            Assert.Equal(CommandStatus.Ok, game.RollWithDie(0, 3, "c1").Status);
            CommandResult second = game.RollWithDie(0, 3, "c2");
            Assert.Equal(CommandStatus.InvalidPhase, second.Status);
            Assert.Equal(3, game.State.LastDie);
        }

        [Fact]
        public void Duplicate_command_id_is_ignored_and_does_not_advance_state()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 5, -1, -1, -1);

            CommandResult first = game.RollWithDie(0, 3, "same-id");
            Assert.Equal(CommandStatus.Ok, first.Status);
            Assert.Equal(3, game.State.LastDie);
            int versionAfterFirst = game.State.Version;

            CommandResult duplicate = game.RollWithDie(0, 3, "same-id");
            Assert.Equal(CommandStatus.Duplicate, duplicate.Status);
            Assert.Equal(versionAfterFirst, game.State.Version);
            Assert.Equal(3, game.State.LastDie);
        }

        [Fact]
        public void Landing_on_opponent_sends_token_back_to_base()
        {
            LudoGame game = TestFactory.TwoPlayer();
            // Red at track progress 0 (absolute 41). Rolling 3 lands on absolute 44.
            TestFactory.Place(game, 0, 0, -1, -1, -1);
            int greenProgress = TestFactory.ProgressAt(PlayerColor.Green, 44);
            TestFactory.Place(game, 1, greenProgress, -1, -1, -1);

            game.RollWithDie(0, 3, "c1");
            CommandResult moved = game.MoveToken(0, 0, "c2");

            Assert.Equal(CommandStatus.Ok, moved.Status);
            Assert.Equal(3, game.State.Seats[0].Tokens[0]);
            Assert.Equal(-1, game.State.Seats[1].Tokens[0]);
            Assert.Contains(moved.Events, e => e.Kind == GameEventKind.TokenCaptured && e.Seat == 1);
            Assert.Equal(0, game.State.ActiveSeat); // capture grants an extra turn
        }

        [Fact]
        public void Safe_cell_prevents_capture()
        {
            LudoGame game = TestFactory.TwoPlayer();
            // Absolute 49 is a safe star square. Red progress 7 + 1 = 8 -> absolute 49.
            TestFactory.Place(game, 0, 7, -1, -1, -1);
            int greenProgress = TestFactory.ProgressAt(PlayerColor.Green, 49);
            TestFactory.Place(game, 1, greenProgress, -1, -1, -1);

            game.RollWithDie(0, 1, "c1");
            CommandResult moved = game.MoveToken(0, 0, "c2");

            Assert.Equal(CommandStatus.Ok, moved.Status);
            Assert.Equal(greenProgress, game.State.Seats[1].Tokens[0]);
            Assert.DoesNotContain(moved.Events, e => e.Kind == GameEventKind.TokenCaptured);
            Assert.Equal(1, game.State.ActiveSeat); // no capture, no extra turn
        }

        [Fact]
        public void Two_opponent_tokens_block_the_cell()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 7, -1, -1, -1);
            int greenProgress = TestFactory.ProgressAt(PlayerColor.Green, 49);
            TestFactory.Place(game, 1, greenProgress, greenProgress, -1, -1);

            CommandResult rolled = game.RollWithDie(0, 1, "c1");
            Assert.Empty(rolled.LegalTokens);
            Assert.Contains(rolled.Events, e => e.Kind == GameEventKind.NoLegalMoves);
            Assert.Equal(1, game.State.ActiveSeat);
        }

        [Fact]
        public void Token_cannot_overshoot_home()
        {
            LudoGame game = TestFactory.TwoPlayer();
            // 55 is the last home-column cell before home (57).
            TestFactory.Place(game, 0, 55, -1, -1, -1);

            CommandResult rolled = game.RollWithDie(0, 3, "c1");
            Assert.Empty(rolled.LegalTokens);
            Assert.Equal(1, game.State.ActiveSeat);
        }

        [Fact]
        public void Exact_roll_reaches_home_and_grants_extra_turn()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 56, -1, -1, -1);

            game.RollWithDie(0, 1, "c1");
            Assert.Equal(new[] { 0 }, game.LegalMoves(0).Select(m => m.TokenIndex).ToArray());

            CommandResult moved = game.MoveToken(0, 0, "c2");
            Assert.Equal(BoardTopology.ProgressHome, game.State.Seats[0].Tokens[0]);
            Assert.Contains(moved.Events, e => e.Kind == GameEventKind.TokenHome);
            Assert.Equal(0, game.State.ActiveSeat);
            Assert.False(game.State.IsOver);
        }

        [Fact]
        public void Three_consecutive_sixes_forfeit_the_turn()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 0, -1, -1, -1);

            for (int i = 0; i < 2; i++)
            {
                game.RollWithDie(0, 6, "roll6-" + i);
                game.MoveToken(0, 0, "move6-" + i);
                Assert.Equal(0, game.State.ActiveSeat);
            }

            CommandResult third = game.RollWithDie(0, 6, "roll6-3");
            Assert.Contains(third.Events, e => e.Kind == GameEventKind.TurnForfeited);
            Assert.Equal(1, game.State.ActiveSeat);
            Assert.Equal(GamePhase.AwaitingRoll, game.State.Phase);
            Assert.Equal(0, game.State.ConsecutiveSixes);
        }

        [Fact]
        public void Turn_order_skips_finished_seats()
        {
            LudoGame game = TestFactory.Seats(3);
            TestFactory.Place(game, 0, 0, -1, -1, -1);
            TestFactory.Place(game, 1, 0, -1, -1, -1);
            TestFactory.Place(game, 2, 0, -1, -1, -1);

            game.RollWithDie(0, 2, "r0");
            game.MoveToken(0, 0, "m0");
            Assert.Equal(1, game.State.ActiveSeat);

            game.RollWithDie(1, 2, "r1");
            game.MoveToken(1, 0, "m1");
            Assert.Equal(2, game.State.ActiveSeat);

            game.State.Seats[0].Finished = true;
            game.RollWithDie(2, 2, "r2");
            game.MoveToken(2, 0, "m2");
            Assert.Equal(1, game.State.ActiveSeat); // seat 0 finished, so the turn wraps to seat 1
        }

        [Fact]
        public void First_player_to_bring_all_tokens_home_wins_two_player_game()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 57, 57, 57, 56);

            game.RollWithDie(0, 1, "c1");
            // token 3 is the one on 56; tokens 0..2 are already home
            Assert.Equal(new[] { 3 }, game.LegalMoves(0).Select(m => m.TokenIndex).ToArray());
            CommandResult moved = game.MoveToken(0, 3, "c2");

            Assert.Equal(CommandStatus.Ok, moved.Status);
            Assert.True(game.State.IsOver);
            Assert.Equal(0, game.State.WinnerSeat);
            Assert.Equal(1, game.State.Seats[0].Rank);
            Assert.Contains(moved.Events, e => e.Kind == GameEventKind.GameFinished);
        }

        [Fact]
        public void Game_rejects_commands_after_finish()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 57, 57, 57, 56);
            game.RollWithDie(0, 1, "c1");
            game.MoveToken(0, 3, "c2");

            CommandResult after = game.RollWithDie(1, 3, "c3");
            Assert.Equal(CommandStatus.GameFinished, after.Status);
        }

        [Fact]
        public void Snapshot_contains_authoritative_state_and_legal_tokens()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 0, -1, -1, -1);
            game.RollWithDie(0, 4, "c1");

            GameSnapshot snapshot = game.Snapshot();

            Assert.Equal(2, snapshot.Seats.Length);
            Assert.Equal((int)GamePhase.AwaitingMove, snapshot.Phase);
            Assert.Equal(0, snapshot.ActiveSeat);
            Assert.Equal(4, snapshot.LastDie);
            Assert.Equal(new[] { 0 }, snapshot.LegalTokens);
            Assert.Equal(0, snapshot.Seats[0].Tokens[0]);
            Assert.Equal(-1, snapshot.Seats[0].Tokens[1]);
            Assert.Equal(snapshot.Version, game.State.Version);
        }

        [Fact]
        public void Crypto_random_source_only_returns_valid_dice()
        {
            var source = new CryptoRandomSource();
            var counts = new int[7];
            for (int i = 0; i < 6000; i++)
            {
                int die = source.NextDie();
                Assert.InRange(die, 1, 6);
                counts[die]++;
            }

            for (int face = 1; face <= 6; face++)
            {
                Assert.InRange(counts[face], 800, 1200);
            }
        }
    }

    public class AiTests
    {
        [Fact]
        public void Ai_prefers_capture_over_plain_progress()
        {
            LudoGame game = TestFactory.TwoPlayer();
            TestFactory.Place(game, 0, 0, 10, -1, -1);
            int greenProgress = TestFactory.ProgressAt(PlayerColor.Green, 44);
            TestFactory.Place(game, 1, greenProgress, -1, -1, -1);

            game.RollWithDie(0, 3, "c1");
            MoveOption chosen = LudoAi.ChooseMove(game);

            Assert.NotNull(chosen);
            Assert.Equal(0, chosen.TokenIndex);
            Assert.True(chosen.Captures);
        }

        [Fact]
        public void Ai_plays_a_turn_and_advances_the_game()
        {
            LudoGame game = TestFactory.TwoPlayer(new ScriptedRandom(6, 6, 6, 6, 6, 6, 6, 6, 6, 6));
            TestFactory.Place(game, 0, -1, -1, -1, -1);
            TestFactory.Place(game, 1, -1, -1, -1, -1);

            int versions = 0;
            for (int i = 0; i < 20 && !game.State.IsOver; i++)
            {
                int before = game.State.Version;
                LudoAi.PlayTurn(game, "ai");
                if (game.State.Version == before)
                {
                    break;
                }
                versions++;
            }

            Assert.True(versions > 0);
            Assert.True(game.State.Version > 1);
        }

        [Fact]
        public void Ai_returns_null_when_no_move_is_legal()
        {
            LudoGame game = TestFactory.TwoPlayer();
            Assert.Null(LudoAi.ChooseMove(game));
        }
    }
}
