using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SwWeb.Lexing;

namespace SwWeb.Tests
{
    public class JsLexerTests
    {
        static List<(JsTokenKind Kind, String Text)> Sig(String src) =>
            JsLexer.Tokenize(src).Where(t => t.IsSignificant).Select(t => (t.Kind, t.GetText(src))).ToList();

        static List<(JsTokenKind Kind, String Text)> All(String src) =>
            JsLexer.Tokenize(src).Select(t => (t.Kind, t.GetText(src))).ToList();

        static void AssertCovers(String src)
        {
            var tokens = JsLexer.Tokenize(src);
            var sb = new StringBuilder();
            int pos = 0;
            foreach (var t in tokens)
            {
                Assert.Equal(pos, t.Start);
                sb.Append(t.GetText(src));
                pos = t.End;
            }
            Assert.Equal(src, sb.ToString());
        }

        [Fact]
        public void TokensCoverTheWholeSource()
        {
            var src = "\"use strict\";\r\n/** @file x */\r\nconst a = `x${ {b: `y${1}`}.b }z` / 2; // c\r\nif (a) /re[/]/g.test('s\\'') ; <!-- h\r\n--> end\r\nvar r = x++ / 2;\r\n#!not a hashbang here";
            AssertCovers(src);
        }

        [Fact]
        public void DivisionAfterIdentifierOrNumberOrParen()
        {
            var t = Sig("a = b / c / d; e = (f) / 2; g = 3 / 4; h = i[0] / 5;");
            Assert.DoesNotContain(t, x => x.Kind == JsTokenKind.Regex);
            Assert.Equal(5, t.Count(x => x.Text == "/"));
        }

        [Fact]
        public void RegexAfterOperatorsAndKeywords()
        {
            var t = Sig("x = /ab+c/gi.test(s); return /x/; y = typeof /z/; f(/a\\/b/, /[/]+/); z = c => /q/;");
            var re = t.Where(x => x.Kind == JsTokenKind.Regex).Select(x => x.Text).ToArray();
            Assert.Equal(new[] { "/ab+c/gi", "/x/", "/z/", "/a\\/b/", "/[/]+/", "/q/" }, re);
        }

        [Fact]
        public void RegexAfterControlParenButNotAfterCallParen()
        {
            var t = Sig("if (a) /re/.test(b); while (c) /x/g.exec(d); f(a) / 2;");
            Assert.Equal(new[] { "/re/", "/x/g" }, t.Where(x => x.Kind == JsTokenKind.Regex).Select(x => x.Text).ToArray());
            Assert.Contains(t, x => x.Kind == JsTokenKind.Punctuator && x.Text == "/");
        }

        [Fact]
        public void RegexAfterBlockDivisionAfterObjectLiteral()
        {
            var t = Sig("function f() {}\n/foo/.test(x);\nvar o = {a: 1} / 2;");
            Assert.Equal(new[] { "/foo/" }, t.Where(x => x.Kind == JsTokenKind.Regex).Select(x => x.Text).ToArray());
            Assert.Contains(t, x => x.Kind == JsTokenKind.Punctuator && x.Text == "/");
        }

        [Fact]
        public void MemberNamedLikeKeywordIsDivision()
        {
            var t = Sig("a = x.return / 2 / y;");
            Assert.DoesNotContain(t, x => x.Kind == JsTokenKind.Regex);
        }

        [Fact]
        public void PostfixIncrementThenDivision()
        {
            var t = Sig("a = b++ / 2 / c;");
            Assert.DoesNotContain(t, x => x.Kind == JsTokenKind.Regex);
        }

        [Fact]
        public void NestedTemplateLiterals()
        {
            var t = Sig("`a${ `b${c}d` }e`");
            Assert.Equal(new[]
            {
                (JsTokenKind.TemplateHead, "`a${"),
                (JsTokenKind.TemplateHead, "`b${"),
                (JsTokenKind.Identifier, "c"),
                (JsTokenKind.TemplateTail, "}d`"),
                (JsTokenKind.TemplateTail, "}e`"),
            }, t);
        }

        [Fact]
        public void TemplateWithObjectLiteralAndMiddle()
        {
            var t = Sig("`x${ {a: 1}.a }y${z}w` + 'q'");
            Assert.Equal(JsTokenKind.TemplateHead, t[0].Kind);
            Assert.Contains((JsTokenKind.TemplateMiddle, "}y${"), t);
            Assert.Contains((JsTokenKind.TemplateTail, "}w`"), t);
            Assert.Equal((JsTokenKind.String, "'q'"), t[t.Count - 1]);
        }

        [Fact]
        public void TemplateEscapesAndNewlines()
        {
            var src = "`a\\`b\\${c}\r\nd`";
            var t = JsLexer.Tokenize(src);
            Assert.Single(t);
            Assert.Equal(JsTokenKind.Template, t[0].Kind);
            Assert.True(t[0].HasNewline);
            Assert.False(t[0].Unterminated);
        }

