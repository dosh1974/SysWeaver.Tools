using System;
using System.Linq;
using SwWeb.Lexing;

namespace SwWeb.Tests
{
    public class HtmlScannerTests
    {
        static void AssertContiguous(String src)
        {
            var nodes = HtmlScanner.Scan(src);
            int pos = 0;
            foreach (var n in nodes)
            {
                Assert.Equal(pos, n.Start);
                Assert.True(n.End > n.Start);
                pos = n.End;
            }
            Assert.Equal(src.Length, pos);
        }

        [Fact]
        public void Attributes()
        {
            var src = "<a href=x.html title='say \"hi\"' disabled data-x = \"1 2\" =odd>t</a>";
            var n = HtmlScanner.Scan(src);
            var a = n[0];
            Assert.Equal(HtmlNodeKind.StartTag, a.Kind);
            Assert.Equal("a", a.Name);
            Assert.Equal(new[] { "href", "title", "disabled", "data-x", "=odd" }, a.Attributes.Select(x => x.Name).ToArray());
            Assert.Equal("x.html", a.GetAttribute("HREF").Value);
            Assert.Equal('\0', a.GetAttribute("href").Quote);
            Assert.Equal("say \"hi\"", a.GetAttribute("title").Value);
            Assert.Equal('\'', a.GetAttribute("title").Quote);
            Assert.Null(a.GetAttribute("disabled").Value);
            Assert.Equal("1 2", a.GetAttribute("data-x").Value);
            var t = a.GetAttribute("title");
            Assert.Equal(t.Value, src.Substring(t.ValueStart, t.Value.Length));
            Assert.Equal(HtmlNodeKind.Text, n[1].Kind);
            Assert.Equal(HtmlNodeKind.EndTag, n[2].Kind);
            AssertContiguous(src);
        }

        [Fact]
        public void ScriptIsRawText()
        {
            var src = "<script>if (a < b) x = '</div>' + \"</scr\" + \"ipt>\";</SCRIPT >after";
            var n = HtmlScanner.Scan(src);
            Assert.Equal(HtmlNodeKind.StartTag, n[0].Kind);
            Assert.Equal(HtmlNodeKind.RawText, n[1].Kind);
            Assert.Equal("if (a < b) x = '</div>' + \"</scr\" + \"ipt>\";", src.Substring(n[1].Start, n[1].End - n[1].Start));
            Assert.Equal(HtmlNodeKind.EndTag, n[2].Kind);
            Assert.Equal("script", n[2].Name);
            Assert.Equal(HtmlNodeKind.Text, n[3].Kind);
            AssertContiguous(src);
        }

        [Fact]
        public void CommentsHideTags()
        {
            var src = "<!DOCTYPE html><!-- <script> --><p>x</p><!--><?xml x?>";
            var n = HtmlScanner.Scan(src);
            Assert.Equal(HtmlNodeKind.Declaration, n[0].Kind);
            Assert.Equal(HtmlNodeKind.Comment, n[1].Kind);
            Assert.Equal("<!-- <script> -->", src.Substring(n[1].Start, n[1].End - n[1].Start));
            Assert.Equal("p", n[2].Name);
            Assert.Equal(HtmlNodeKind.Comment, n[5].Kind);
            Assert.Equal(HtmlNodeKind.Declaration, n[6].Kind);
            Assert.DoesNotContain(n, x => x.Name == "script");
            AssertContiguous(src);
        }

        [Fact]
        public void LessThanInTextIsText()
        {
            var src = "a < b <3 </ > c";
            var n = HtmlScanner.Scan(src);
            Assert.DoesNotContain(n, x => x.Kind == HtmlNodeKind.StartTag);
            AssertContiguous(src);
        }

        [Fact]
        public void SelfClosingAndUnterminated()
        {
            var n = HtmlScanner.Scan("<br/><img src=a.png />");
            Assert.True(n[0].SelfClosing);
            Assert.True(n[1].SelfClosing);
            Assert.Equal("a.png", n[1].GetAttribute("src").Value);
            var u = HtmlScanner.Scan("<div class=\"x");
            Assert.True(u[0].Unterminated);
            var s = HtmlScanner.Scan("<script>var a;");
            Assert.True(s[1].Unterminated);
        }

        [Fact]
        public void GetScripts()
        {
            var src = "<head><script defer src=\"../sw/core.js\"></script><script type=\"text/template\">${x}</script><script type=module>a()</script><script>b()</script></head>";
            var s = HtmlScanner.GetScripts(HtmlScanner.Scan(src));
            Assert.Equal(4, s.Count);
            Assert.Equal("../sw/core.js", s[0].Src);
            Assert.Equal(s[0].BodyStart, s[0].BodyEnd);
            Assert.False(s[1].IsJavaScript);
            Assert.True(s[2].IsJavaScript);
            Assert.Equal("a()", src.Substring(s[2].BodyStart, s[2].BodyEnd - s[2].BodyStart));
            Assert.Null(s[3].Src);
            Assert.Equal("b()", src.Substring(s[3].BodyStart, s[3].BodyEnd - s[3].BodyStart));
        }
    }
}
