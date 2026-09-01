using System.Collections.Generic;

namespace SnakeReturns.Gameplay
{
    /// <summary>
    /// A 5x7 pixel font, stored as data and nothing else.
    /// </summary>
    /// <remarks>
    /// Glyphs are written as seven 5-character strings, '#' lit and '.' unlit, so the letter
    /// shapes are legible in the source and a typo is something you can SEE rather than
    /// something you discover on screen. A packed bitfield would be a few bytes smaller and
    /// completely unreviewable.
    ///
    /// There is no lower case. Space is the implicit blank fallback, which is also what any
    /// unknown character gets — a stray character in a score string should leave a gap, not
    /// take the game down.
    /// </remarks>
    public static class LcdFont
    {
        public const int Width = 5;
        public const int Height = 7;
        public const int Tracking = 1;
        public const int Advance = Width + Tracking;   // 6

        private static readonly string[] Blank =
        {
            ".....", ".....", ".....", ".....", ".....", ".....", "....."
        };

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },

            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
            ['D'] = new[] { "###..", "#..#.", "#...#", "#...#", "#...#", "#..#.", "###.." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['N'] = new[] { "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#" },
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
            ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" },
            ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
            ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },

            ['!'] = new[] { "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.." },
            ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
            ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "..#.." },
            [':'] = new[] { ".....", "..#..", "..#..", ".....", "..#..", "..#..", "....." },
            ['/'] = new[] { "....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...." },
            ['>'] = new[] { "#....", ".#...", "..#..", "...#.", "..#..", ".#...", "#...." },
            ['<'] = new[] { "....#", "...#.", "..#..", ".#...", "..#..", "...#.", "....#" }
        };

        /// <summary>Is the pixel at (col, row) lit? Row 0 is the TOP row.</summary>
        public static bool Pixel(char c, int col, int row)
        {
            if (col < 0 || col >= Width || row < 0 || row >= Height)
            {
                return false;
            }

            char key = char.ToUpperInvariant(c);

            // Anything unknown draws as blank rather than throwing. A bad character should
            // cost a gap in a word, never a run.
            string[] glyph = Glyphs.TryGetValue(key, out string[] found) ? found : Blank;

            return glyph[row][col] == '#';
        }

        /// <summary>Width of a rendered string in font pixels, tracking between glyphs only.</summary>
        public static int MeasureWidth(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }

            return s.Length * Advance - Tracking;
        }

        public static bool Has(char c)
        {
            return Glyphs.ContainsKey(char.ToUpperInvariant(c));
        }
    }
}
