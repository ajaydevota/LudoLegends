using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using LudoLegends.Core;

namespace LudoLegends.Tools.BoardPreview
{
    /// <summary>
    /// Renders the board purely from <see cref="BoardTopology"/>.
    ///
    /// This exists so the geometry can be reviewed without Unity: the resulting image is
    /// the same data the Unity board view consumes, so if the picture is right, the
    /// client's cell mapping is right. It also guards the topology against regressions.
    ///
    /// Usage: dotnet run --project tools/BoardPreview -- <output-directory>
    /// </summary>
    internal static class Program
    {
        private const int Cell = 40;
        private const int Margin = 24;
        private const int Size = BoardTopology.GridSize * Cell + Margin * 2;

        private static readonly (byte R, byte G, byte B) BoardBg = (245, 239, 225);
        private static readonly (byte R, byte G, byte B) RingFill = (255, 255, 255);
        private static readonly (byte R, byte G, byte B) GridLine = (201, 191, 168);
        private static readonly (byte R, byte G, byte B) SafeFill = (255, 233, 168);
        private static readonly (byte R, byte G, byte B) Ink = (58, 46, 34);

        private static readonly Dictionary<PlayerColor, (byte R, byte G, byte B)> Colors =
            new Dictionary<PlayerColor, (byte, byte, byte)>
            {
                { PlayerColor.Red, (229, 69, 63) },
                { PlayerColor.Blue, (47, 111, 224) },
                { PlayerColor.Green, (47, 168, 79) },
                { PlayerColor.Yellow, (232, 180, 39) }
            };

        private static int Main(string[] args)
        {
            string outputDir = args.Length > 0 ? args[0] : "artifacts";
            Directory.CreateDirectory(outputDir);

            var image = new Raster(Size, Size, BoardBg);

            // Yards
            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                BoardTopology.YardOrigin(color, out int ox, out int oy);
                var tint = Colors[color];
                image.FillRect(Margin + ox * Cell, Margin + oy * Cell, 6 * Cell, 6 * Cell, tint);

                // inner white plate with four token slots
                image.FillRect(Margin + ox * Cell + 12, Margin + oy * Cell + 12, 6 * Cell - 24, 6 * Cell - 24, (255, 250, 240));
                for (int token = 0; token < 4; token++)
                {
                    BoardTopology.BaseSlot(color, token, out int sx, out int sy);
                    image.FillCircle(
                        Margin + sx * Cell + Cell / 2,
                        Margin + sy * Cell + Cell / 2,
                        Cell / 3,
                        tint);
                    image.FillCircle(
                        Margin + sx * Cell + Cell / 2,
                        Margin + sy * Cell + Cell / 2,
                        Cell / 6,
                        (255, 255, 255));
                }
            }

            // Home columns
            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                for (int i = 0; i < BoardTopology.HomeColumnCells; i++)
                {
                    int x = BoardTopology.HomeColumnCellX(color, i);
                    int y = BoardTopology.HomeColumnCellY(color, i);
                    image.FillRect(Margin + x * Cell, Margin + y * Cell, Cell, Cell, Colors[color]);
                }
            }

            // Ring
            for (int cell = 0; cell < BoardTopology.TrackCells; cell++)
            {
                int x = BoardTopology.TrackCellX(cell);
                int y = BoardTopology.TrackCellY(cell);
                bool safe = BoardTopology.IsSafeCell(cell);
                image.FillRect(Margin + x * Cell, Margin + y * Cell, Cell, Cell, safe ? SafeFill : RingFill);
                image.StrokeRect(Margin + x * Cell, Margin + y * Cell, Cell, Cell, GridLine);

                if (safe)
                {
                    image.FillCircle(Margin + x * Cell + Cell / 2, Margin + y * Cell + Cell / 2, Cell / 8, Ink);
                }
            }

