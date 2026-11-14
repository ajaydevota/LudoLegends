using System;

namespace LudoLegends.Core
{
    /// <summary>
    /// Geometry of the traditional 15x15 Ludo board.
    ///
    /// Track model
    /// -----------
    /// The main loop is a single ring of 52 cells. A token's position is expressed as a
    /// <em>progress</em> value relative to its own start cell:
    ///
    ///   -1        in the yard (base)
    ///    0..51    on the main ring. Absolute cell = (startOffset + progress) % 52
    ///   52..56    in the player's coloured home column (5 cells, walking inward)
    ///   57        home (the centre triangle)
    ///
    /// A token therefore needs exactly 57 steps from its start cell to reach home, and
    /// the last ring cell it occupies (progress 51) is the cell immediately before its
    /// own start cell - i.e. the entry square for its home column.
    ///
    /// Grid model
    /// ----------
    /// Cells are (X, Y) with X to the right and Y downwards, both 0..14.
    /// Arms are the 3-wide bands: left x0..5 y6..8, top y0..5 x6..8,
    /// right x9..14 y6..8, bottom y9..14 x6..8, centre 3x3 at x6..8 y6..8.
    /// </summary>
    public static class BoardTopology
    {
        public const int GridSize = 15;
        public const int TrackCells = 52;

        /// <summary>Last progress value that is still on the main ring.</summary>
        public const int LastTrackProgress = 51;

        /// <summary>Number of coloured cells in a home column (progress 52..56).</summary>
        public const int HomeColumnCells = 5;

        /// <summary>Progress value that means "home".</summary>
        public const int ProgressHome = 57;

        /// <summary>Centre square used to draw/stack finished tokens.</summary>
        public const int CenterCell = 7;

        /// <summary>
        /// The 52-cell ring, clockwise, starting at the top-left corner of the top arm.
        /// Index into this array is the "absolute cell" id used everywhere else.
        /// </summary>
        private static readonly int[] RingX =
        {
            6, 7, 8,                         // top edge of the top arm
            8, 8, 8, 8, 8,                   // right side of the top arm
            9, 10, 11, 12, 13, 14,           // top edge of the right arm
            14,                              // outer middle of the right arm
            14, 13, 12, 11, 10, 9,           // bottom edge of the right arm
            8, 8, 8, 8, 8, 8,                // right side of the bottom arm
            7, 6,                            // bottom edge of the bottom arm
            6, 6, 6, 6, 6,                   // left side of the bottom arm
            5, 4, 3, 2, 1, 0,                // bottom edge of the left arm
            0,                               // outer middle of the left arm
            0, 1, 2, 3, 4, 5,                // top edge of the left arm
            6, 6, 6, 6, 6                    // left side of the top arm
        };

        private static readonly int[] RingY =
        {
            0, 0, 0,
            1, 2, 3, 4, 5,
            6, 6, 6, 6, 6, 6,
            7,
            8, 8, 8, 8, 8, 8,
            9, 10, 11, 12, 13, 14,
            14, 14,
            13, 12, 11, 10, 9,
            8, 8, 8, 8, 8, 8,
            7,
            6, 6, 6, 6, 6, 6,
            5, 4, 3, 2, 1
        };

        /// <summary>
        /// Start offsets on the ring. Each is one cell after that colour's home-column entry.
        /// Red sits top-left, Blue top-right, Green bottom-right, Yellow bottom-left.
        /// </summary>
        private static readonly int[] StartOffsets = { 41, 2, 15, 28 };

        /// <summary>
        /// The eight safe squares: the four start squares plus the four "star" squares
        /// eight cells after each start. Tokens on these cells can never be captured.
        /// </summary>
        private static readonly int[] SafeCells = { 2, 10, 15, 23, 28, 36, 41, 49 };

        /// <summary>Yard (base) origin for each colour: the top-left cell of its 6x6 block.</summary>
        private static readonly int[] YardOriginX = { 0, 9, 9, 0 };
        private static readonly int[] YardOriginY = { 0, 0, 9, 9 };

        /// <summary>Home column cells per colour, ordered from the entry end inward (5 each).</summary>
        private static readonly int[][] HomeColX =
        {
            new[] { 1, 2, 3, 4, 5 },     // Red: left arm, walking right
            new[] { 7, 7, 7, 7, 7 },     // Blue: top arm, walking down
            new[] { 13, 12, 11, 10, 9 }, // Green: right arm, walking left
            new[] { 7, 7, 7, 7, 7 }      // Yellow: bottom arm, walking up
        };

        private static readonly int[][] HomeColY =
        {
            new[] { 7, 7, 7, 7, 7 },
            new[] { 1, 2, 3, 4, 5 },
            new[] { 7, 7, 7, 7, 7 },
            new[] { 13, 12, 11, 10, 9 }
        };

        /// <summary>Token slot offsets inside a 6x6 yard.</summary>
        private static readonly int[] BaseSlotOffsetX = { 1, 4, 1, 4 };
        private static readonly int[] BaseSlotOffsetY = { 1, 1, 4, 4 };

        static BoardTopology()
        {
            if (RingX.Length != TrackCells || RingY.Length != TrackCells)
            {
                throw new InvalidOperationException("Ring geometry must contain exactly 52 cells.");
            }

            if (HomeColX.Length != 4 || HomeColY.Length != 4)
            {
                throw new InvalidOperationException("Home column geometry must define four colours.");
            }
        }

