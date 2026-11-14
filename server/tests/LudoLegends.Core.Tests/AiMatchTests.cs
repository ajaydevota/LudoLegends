using System.Collections.Generic;
using System.Linq;
using LudoLegends.Core;
using Xunit;

namespace LudoLegends.Core.Tests
{
    /// <summary>
    /// The offline modes (versus AI, and the AI takeover used online for abandoned seats) run
    /// entirely on the shared engine with no server involved. These tests drive complete
    /// matches with bot decisions only, which is exactly what the Unity client does locally.
    /// </summary>
    public sealed class AiMatchTests
    {
        private static LudoGame StartBotGame(int seats)
        {
            PlayerColor[] colors = BoardTopology.ColorsForSeatCount(seats);
            var definitions = new List<SeatDefinition>();
            for (int i = 0; i < seats; i++)
            {
                definitions.Add(new SeatDefinition("bot" + i, "Bot " + (i + 1), colors[i], true));
            }

            return LudoGame.Start(GameRules.Standard(), definitions, new CryptoRandomSource());
        }

        /// <summary>Plays a whole match using only the AI, mirroring the offline client loop.</summary>
        private static int PlayOut(LudoGame game)
        {
            int steps = 0;
            while (!game.State.IsOver)
            {
                Assert.True(steps < 20000, "match did not terminate");

                if (game.State.Phase == GamePhase.AwaitingRoll)
                {
                    CommandResult roll = game.Roll(game.State.ActiveSeat, "bot-roll-" + steps);
                    Assert.True(roll.Accepted);
                }
                else
                {
                    MoveOption move = LudoAi.ChooseMove(game);
                    Assert.NotNull(move);
                    CommandResult applied = game.MoveToken(game.State.ActiveSeat, move.TokenIndex, "bot-move-" + steps);
                    Assert.True(applied.Accepted);
                }

                steps++;
            }

            return steps;
        }

        private static IEnumerable<int> Homes(LudoGame game)
        {
            foreach (SeatState seat in game.State.Seats)
            {
                yield return seat.TokensHome();
            }
        }

        [Fact]
        public void Two_bots_play_a_full_match_to_a_winner()
        {
            LudoGame game = StartBotGame(2);

            int steps = PlayOut(game);

            Assert.True(game.State.IsOver);
            Assert.True(game.State.WinnerSeat >= 0);
            Assert.Single(game.State.FinishOrder);
            Assert.True(steps > 20, "a real match takes many actions");
        }

        [Fact]
        public void Four_bots_play_a_full_match_and_leave_exactly_one_unfinished()
        {
            LudoGame game = StartBotGame(4);

            PlayOut(game);

            Assert.True(game.State.IsOver);
            Assert.Equal(3, game.State.FinishOrder.Count);
            Assert.Equal(4, game.State.SeatCount);
        }

        [Fact]
        public void Ai_never_produces_an_illegal_move_across_a_whole_match()
        {
            LudoGame game = StartBotGame(4);
            int checks = 0;
            int steps = 0;

            while (!game.State.IsOver)
            {
                Assert.True(steps++ < 20000,
                    "match did not terminate: phase " + game.State.Phase +
                    " active " + game.State.ActiveSeat +
                    " finished " + game.State.FinishedCount +
                    " order " + string.Join(",", game.State.FinishOrder) +
                    " version " + game.State.Version +
                    " tokensHome " + string.Join("/", Homes(game)) +
                    " tokens " + string.Join(" ", game.State.Seats.Select(s => string.Join(":", s.Tokens))));
                int version = game.State.Version;
                int active = game.State.ActiveSeat;
                GamePhase phase = game.State.Phase;

                if (phase == GamePhase.AwaitingRoll)
                {
                    // Unique id per command: the engine deliberately treats a repeated id as an
                    // idempotent retry, so reusing one would stall this loop forever.
                    CommandResult rolled = game.Roll(active, "legal-roll-" + steps);
                    Assert.True(rolled.Accepted,
                        "roll rejected: " + rolled.Status + " (seat " + active + ", version " + version + ")");
                }
                else
                {
                    MoveOption move = LudoAi.ChooseMove(game);
                    Assert.NotNull(move);

                    // Whatever the AI picks must be in the engine's own legal-move list.
                    IReadOnlyList<MoveOption> legal = game.LegalMoves(active);
                    Assert.Contains(legal, option => option.TokenIndex == move.TokenIndex);

                    CommandResult applied = game.MoveToken(active, move.TokenIndex, "legal-move-" + checks);
                    Assert.True(applied.Accepted,
                        "move rejected: " + applied.Status + " token " + move.TokenIndex +
                        " (seat " + active + ", version " + version + ")");
                    checks++;
                }
            }

            Assert.True(checks > 10);
        }
    }
}