            // Start cells get their owner's colour and an arrow direction hint
            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                int start = BoardTopology.StartOffset(color);
                int x = BoardTopology.TrackCellX(start);
                int y = BoardTopology.TrackCellY(start);
                image.StrokeRect(Margin + x * Cell + 2, Margin + y * Cell + 2, Cell - 4, Cell - 4, Colors[color]);
                image.StrokeRect(Margin + x * Cell + 4, Margin + y * Cell + 4, Cell - 8, Cell - 8, Colors[color]);
            }

            // Centre home triangle
            int cx = Margin + BoardTopology.CenterCell * Cell + Cell / 2;
            int cy = Margin + BoardTopology.CenterCell * Cell + Cell / 2;
            int half = (int)(1.5 * Cell);
            // Four triangles filling the whole 3x3 centre, meeting at the middle point,
            // which is exactly how the printed board looks (no beige gaps at the corners).
            int left = cx - half, right = cx + half, top = cy - half, bottom = cy + half;
            image.FillTriangle(left, top, right, top, cx, cy, Colors[PlayerColor.Blue]);    // top
            image.FillTriangle(right, top, right, bottom, cx, cy, Colors[PlayerColor.Green]); // right
            image.FillTriangle(right, bottom, left, bottom, cx, cy, Colors[PlayerColor.Yellow]); // bottom
            image.FillTriangle(left, bottom, left, top, cx, cy, Colors[PlayerColor.Red]);    // left
            image.StrokeRect(left, top, half * 2, half * 2, Ink);

            // Player labels
            image.DrawText(40, 6, "LUDO LEGENDS - 52-cell ring, 5-cell home column, 57 steps", Ink);

            string pngPath = Path.Combine(outputDir, "board-preview.png");
            Raster.SavePng(image, pngPath);
            File.WriteAllText(Path.Combine(outputDir, "board-preview.svg"), BuildSvg());
            Console.WriteLine("Wrote " + pngPath + " and board-preview.svg");

            // Self-check: every ring cell must be reachable and unique.
            var seen = new HashSet<string>();
            for (int i = 0; i < BoardTopology.TrackCells; i++)
            {
                string key = BoardTopology.TrackCellX(i) + "," + BoardTopology.TrackCellY(i);
                if (!seen.Add(key))
                {
                    Console.Error.WriteLine("Duplicate ring cell: " + key);
                    return 2;
                }
            }

            Console.WriteLine("Ring unique cells: " + seen.Count);
            return 0;
        }

        private static string BuildSvg()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + Size + "\" height=\"" + Size + "\" viewBox=\"0 0 " + Size + " " + Size + "\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#f5efe1\"/>");

            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                BoardTopology.YardOrigin(color, out int ox, out int oy);
                string hex = Hex(Colors[color]);
                sb.AppendLine("<rect x=\"" + (Margin + ox * Cell) + "\" y=\"" + (Margin + oy * Cell) + "\" width=\"" + (6 * Cell) + "\" height=\"" + (6 * Cell) + "\" fill=\"" + hex + "\"/>");
                sb.AppendLine("<rect x=\"" + (Margin + ox * Cell + 12) + "\" y=\"" + (Margin + oy * Cell + 12) + "\" width=\"" + (6 * Cell - 24) + "\" height=\"" + (6 * Cell - 24) + "\" fill=\"#fffaf0\"/>");
            }

            foreach (PlayerColor color in Enum.GetValues(typeof(PlayerColor)))
            {
                for (int i = 0; i < BoardTopology.HomeColumnCells; i++)
                {
                    int x = BoardTopology.HomeColumnCellX(color, i);
                    int y = BoardTopology.HomeColumnCellY(color, i);
                    sb.AppendLine("<rect x=\"" + (Margin + x * Cell) + "\" y=\"" + (Margin + y * Cell) + "\" width=\"" + Cell + "\" height=\"" + Cell + "\" fill=\"" + Hex(Colors[color]) + "\"/>");
                }
            }

            for (int cell = 0; cell < BoardTopology.TrackCells; cell++)
            {
                int x = BoardTopology.TrackCellX(cell);
                int y = BoardTopology.TrackCellY(cell);
                string fill = BoardTopology.IsSafeCell(cell) ? "#ffe9a8" : "#ffffff";
                sb.AppendLine("<rect x=\"" + (Margin + x * Cell) + "\" y=\"" + (Margin + y * Cell) + "\" width=\"" + Cell + "\" height=\"" + Cell + "\" fill=\"" + fill + "\" stroke=\"#c9bfa8\"/>");
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static string Hex((byte R, byte G, byte B) c)
        {
            return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2");
        }
    }

    /// <summary>Tiny dependency-free RGBA raster with a PNG encoder.</summary>
    internal sealed class Raster
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _pixels;

        public Raster(int width, int height, (byte R, byte G, byte B) background)
        {
            _width = width;
            _height = height;
            _pixels = new byte[width * height * 4];
            FillRect(0, 0, width, height, background);
        }

        public void FillRect(int x, int y, int w, int h, (byte R, byte G, byte B) color)
        {
            for (int py = y; py < y + h; py++)
            {
                if (py < 0 || py >= _height)
                {
                    continue;
                }

                for (int px = x; px < x + w; px++)
                {
                    if (px < 0 || px >= _width)
                    {
                        continue;
                    }

                    int index = (py * _width + px) * 4;
                    _pixels[index] = color.R;
                    _pixels[index + 1] = color.G;
                    _pixels[index + 2] = color.B;
                    _pixels[index + 3] = 255;
                }
            }
        }

        public void StrokeRect(int x, int y, int w, int h, (byte R, byte G, byte B) color)
        {
            FillRect(x, y, w, 1, color);
            FillRect(x, y + h - 1, w, 1, color);
            FillRect(x, y, 1, h, color);
            FillRect(x + w - 1, y, 1, h, color);
        }

        public void FillCircle(int cx, int cy, int radius, (byte R, byte G, byte B) color)
        {
            int r2 = radius * radius;
            for (int py = cy - radius; py <= cy + radius; py++)
            {
                for (int px = cx - radius; px <= cx + radius; px++)
                {
                    int dx = px - cx;
                    int dy = py - cy;
                    if (dx * dx + dy * dy <= r2)
                    {
                        FillRect(px, py, 1, 1, color);
                    }
                }
            }
        }

        public void FillTriangle(int x1, int y1, int x2, int y2, int x3, int y3, (byte R, byte G, byte B) color)
        {
            int minX = Math.Min(x1, Math.Min(x2, x3));
            int maxX = Math.Max(x1, Math.Max(x2, x3));
            int minY = Math.Min(y1, Math.Min(y2, y3));
            int maxY = Math.Max(y1, Math.Max(y2, y3));

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    if (InTriangle(px, py, x1, y1, x2, y2, x3, y3))
                    {
                        FillRect(px, py, 1, 1, color);
                    }
                }
            }
        }

        /// <summary>Five-by-three bitmap font, enough for a caption.</summary>
        public void DrawText(int x, int y, string text, (byte R, byte G, byte B) color)
        {
            int cursor = x;
            foreach (char raw in text.ToUpperInvariant())
            {
                if (raw == ' ')
                {
                    cursor += 4;
                    continue;
                }

                int[] glyph = Glyphs(raw);
                for (int row = 0; row < 5; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        if ((glyph[row] & (1 << (2 - col))) != 0)
                        {
                            FillRect(cursor + col * 2, y + row * 2, 2, 2, color);
                        }
                    }
                }

                cursor += 8;
            }
        }

        private static int[] Glyphs(char c)
        {
            switch (c)
            {
                case 'A': return new[] { 2, 5, 7, 5, 5 };
                case 'B': return new[] { 6, 5, 6, 5, 6 };
                case 'C': return new[] { 3, 4, 4, 4, 3 };
                case 'D': return new[] { 6, 5, 5, 5, 6 };
                case 'E': return new[] { 7, 4, 6, 4, 7 };
                case 'G': return new[] { 3, 4, 5, 5, 3 };
                case 'H': return new[] { 5, 5, 7, 5, 5 };
                case 'I': return new[] { 7, 2, 2, 2, 7 };
                case 'L': return new[] { 4, 4, 4, 4, 7 };
                case 'M': return new[] { 5, 7, 7, 5, 5 };
                case 'N': return new[] { 5, 7, 7, 7, 5 };
                case 'O': return new[] { 2, 5, 5, 5, 2 };
                case 'P': return new[] { 6, 5, 6, 4, 4 };
                case 'R': return new[] { 6, 5, 6, 5, 5 };
                case 'S': return new[] { 3, 4, 2, 1, 6 };
                case 'T': return new[] { 7, 2, 2, 2, 2 };
                case 'U': return new[] { 5, 5, 5, 5, 7 };
                case 'V': return new[] { 5, 5, 5, 5, 2 };
                case 'W': return new[] { 5, 5, 7, 7, 5 };
                case 'Y': return new[] { 5, 5, 2, 2, 2 };
                case '2': return new[] { 6, 1, 2, 4, 7 };
                case '5': return new[] { 7, 4, 6, 1, 6 };
                case '7': return new[] { 7, 1, 2, 2, 2 };
                case '-': return new[] { 0, 0, 7, 0, 0 };
                case ',': return new[] { 0, 0, 0, 2, 4 };
                default: return new[] { 7, 5, 5, 5, 7 };
            }
        }

        private static bool InTriangle(int px, int py, int x1, int y1, int x2, int y2, int x3, int y3)
        {
            int d1 = Sign(px, py, x1, y1, x2, y2);
            int d2 = Sign(px, py, x2, y2, x3, y3);
            int d3 = Sign(px, py, x3, y3, x1, y1);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        private static int Sign(int x1, int y1, int x2, int y2, int x3, int y3)
        {
            return (x1 - x3) * (y2 - y3) - (x2 - x3) * (y1 - y3);
        }

        public static void SavePng(Raster raster, string path)
        {
            using (FileStream stream = File.Create(path))
            {
                stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

                var ihdr = new List<byte>();
                ihdr.AddRange(BigEndian(raster._width));
                ihdr.AddRange(BigEndian(raster._height));
                ihdr.Add(8); // bit depth
                ihdr.Add(6); // colour type RGBA
                ihdr.Add(0);
                ihdr.Add(0);
                ihdr.Add(0);
                WriteChunk(stream, "IHDR", ihdr.ToArray());

                var raw = new byte[(raster._width * 4 + 1) * raster._height];
                int offset = 0;
                for (int y = 0; y < raster._height; y++)
                {
                    raw[offset++] = 0; // filter: none
                    Buffer.BlockCopy(raster._pixels, y * raster._width * 4, raw, offset, raster._width * 4);
                    offset += raster._width * 4;
                }

                byte[] compressed;
                using (var memory = new MemoryStream())
                {
                    // PNG requires a zlib-wrapped deflate stream (RFC 1950), which is what
                    // ZLibStream produces. DeflateStream writes raw deflate and yields a
                    // PNG that decoders reject.
                    using (var deflate = new ZLibStream(memory, CompressionLevel.Optimal, true))
                    {
                        deflate.Write(raw, 0, raw.Length);
                    }

                    compressed = memory.ToArray();
                }

                WriteChunk(stream, "IDAT", compressed);
                WriteChunk(stream, "IEND", Array.Empty<byte>());
            }
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            stream.Write(BigEndian(data.Length), 0, 4);
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes, 0, 4);
            stream.Write(data, 0, data.Length);

            uint crc = Crc32.Compute(typeBytes, data);
            stream.Write(BigEndian((int)crc), 0, 4);
        }

        private static byte[] BigEndian(int value)
        {
            return new[]
            {
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }
    }

    internal static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[i] = c;
            }

            return table;
        }

        public static uint Compute(byte[] a, byte[] b)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < a.Length; i++)
            {
                crc = Table[(crc ^ a[i]) & 0xFF] ^ (crc >> 8);
            }

            for (int i = 0; i < b.Length; i++)
            {
                crc = Table[(crc ^ b[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
