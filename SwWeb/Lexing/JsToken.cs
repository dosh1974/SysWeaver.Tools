using System;

namespace SwWeb.Lexing
{
    /// <summary>
    /// The kind of a JavaScript token.
    /// </summary>
    public enum JsTokenKind
    {
        /// <summary>
        /// Spaces, tabs and line terminators (see <see cref="JsToken.HasNewline"/>).
        /// </summary>
        Whitespace,
        /// <summary>
        /// A <c>// ...</c> comment (also a <c>#!</c> hashbang on the first line), not including the line terminator.
        /// </summary>
        LineComment,
        /// <summary>
        /// A <c>/* ... */</c> comment.
        /// </summary>
        BlockComment,
        /// <summary>
        /// A HTML-like comment: <c>&lt;!--</c> anywhere or <c>--&gt;</c> at the start of a line, up to the end of the line (Annex B, classic scripts).
        /// </summary>
        HtmlComment,
        /// <summary>
        /// An identifier or keyword (including unicode escapes).
        /// </summary>
        Identifier,
        /// <summary>
        /// A private name, ex: <c>#field</c>.
        /// </summary>
        PrivateName,
        /// <summary>
        /// A numeric literal.
        /// </summary>
        Number,
        /// <summary>
        /// A <c>'</c> or <c>"</c> string literal (including the quotes).
        /// </summary>
        String,
        /// <summary>
        /// A template literal without substitutions: <c>`abc`</c>.
        /// </summary>
        Template,
        /// <summary>
        /// The start of a template with substitutions: <c>`abc${</c>.
        /// </summary>
        TemplateHead,
        /// <summary>
        /// The text between two substitutions: <c>}abc${</c>.
        /// </summary>
        TemplateMiddle,
        /// <summary>
        /// The end of a template with substitutions: <c>}abc`</c>.
        /// </summary>
        TemplateTail,
        /// <summary>
        /// A regular expression literal including flags: <c>/a[/]b/gi</c>.
        /// </summary>
        Regex,
        /// <summary>
        /// An operator or punctuator.
        /// </summary>
        Punctuator,
        /// <summary>
        /// A character that can't start any token.
        /// </summary>
        Invalid,
    }

    /// <summary>
    /// A JavaScript token (a span in the source text).
    /// </summary>
    public readonly struct JsToken
    {
        /// <summary>
        /// Create a token.
        /// </summary>
        public JsToken(JsTokenKind kind, int start, int length, bool hasNewline, bool unterminated)
        {
            Kind = kind;
            Start = start;
            Length = length;
            HasNewline = hasNewline;
            Unterminated = unterminated;
        }

        /// <summary>
        /// The token kind.
        /// </summary>
        public readonly JsTokenKind Kind;

        /// <summary>
        /// Offset of the first char.
        /// </summary>
        public readonly int Start;

        /// <summary>
        /// The number of chars.
        /// </summary>
        public readonly int Length;

        /// <summary>
        /// The offset after the last char.
        /// </summary>
        public int End => Start + Length;

        /// <summary>
        /// True if the token contains a line terminator (whitespace, block comments, templates).
        /// </summary>
        public readonly bool HasNewline;

        /// <summary>
        /// True if a string, template, regex or block comment isn't terminated (an error).
        /// </summary>
        public readonly bool Unterminated;

        /// <summary>
        /// True for tokens that aren't whitespace or comments.
        /// </summary>
        public bool IsSignificant => Kind > JsTokenKind.HtmlComment;

        /// <summary>
        /// True for comments.
        /// </summary>
        public bool IsComment => (Kind == JsTokenKind.LineComment) || (Kind == JsTokenKind.BlockComment) || (Kind == JsTokenKind.HtmlComment);

        /// <summary>
        /// True for any part of a template literal.
        /// </summary>
        public bool IsTemplate => (Kind >= JsTokenKind.Template) && (Kind <= JsTokenKind.TemplateTail);

        /// <summary>
        /// Get the token text.
        /// </summary>
        /// <param name="source">The source the token was lexed from</param>
        /// <returns>The text</returns>
        public String GetText(String source) => source.Substring(Start, Length);

        /// <summary>
        /// Test if the token text equals a string.
        /// </summary>
        /// <param name="source">The source the token was lexed from</param>
        /// <param name="text">The text to compare with</param>
        /// <returns>True if equal</returns>
        public bool Is(String source, String text) => (Length == text.Length) && (String.CompareOrdinal(source, Start, text, 0, Length) == 0);

        /// <summary>
        /// A debug string.
        /// </summary>
        public override string ToString() => Kind + "@" + Start + "+" + Length;
    }
}
