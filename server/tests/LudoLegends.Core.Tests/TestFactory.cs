using System;
using System.Collections.Generic;
using LudoLegends.Core;


namespace LudoLegends.Core.Tests
{
    /// <summary>Deterministic die source for tests. Repeats the scripted values in order.</summary>
    internal sealed class ScriptedRandom : IRandomSource
    {
        private readonly int[] _values;
        private int _index;

        public ScriptedRandom(params int[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("At least one die value is required.", nameof(values));
            }

            _values = values;
        }

        public int NextDie()
        {
            int value = _values[_index % _values.Length];
            _index++;
            return value;
        }
    }

    internal static class TestFactory
    {
        public static LudoGame TwoPlayer(IRandomSource random = null, int tokens = 4)
        {
            var rules = GameRules.Standard();
            rules.TokensPerPlayer = tokens;

            var seats = new List<SeatDefinition>
            {
                new SeatDefinition("p1", "Player One", PlayerColor.Red, false),
                new SeatDefinition("p2", "Player Two", PlayerColor.Green, false)
            };

            return LudoGame.Start(rules, seats, random ?? new ScriptedRandom(1, 1, 1, 1, 1, 1));
        }

        public static LudoGame Seats(int count, IRandomSource random = null)
        {
            var rules = GameRules.Standard();
            var seats = new List<SeatDefinition>();
            PlayerColor[] colors = BoardTopology.ColorsForSeatCount(count);
            for (int i = 0; i < count; i++)
            {
                seats.Add(new SeatDefinition("p" + (i + 1), "Player " + (i + 1), colors[i], false));
            }

            return LudoGame.Start(rules, seats, random ?? new ScriptedRandom(1, 1, 1, 1));
        }

        /// <summary>Puts specific tokens of a seat at specific progress values.</summary>
        public static void Place(LudoGame game, int seatIndex, params int[] progress)
        {
            SeatState seat = game.State.Seats[seatIndex];
            for (int i = 0; i < seat.Tokens.Length; i++)
            {
                seat.Tokens[i] = -1;
            }

            for (int i = 0; i < progress.Length && i < seat.Tokens.Length; i++)
            {
                seat.Tokens[i] = progress[i];
            }
        }

        /// <summary>Progress a token of <paramref name="color"/> needs to sit on an absolute ring cell.</summary>
        public static int ProgressAt(PlayerColor color, int absoluteCell)
        {
            int start = BoardTopology.StartOffset(color);
            int progress = absoluteCell - start;
            while (progress < 0)
            {
                progress += BoardTopology.TrackCells;
            }

            return progress % BoardTopology.TrackCells;
        }
    }
}
