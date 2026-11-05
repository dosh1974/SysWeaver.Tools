using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SwWeb.Lexing
{
    /// <summary>
    /// A decoded text file with an offset to line / column map.
    /// Offsets are UTF-16 indices into <see cref="Text"/> (the BOM is not part of the text).
    /// </summary>
    public sealed class SourceText
    {
        /// <summary>
        /// Create a source text from a string.
        /// </summary>
        /// <param name="text">The text (without a BOM)</param>
        /// <param name="path">An optional path (used in messages)</param>
        /// <param name="hadBom">True if the file started with a UTF-8 BOM</param>
        public SourceText(String text, String path = null, bool hadBom = false)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Path = path;
            HadBom = hadBom;
            var ls = new List<int> { 0 };
            var t = Text;
            var l = t.Length;
            for (int i = 0; i < l; ++i)
            {
                var c = t[i];
                if (c == '\n')
                    ls.Add(i + 1);
                else if (c == '\r')
                {
                    if ((i + 1 < l) && (t[i + 1] == '\n'))
                        ++i;
                    ls.Add(i + 1);
                }
                else if ((c == '\u2028') || (c == '\u2029'))
                    ls.Add(i + 1);
            }
            LineStarts = ls.ToArray();
        }

        /// <summary>
        /// The text (without BOM).
        /// </summary>
        public readonly String Text;

        /// <summary>
        /// The path (may be null).
        /// </summary>
        public readonly String Path;

        /// <summary>
        /// True if the file started with a UTF-8 BOM.
        /// </summary>
        public readonly bool HadBom;

        /// <summary>
        /// True if the bytes were not valid UTF-8 (decoded leniently).
        /// </summary>
        public bool InvalidUtf8 { get; private set; }

        /// <summary>
        /// Offsets of the start of every line.
        /// </summary>
        public readonly int[] LineStarts;

        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// Decode UTF-8 bytes (a leading BOM is removed).
        /// </summary>
        /// <param name="data">The bytes</param>
        /// <param name="path">An optional path</param>
        /// <returns>The source text</returns>
        public static SourceText FromBytes(ReadOnlySpan<byte> data, String path = null)
        {
            bool bom = (data.Length >= 3) && (data[0] == 0xef) && (data[1] == 0xbb) && (data[2] == 0xbf);
            if (bom)
                data = data.Slice(3);
            String text;
            bool invalid = false;
            try
            {
                text = StrictUtf8.GetString(data);
            }
            catch (DecoderFallbackException)
            {
                text = Encoding.UTF8.GetString(data);
                invalid = true;
            }
            return new SourceText(text, path, bom) { InvalidUtf8 = invalid };
        }

        /// <summary>
        /// Read and decode a file.
        /// </summary>
        /// <param name="path">The file name</param>
        /// <returns>The source text</returns>
        public static SourceText FromFile(String path) => FromBytes(File.ReadAllBytes(path), path);

        /// <summary>
        /// Get the 1-based line and column of an offset.
        /// </summary>
        /// <param name="offset">The offset</param>
        /// <returns>The line and column (1-based)</returns>
        public (int Line, int Col) GetLineCol(int offset)
        {
            var ls = LineStarts;
            var i = Array.BinarySearch(ls, offset);
            if (i < 0)
                i = ~i - 1;
            if (i < 0)
                i = 0;
            return (i + 1, offset - ls[i] + 1);
        }
    }
}