        [Fact]
        public void RegexInsideTemplateSubstitution()
        {
            var t = Sig("`${ /}/.source }`");
            Assert.Equal(new[] { "/}/" }, t.Where(x => x.Kind == JsTokenKind.Regex).Select(x => x.Text).ToArray());
            Assert.Equal(JsTokenKind.TemplateTail, t[t.Count - 1].Kind);
        }

        [Fact]
        public void StringsWithEscapesAndContinuation()
        {
            var t = Sig("a = 'it\\'s' + \"a\\\"b\" + 'x\\\r\ny';");
            var s = t.Where(x => x.Kind == JsTokenKind.String).Select(x => x.Text).ToArray();
            Assert.Equal(new[] { "'it\\'s'", "\"a\\\"b\"", "'x\\\r\ny'" }, s);
        }

        [Fact]
        public void UnterminatedTokensAreFlagged()
        {
            var a = JsLexer.Tokenize("a = 'abc\nb = 1;");
            Assert.Contains(a, x => x.Kind == JsTokenKind.String && x.Unterminated);
            Assert.Contains(a, x => x.Kind == JsTokenKind.Identifier && x.Start == 9); // lexing continues on the next line
            Assert.Contains(JsLexer.Tokenize("`abc"), x => x.Unterminated && x.IsTemplate);
            Assert.Contains(JsLexer.Tokenize("/* abc"), x => x.Unterminated && x.Kind == JsTokenKind.BlockComment);
            Assert.Contains(JsLexer.Tokenize("x = /abc\n"), x => x.Unterminated && x.Kind == JsTokenKind.Regex);
        }

        [Fact]
        public void Comments()
        {
            var t = All("a /* b */ // c\r\n/** d\r\n*/ e");
            Assert.Contains((JsTokenKind.BlockComment, "/* b */"), t);
            Assert.Contains((JsTokenKind.LineComment, "// c"), t);
            Assert.Contains((JsTokenKind.BlockComment, "/** d\r\n*/"), t);
            Assert.Equal((JsTokenKind.Identifier, "e"), t[t.Count - 1]);
        }

        [Fact]
        public void HtmlLikeComments()
        {
            var t = All("<!-- hidden\r\nx = 1;\r\n  --> also hidden\r\ny = a-->b;");
            Assert.Contains((JsTokenKind.HtmlComment, "<!-- hidden"), t);
            Assert.Contains((JsTokenKind.HtmlComment, "--> also hidden"), t);
            // Not at the start of a line: a-- > b
            Assert.Contains((JsTokenKind.Punctuator, "--"), t);
            Assert.Contains((JsTokenKind.Punctuator, ">"), t);
        }

        [Fact]
        public void HashbangOnlyAtStart()
        {
            var t = All("#!/usr/bin/env node\nx");
            Assert.Equal((JsTokenKind.LineComment, "#!/usr/bin/env node"), t[0]);
        }

        [Fact]
        public void Numbers()
        {
            var t = Sig("a = 1.5e-3 + .5 + 0x1F + 1_000n + b?.5:1;");
            var n = t.Where(x => x.Kind == JsTokenKind.Number).Select(x => x.Text).ToArray();
            Assert.Equal(new[] { "1.5e-3", ".5", "0x1F", "1_000n", ".5", "1" }, n);
            Assert.Contains((JsTokenKind.Punctuator, "?"), t);
        }

        [Fact]
        public void OptionalChainingAndPrivateNames()
        {
            var t = Sig("class A { #x = 1; f() { return this.#x?.y ?? 2; } }");
            Assert.Contains((JsTokenKind.PrivateName, "#x"), t);
            Assert.Contains((JsTokenKind.Punctuator, "?."), t);
            Assert.Contains((JsTokenKind.Punctuator, "??"), t);
        }

        [Fact]
        public void UnicodeIdentifiersAndLineSeparators()
        {
            var ls = ((char)0x2028).ToString();
            var src = "var åäö = 1;" + ls + "x\\u0041 = 2;";
            AssertCovers(src);
            var t = Sig(src);
            Assert.Contains((JsTokenKind.Identifier, "åäö"), t);
            Assert.Contains((JsTokenKind.Identifier, "x\\u0041"), t);
        }

        [Fact]
        public void TokenizeRange()
        {
            var src = "<script>a = /x/;</script>";
            var t = JsLexer.Tokenize(src, 8, 16);
            Assert.Equal(8, t[0].Start);
            Assert.Equal(16, t[t.Count - 1].End);
            Assert.Contains(t, x => x.Kind == JsTokenKind.Regex && x.GetText(src) == "/x/");
        }

        [Theory]
        [InlineData("'a\\nb'", "a\nb")]
        [InlineData("\"it\\'s\"", "it's")]
        [InlineData("'\\x41\\u0042\\u{43}'", "ABC")]
        [InlineData("'a\\\r\nb'", "ab")]
        [InlineData("'\\101'", "A")]
        [InlineData("'\\0'", "\0")]
        public void DecodeStringLiteral(String raw, String expected)
        {
            Assert.Equal(expected, JsLexer.DecodeStringLiteral(raw));
        }
    }
}
