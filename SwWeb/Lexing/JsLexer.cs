using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SwWeb.Lexing
{
    /// <summary>
    /// A robust, error tolerant JavaScript tokenizer (classic script goal).
    /// Handles strings, template literals with nested substitutions, regex literals vs division (context heuristics),
    /// line / block comments and HTML-like comments (<c>&lt;!--</c>, <c>--&gt;</c> at line start).
    /// Every char of the input is covered by exactly one token, so the tokens can be concatenated to rebuild the source.
    /// </summary>
    public static class JsLexer
    {
        /// <summary>
        /// Tokenize JavaScript code.
        /// </summary>
        /// <param name="source">The source text</param>
        /// <param name="start">The offset to start at (ex: the start of a script element body)</param>
        /// <param name="end">The offset to stop at (exclusive), -1 = end of text</param>
        /// <returns>All tokens (including whitespace and comments)</returns>
        public static List<JsToken> Tokenize(String source, int start = 0, int end = -1)
        {
            if (end < 0)
                end = source.Length;
            var l = new Lexer(source, start, end);
            l.Run();
            return l.Tokens;
        }

        /// <summary>
        /// Keywords after which a <c>/</c> starts a regular expression (and a <c>{</c> starts an expression).
        /// </summary>
        static readonly HashSet<String> ExpressionKeywords = new HashSet<String>(StringComparer.Ordinal)
        {
            "return", "typeof", "instanceof", "in", "of", "new", "delete", "void", "throw", "case", "do", "else", "yield", "await", "extends",
        };

        /// <summary>
        /// Keywords that are followed by a parenthesized head (after its closing paren a <c>/</c> starts a regex).
        /// </summary>
        static readonly HashSet<String> ControlKeywords = new HashSet<String>(StringComparer.Ordinal)
        {
            "if", "while", "for", "with",
        };

        /// <summary>
        /// Keywords followed by a block statement.
        /// </summary>
        static readonly HashSet<String> BlockKeywords = new HashSet<String>(StringComparer.Ordinal)
        {
            "else", "do", "try", "finally",
        };

        static readonly String[] Punctuators =
        {
            ">>>=",
            "...", "===", "!==", "**=", "<<=", ">>=", ">>>", "&&=", "||=", "??=",
            "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?.", "++", "--", "+=", "-=", "*=", "%=", "&=", "|=", "^=", "**", "<<", ">>",
            "{", "}", "(", ")", "[", "]", ";", ",", "<", ">", "+", "-", "*", "%", "&", "|", "^", "!", "~", "?", ":", "=", ".", "@",
        };

        /// <summary>
        /// Test if a char is a JavaScript line terminator.
        /// </summary>
        public static bool IsLineTerminator(char c) => (c == '\n') || (c == '\r') || (c == '\u2028') || (c == '\u2029');

        /// <summary>
        /// Test if a char is JavaScript white space (not including line terminators).
        /// </summary>
        public static bool IsWhitespace(char c) => (c == ' ') || (c == '\t') || (c == '\v') || (c == '\f') || (c == '\u00a0') || (c == '\ufeff') || ((c > 127) && (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator));

        /// <summary>
        /// Test if a char can start an identifier.
        /// </summary>
        public static bool IsIdentifierStart(char c)
        {
            if ((c >= 'a') && (c <= 'z'))
                return true;
            if ((c >= 'A') && (c <= 'Z'))
                return true;
            if ((c == '$') || (c == '_') || (c == '\\'))
                return true;
            if (c < 128)
                return false;
            if (Char.IsSurrogate(c))
                return true;
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return (cat <= UnicodeCategory.OtherLetter) || (cat == UnicodeCategory.LetterNumber);
        }

        /// <summary>
        /// Test if a char can be a part of an identifier.
        /// </summary>
        public static bool IsIdentifierPart(char c)
        {
            if (IsIdentifierStart(c))
                return true;
            if ((c >= '0') && (c <= '9'))
                return true;
            if (c < 128)
                return false;
            if ((c == '\u200c') || (c == '\u200d'))
                return true;
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return (cat == UnicodeCategory.NonSpacingMark) || (cat == UnicodeCategory.SpacingCombiningMark) || (cat == UnicodeCategory.DecimalDigitNumber) || (cat == UnicodeCategory.ConnectorPunctuation);
        }

        /// <summary>
        /// Decode the value of a string literal token (<c>'...'</c> or <c>"..."</c>), or the raw text of a template part.
        /// Unknown escapes keep the escaped char (as JavaScript does).
        /// </summary>
        /// <param name="raw">The literal including the quotes</param>
        /// <returns>The value</returns>
        public static String DecodeStringLiteral(String raw)
        {
            if (raw.Length < 2)
                return "";
            var q = raw[0];
            int end = raw.Length;
            if (raw[end - 1] == q)
                --end;
            var sb = new StringBuilder(end);
            for (int i = 1; i < end; ++i)
            {
                var c = raw[i];
                if ((c != '\\') || (i + 1 >= end))
                {
                    sb.Append(c);
                    continue;
                }
                c = raw[++i];
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'v': sb.Append('\v'); break;
                    case '\r':
                        if ((i + 1 < end) && (raw[i + 1] == '\n'))
                            ++i;
                        break;
                    case '\n':
                    case '\u2028':
                    case '\u2029':
                        break;
                    case 'x':
                        if ((i + 2 < end) && int.TryParse(raw.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hx))
                        {
                            sb.Append((char)hx);
                            i += 2;
                        }
                        else
                            sb.Append(c);
                        break;
                    case 'u':
                        if ((i + 1 < end) && (raw[i + 1] == '{'))
                        {
                            var ce = raw.IndexOf('}', i + 2);
                            if ((ce > 0) && (ce < end) && int.TryParse(raw.AsSpan(i + 2, ce - i - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp) && (cp <= 0x10ffff))
                            {
                                sb.Append(Char.ConvertFromUtf32(cp));
                                i = ce;
                            }
                            else
                                sb.Append(c);
                        }
                        else if ((i + 4 < end) && int.TryParse(raw.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u))
                        {
                            sb.Append((char)u);
                            i += 4;
                        }
                        else
                            sb.Append(c);
                        break;
                    case '0':
                        if ((i + 1 >= end) || (raw[i + 1] < '0') || (raw[i + 1] > '9'))
                        {
                            sb.Append('\0');
                            break;
                        }
                        goto default;
                    default:
                        if ((c >= '0') && (c <= '7'))
                        {
                            int v = c - '0';
                            int n = 1;
                            while ((n < 3) && (i + 1 < end) && (raw[i + 1] >= '0') && (raw[i + 1] <= '7') && (v * 8 + (raw[i + 1] - '0') <= 255))
                            {
                                v = v * 8 + (raw[++i] - '0');
                                ++n;
                            }
                            sb.Append((char)v);
                        }
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }


        enum Brace : byte
        {
            Block,
            Expression,
            Template,
        }

        sealed class Lexer
        {
            public Lexer(String src, int start, int end)
            {
                S = src;
                Pos = start;

                End = end;
            }

            readonly String S;
            readonly int End;

            int Pos;

            public readonly List<JsToken> Tokens = new List<JsToken>();

            readonly Stack<Brace> Braces = new Stack<Brace>();
            readonly Stack<bool> Parens = new Stack<bool>();

            /// <summary>Index of the last significant token (-1 = none)</summary>
            int LastSig = -1;
            /// <summary>Index of the significant token before LastSig (-1 = none)</summary>
            int PrevSig = -1;
            /// <summary>Set when the last significant token is a ')' or '}' after which a regex may start</summary>
            bool LastCloseAllowsRegex;
            /// <summary>Only whitespace and comments since the last line terminator (or the start)</summary>
            bool AtLineStart = true;

            char C(int o = 0)
            {
                var p = Pos + o;
                return p < End ? S[p] : '\0';
            }

            void Add(JsTokenKind kind, int start, bool newline = false, bool unterminated = false, bool closeAllowsRegex = false)
            {
                var t = new JsToken(kind, start, Pos - start, newline, unterminated);
                Tokens.Add(t);
                if (t.IsSignificant)
                {
                    PrevSig = LastSig;
                    LastSig = Tokens.Count - 1;
                    LastCloseAllowsRegex = closeAllowsRegex;
                    AtLineStart = false;
                }
                else if (newline)
                    AtLineStart = true;
            }

            bool LastIs(String text) => (LastSig >= 0) && (Tokens[LastSig].Kind == JsTokenKind.Punctuator) && Tokens[LastSig].Is(S, text);

            bool IsMemberName(int tokenIndex)
            {
                if (tokenIndex != LastSig)
                    return false;
                if (PrevSig < 0)
                    return false;
                var p = Tokens[PrevSig];
                return (p.Kind == JsTokenKind.Punctuator) && (p.Is(S, ".") || p.Is(S, "?."));
            }

            String LastKeyword()
            {
                if (LastSig < 0)
                    return null;
                var t = Tokens[LastSig];
                if (t.Kind != JsTokenKind.Identifier)
                    return null;
                if (IsMemberName(LastSig))
                    return null;
                return t.GetText(S);
            }

            bool RegexAllowed()
            {
                if (LastSig < 0)
                    return true;
                var t = Tokens[LastSig];
                switch (t.Kind)
                {
                    case JsTokenKind.TemplateHead:
                    case JsTokenKind.TemplateMiddle:
                        return true;
                    case JsTokenKind.Identifier:
                        {
                            var k = LastKeyword();
                            return (k != null) && ExpressionKeywords.Contains(k);
                        }
                    case JsTokenKind.Punctuator:
                        if (t.Is(S, ")") || t.Is(S, "}"))
                            return LastCloseAllowsRegex;
                        if (t.Is(S, "]") || t.Is(S, "++") || t.Is(S, "--"))
                            return false;
                        return true;
                    default:
                        return false;
                }
            }

            Brace ClassifyOpenBrace()
            {
                if (LastSig < 0)
                    return Brace.Block;
                var t = Tokens[LastSig];
                switch (t.Kind)
                {
                    case JsTokenKind.TemplateHead:
                    case JsTokenKind.TemplateMiddle:
                        return Brace.Expression;
                    case JsTokenKind.Identifier:
                        {
                            var k = LastKeyword();
                            if (k == null)
                                return Brace.Block;
                            if (BlockKeywords.Contains(k))
                                return Brace.Block;
                            return ExpressionKeywords.Contains(k) ? Brace.Expression : Brace.Block;
                        }
                    case JsTokenKind.Punctuator:
                        if (t.Is(S, ")") || t.Is(S, ";") || t.Is(S, "{") || t.Is(S, "}") || t.Is(S, "=>"))
                            return Brace.Block;
                        if (t.Is(S, ":"))
                            return (Braces.Count > 0) && (Braces.Peek() == Brace.Expression) ? Brace.Expression : Brace.Block;
                        return Brace.Expression;
                    default:
                        return Brace.Block;
                }
            }

            public void Run()
            {
                if ((Pos + 1 < End) && (S[Pos] == '#') && (S[Pos + 1] == '!') && (Pos == 0))
                {
                    var s = Pos;
                    while ((Pos < End) && !IsLineTerminator(S[Pos]))
                        ++Pos;
                    Add(JsTokenKind.LineComment, s);
                }
                while (Pos < End)
                    Next();
            }

            void Next()
            {
                var s = Pos;
                var c = S[Pos];
                // Whitespace
                if (IsWhitespace(c) || IsLineTerminator(c))
                {
                    bool nl = false;
                    while (Pos < End)
                    {
                        c = S[Pos];
                        if (IsLineTerminator(c))
                            nl = true;
                        else if (!IsWhitespace(c))
                            break;
                        ++Pos;
                    }
                    Add(JsTokenKind.Whitespace, s, nl);
                    return;
                }
                // Comments
                if (c == '/')
                {
                    var n = C(1);
                    if (n == '/')
                    {
                        LineRest();
                        Add(JsTokenKind.LineComment, s);
                        return;
                    }
                    if (n == '*')
                    {
                        Pos += 2;
                        bool nl = false;
                        bool closed = false;
                        while (Pos < End)
                        {
                            c = S[Pos];
                            if ((c == '*') && (C(1) == '/'))
                            {
                                Pos += 2;
                                closed = true;
                                break;
                            }
                            if (IsLineTerminator(c))
                                nl = true;
                            ++Pos;
                        }
                        Add(JsTokenKind.BlockComment, s, nl, !closed);
                        return;
                    }
                    if (RegexAllowed())
                    {
                        RegexLiteral(s);
                        return;
                    }
                }
                if ((c == '<') && (C(1) == '!') && (C(2) == '-') && (C(3) == '-'))
                {
                    LineRest();
                    Add(JsTokenKind.HtmlComment, s);
                    return;
                }
                if (AtLineStart && (c == '-') && (C(1) == '-') && (C(2) == '>'))
                {
                    LineRest();
                    Add(JsTokenKind.HtmlComment, s);
                    return;
                }
                // Strings
                if ((c == '"') || (c == '\''))
                {
                    StringLiteral(s, c);
                    return;
                }
                if (c == '`')
                {
                    ++Pos;
                    TemplatePart(s, true);
                    return;
                }
                // Numbers
                if (((c >= '0') && (c <= '9')) || ((c == '.') && (C(1) >= '0') && (C(1) <= '9')))
                {
                    NumberLiteral();
                    Add(JsTokenKind.Number, s);
                    return;
                }
                // Identifiers
                if (IsIdentifierStart(c))
                {
                    IdentifierRest();
                    Add(JsTokenKind.Identifier, s);
                    return;
                }
                if ((c == '#') && IsIdentifierStart(C(1)))
                {
                    ++Pos;
                    IdentifierRest();
                    Add(JsTokenKind.PrivateName, s);
                    return;
                }
                // Braces / parens
                switch (c)
                {
                    case '{':
                        Braces.Push(ClassifyOpenBrace());
                        ++Pos;
                        Add(JsTokenKind.Punctuator, s);
                        return;
                    case '}':
                        {
                            var b = Braces.Count > 0 ? Braces.Pop() : Brace.Block;
                            ++Pos;
                            if (b == Brace.Template)
                            {
                                TemplatePart(s, false);
                                return;
                            }
                            Add(JsTokenKind.Punctuator, s, false, false, b == Brace.Block);
                            return;
                        }
                    case '(':
                        {
                            var k = LastKeyword();
                            Parens.Push((k != null) && ControlKeywords.Contains(k));
                            ++Pos;
                            Add(JsTokenKind.Punctuator, s);
                            return;
                        }
                    case ')':
                        {
                            var ctrl = Parens.Count > 0 && Parens.Pop();
                            ++Pos;
                            Add(JsTokenKind.Punctuator, s, false, false, ctrl);
                            return;
                        }
                }
                // Other punctuators
                foreach (var p in Punctuators)
                {
                    var pl = p.Length;
                    if ((Pos + pl <= End) && (String.CompareOrdinal(S, Pos, p, 0, pl) == 0))
                    {
                        if ((p == "?.") && (C(2) >= '0') && (C(2) <= '9'))
                            continue;
                        Pos += pl;
                        Add(JsTokenKind.Punctuator, s);
                        return;
                    }
                }
                if (c == '/')
                {
                    // Division (regex not allowed here)
                    Pos += (C(1) == '=') ? 2 : 1;
                    Add(JsTokenKind.Punctuator, s);
                    return;
                }
                // Invalid char (keep surrogate pairs together)
                Pos += (Char.IsHighSurrogate(c) && Char.IsLowSurrogate(C(1))) ? 2 : 1;
                Add(JsTokenKind.Invalid, s);
            }

            void LineRest()
            {
                while ((Pos < End) && !IsLineTerminator(S[Pos]))
                    ++Pos;
            }

            void IdentifierRest()
            {
                while (Pos < End)
                {
                    var c = S[Pos];
                    if (c == '\\')
                    {
                        // \uXXXX or \u{X..}
                        if (C(1) == 'u')
                        {
                            if (C(2) == '{')
                            {
                                var e = S.IndexOf('}', Pos + 3);
                                Pos = (e < 0) || (e >= End) ? Pos + 2 : e + 1;
                            }
                            else
                                Pos = Math.Min(End, Pos + 6);
                            continue;
                        }
                        break;
                    }
                    if (!IsIdentifierPart(c))
                        break;
                    ++Pos;
                }
            }

            void NumberLiteral()
            {
                bool hex = (S[Pos] == '0') && ((C(1) | 0x20) is 'x' or 'o' or 'b');
                if (hex)
                    Pos += 2;
                bool dot = false;
                while (Pos < End)
                {
                    var c = S[Pos];
                    if (((c >= '0') && (c <= '9')) || (c == '_'))
                    {
                        ++Pos;
                        continue;
                    }
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    {
                        ++Pos;
                        if (!hex && ((c | 0x20) == 'e') && ((C() == '+') || (C() == '-')))
                            ++Pos;
                        continue;
                    }
                    if ((c == '.') && !dot && !hex)
                    {
                        dot = true;
                        ++Pos;
                        continue;
                    }
                    break;
                }
            }

            void StringLiteral(int s, char q)
            {
                ++Pos;
                bool closed = false;
                while (Pos < End)
                {
                    var c = S[Pos];
                    if (c == q)
                    {
                        ++Pos;
                        closed = true;
                        break;
                    }
                    if (c == '\\')
                    {
                        ++Pos;
                        if (Pos < End)
                        {
                            if ((S[Pos] == '\r') && (C(1) == '\n'))
                                ++Pos;
                            ++Pos;
                        }
                        continue;
                    }
                    if ((c == '\n') || (c == '\r'))
                        break;
                    ++Pos;
                }
                Add(JsTokenKind.String, s, false, !closed);
            }

            /// <summary>
            /// Lex a template part, Pos is after the opening ` or }.
            /// </summary>
            void TemplatePart(int s, bool head)
            {
                bool nl = false;
                while (Pos < End)
                {
                    var c = S[Pos];
                    if (c == '`')
                    {
                        ++Pos;
                        Add(head ? JsTokenKind.Template : JsTokenKind.TemplateTail, s, nl);
                        return;
                    }
                    if ((c == '$') && (C(1) == '{'))
                    {
                        Pos += 2;
                        Braces.Push(Brace.Template);
                        Add(head ? JsTokenKind.TemplateHead : JsTokenKind.TemplateMiddle, s, nl);
                        return;
                    }
                    if (c == '\\')
                    {
                        Pos += 2;
                        if (Pos > End)
                            Pos = End;
                        continue;
                    }
                    if (IsLineTerminator(c))
                        nl = true;
                    ++Pos;
                }
                Add(head ? JsTokenKind.Template : JsTokenKind.TemplateTail, s, nl, true);
            }

            void RegexLiteral(int s)
            {
                ++Pos;
                bool inClass = false;
                bool closed = false;
                while (Pos < End)
                {
                    var c = S[Pos];
                    if (IsLineTerminator(c))
                        break;
                    if (c == '\\')
                    {
                        Pos += 2;
                        if (Pos > End)
                            Pos = End;
                        continue;
                    }
                    ++Pos;
                    if (inClass)
                    {
                        if (c == ']')
                            inClass = false;
                        continue;
                    }
                    if (c == '[')
                    {
                        inClass = true;
                        continue;
                    }
                    if (c == '/')
                    {
                        closed = true;
                        break;
                    }
                }
                if (closed)
                    while ((Pos < End) && IsIdentifierPart(S[Pos]))
                        ++Pos;
                Add(JsTokenKind.Regex, s, false, !closed);
            }
        }
    }
}
