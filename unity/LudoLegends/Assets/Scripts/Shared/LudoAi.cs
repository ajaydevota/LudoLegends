using System;
using System.Collections.Generic;

namespace LudoLegends.Core
{
    /// <summary>
    /// Heuristic Ludo AI. Used for offline matches, for filling online rooms with bots,
    /// and by the server to auto-play a seat whose player dropped past the grace period.
    ///
    /// It evaluates every legal move with a small set of weights, which is fast enough to
    /// run on a 2 GB device and strong enough to feel reasonable to human players.
    /// </summary>
    public static class LudoAi
    {
        public const int WeightCapture = 1000;
        public const int WeightReachHome = 800;
        public const int WeightLeaveBase = 220;
        public const int WeightEnterHomeColumn = 300;
        public const int WeightLandSafe = 160;
        public const int WeightProgress = 1;

        /// <summary>
        /// Picks the best legal move for the active seat, or null when there is none.
        /// Ties are broken deterministically by token index so replays are stable.
        /// </summary>
        public static MoveOption ChooseMove(LudoGame game)
        {
            if (game == null)
            {
                throw new ArgumentNullException(nameof(game));
            }

            List<MoveOption> moves = game.LegalMoves(game.State.ActiveSeat);
            if (moves.Count == 0)
            {
                return null;
            }

            MoveOption best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < moves.Count; i++)
            {
                int score = Score(game, moves[i]);
                if (score > bestScore || (score == bestScore && best != null && moves[i].TokenIndex < best.TokenIndex))
                {
                    bestScore = score;
                    best = moves[i];
                }
            }

            return best;
        }

        /// <summary>Scores a single move; exposed for tests and for UI hints later on.</summary>
        public static int Score(LudoGame game, MoveOption move)
        {
            if (game == null || move == null)
            {
                return int.MinValue;
            }

            SeatState seat = game.State.Seats[game.State.ActiveSeat];
            int score = 0;

            if (move.Captures)
            {
                score += WeightCapture;
            }

            if (move.ReachesHome)
            {
                score += WeightReachHome;
            }

            if (move.LeavesBase)
            {
                score += WeightLeaveBase;
            }

            if (move.EntersHomeColumn)
            {
                score += WeightEnterHomeColumn;
            }

            if (move.Destination <= BoardTopology.LastTrackProgress)
            {
                int cell = BoardTopology.AbsoluteCell(seat.Color, move.Destination);
                if (BoardTopology.IsSafeCell(cell))
                {
                    score += WeightLandSafe;
                }
            }

            score += move.Destination * WeightProgress;

            return score;
        }

        /// <summary>
        /// Plays one full turn for the active seat: rolls, then moves if a move exists.
        /// Returns false when the game is over or the seat may not act.
        /// </summary>
        public static bool PlayTurn(LudoGame game, string commandPrefix)
        {
            if (game == null || game.State.IsOver)
            {
                return false;
            }

            if (game.State.Phase == GamePhase.AwaitingRoll)
            {
                game.Roll(game.State.ActiveSeat, commandPrefix + "-roll-" + game.State.Version);
            }

            if (game.State.Phase == GamePhase.AwaitingMove)
            {
                MoveOption move = ChooseMove(game);
                if (move != null)
                {
                    game.MoveToken(game.State.ActiveSeat, move.TokenIndex, commandPrefix + "-move-" + game.State.Version);
                }
            }

            return true;
        }
    }
}