        /// <summary>Start offset (absolute ring cell) for a colour.</summary>
        public static int StartOffset(PlayerColor color)
        {
            return StartOffsets[(int)color];
        }

        /// <summary>Absolute ring cell for a progress value in 0..51.</summary>
        public static int AbsoluteCell(PlayerColor color, int progress)
        {
            if (progress < 0 || progress > LastTrackProgress)
            {
                throw new ArgumentOutOfRangeException(nameof(progress), "Progress must be on the main ring (0..51).");
            }

            int cell = (StartOffsets[(int)color] + progress) % TrackCells;
            return cell < 0 ? cell + TrackCells : cell;
        }

        /// <summary>Grid X of an absolute ring cell (0..51).</summary>
        public static int TrackCellX(int absoluteCell)
        {
            CheckRing(absoluteCell);
            return RingX[absoluteCell];
        }

        /// <summary>Grid Y of an absolute ring cell (0..51).</summary>
        public static int TrackCellY(int absoluteCell)
        {
            CheckRing(absoluteCell);
            return RingY[absoluteCell];
        }

        /// <summary>True if the absolute ring cell is one of the eight safe squares.</summary>
        public static bool IsSafeCell(int absoluteCell)
        {
            for (int i = 0; i < SafeCells.Length; i++)
            {
                if (SafeCells[i] == absoluteCell)
                {
                    return true;
                }
            }
            return false;
        }

        public static int[] SafeCellList()
        {
            return (int[])SafeCells.Clone();
        }

        /// <summary>True if the absolute cell is the start square of any colour.</summary>
        public static bool IsStartCell(int absoluteCell)
        {
            for (int i = 0; i < StartOffsets.Length; i++)
            {
                if (StartOffsets[i] == absoluteCell)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Grid X of a home-column cell (progress 52..56) for a colour.</summary>
        public static int HomeColumnCellX(PlayerColor color, int index)
        {
            CheckHomeColumn(index);
            return HomeColX[(int)color][index];
        }

        /// <summary>Grid Y of a home-column cell (progress 52..56) for a colour.</summary>
        public static int HomeColumnCellY(PlayerColor color, int index)
        {
            CheckHomeColumn(index);
            return HomeColY[(int)color][index];
        }

        /// <summary>Yard origin (top-left of the 6x6 base block).</summary>
        public static void YardOrigin(PlayerColor color, out int x, out int y)
        {
            x = YardOriginX[(int)color];
            y = YardOriginY[(int)color];
        }

        /// <summary>Grid position of a token slot in the yard (tokenIndex 0..3).</summary>
        public static void BaseSlot(PlayerColor color, int tokenIndex, out int x, out int y)
        {
            if (tokenIndex < 0 || tokenIndex >= BaseSlotOffsetX.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(tokenIndex));
            }

            x = YardOriginX[(int)color] + BaseSlotOffsetX[tokenIndex];
            y = YardOriginY[(int)color] + BaseSlotOffsetY[tokenIndex];
        }

        /// <summary>
        /// Grid position of any progress value, including home. Home tokens all map to the
        /// centre; the renderer offsets them so the pile is visible.
        /// </summary>
        public static void ProgressToCell(PlayerColor color, int progress, out int x, out int y)
        {
            if (progress < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(progress), "Base tokens have no board cell.");
            }

            if (progress <= LastTrackProgress)
            {
                int abs = AbsoluteCell(color, progress);
                x = TrackCellX(abs);
                y = TrackCellY(abs);
                return;
            }

            if (progress < ProgressHome)
            {
                int index = progress - (LastTrackProgress + 1);
                x = HomeColumnCellX(color, index);
                y = HomeColumnCellY(color, index);
                return;
            }

            x = CenterCell;
            y = CenterCell;
        }

        /// <summary>True when the two ring cells are neighbours (used by geometry tests).</summary>
        public static bool AreRingNeighbours(int a, int b)
        {
            CheckRing(a);
            CheckRing(b);
            int dx = Math.Abs(RingX[a] - RingX[b]);
            int dy = Math.Abs(RingY[a] - RingY[b]);
            return dx + dy == 1;
        }

        /// <summary>Colours used for a given seat count (two-player games use opposite corners).</summary>
        public static PlayerColor[] ColorsForSeatCount(int seatCount)
        {
            switch (seatCount)
            {
                case 2:
                    return new[] { PlayerColor.Red, PlayerColor.Green };
                case 3:
                    return new[] { PlayerColor.Red, PlayerColor.Blue, PlayerColor.Green };
                case 4:
                    return new[] { PlayerColor.Red, PlayerColor.Blue, PlayerColor.Green, PlayerColor.Yellow };
                default:
                    throw new ArgumentOutOfRangeException(nameof(seatCount), "Seat count must be 2, 3 or 4.");
            }
        }

        private static void CheckRing(int absoluteCell)
        {
            if (absoluteCell < 0 || absoluteCell >= TrackCells)
            {
                throw new ArgumentOutOfRangeException(nameof(absoluteCell), "Ring cell must be 0..51.");
            }
        }

        private static void CheckHomeColumn(int index)
        {
            if (index < 0 || index >= HomeColumnCells)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Home column index must be 0..4.");
            }
        }
    }
}
