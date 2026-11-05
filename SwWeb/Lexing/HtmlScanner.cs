using System;
using System.Collections.Generic;

namespace SwWeb.Lexing
{
    /// <summary>
    /// The kind of a HTML node.
    /// </summary>
    public enum HtmlNodeKind
    {
        /// <summary>
        /// Text between tags (character references are not decoded).
        /// </summary>
        Text,
        /// <summary>
        /// A <c>&lt;!-- ... --&gt;</c> comment.
        /// </summary>
        Comment,
        /// <summary>
        /// <c>&lt;!DOCTYPE ...&gt;</c>, <c>&lt;![CDATA[ ... ]]&gt;</c>, <c>&lt;? ... &gt;</c> and other markup declarations.
        /// </summary>
        Declaration,
        /// <summary>
        /// A start tag with attributes.
        /// </summary>
        StartTag,
        /// <summary>
        /// An end tag.
        /// </summary>
        EndTag,
        /// <summary>
        /// The raw content of a <c>script</c>, <c>style</c>, <c>textarea</c> or <c>title</c> element (or the rest of the document after a <c>plaintext</c> tag).
        /// </summary>
        RawText,
    }

    /// <summary>
    /// A HTML attribute.
    /// </summary>
    public sealed class HtmlAttribute
    {
        /// <summary>
        /// The attribute name as written.
        /// </summary>
        public String Name;
        /// <summary>
        /// Offset of the name.
        /// </summary>
        public int NameStart;
        /// <summary>
        /// The raw value (without quotes, character references not decoded), null if the attribute has no value.
        /// </summary>
        public String Value;
        /// <summary>
        /// Offset of the value (after the opening quote), -1 if no value.
        /// </summary>
        public int ValueStart = -1;
        /// <summary>
        /// The quote char used ('"', '\'') or '\0' if unquoted / no value.
        /// </summary>
        public char Quote;
        /// <summary>
        /// Offset after the attribute (after the closing quote).
        /// </summary>
        public int End;

        /// <summary>
        /// A debug string.
        /// </summary>
        public override string ToString() => Value == null ? Name : (Name + "=" + Quote + Value + Quote);
    }

    /// <summary>
    /// A HTML node (a span in the source).
    /// </summary>
    public sealed class HtmlNode
    {
        /// <summary>
        /// The node kind.
        /// </summary>
        public HtmlNodeKind Kind;
        /// <summary>
        /// Offset of the first char.
        /// </summary>
        public int Start;
        /// <summary>
        /// Offset after the last char.
        /// </summary>
        public int End;
        /// <summary>
        /// Lower case tag name for start / end tags (the parent element name for raw text), else null.
        /// </summary>
        public String Name;
        /// <summary>
        /// The attributes of a start tag (empty for other kinds).
        /// </summary>
        public readonly List<HtmlAttribute> Attributes = new List<HtmlAttribute>();
        /// <summary>
        /// True if a start tag ends with "/&gt;".
        /// </summary>
        public bool SelfClosing;
        /// <summary>
        /// True if a tag, comment or raw text element isn't terminated before the end of the input.
        /// </summary>
        public bool Unterminated;

        /// <summary>
        /// Get an attribute by name (case insensitive).
        /// </summary>
        /// <param name="name">The attribute name</param>
        /// <returns>The attribute or null</returns>
        public HtmlAttribute GetAttribute(String name)
        {
            foreach (var a in Attributes)
                if (String.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                    return a;
            return null;
        }

        /// <summary>
        /// A debug string.
        /// </summary>
        public override string ToString() => Kind + (Name == null ? "" : (" " + Name)) + "@" + Start + "-" + End;
    }

    /// <summary>
    /// A script element found by <see cref="HtmlScanner.GetScripts"/>.
    /// </summary>
    public sealed class HtmlScript
    {
        /// <summary>
        /// The start tag.
        /// </summary>
        public HtmlNode Tag;
        /// <summary>
        /// The src attribute value (null for inline scripts).
        /// </summary>
        public String Src;
        /// <summary>
        /// Offset of the body (after the start tag).
        /// </summary>
        public int BodyStart;
        /// <summary>
        /// Offset after the body (before "&lt;/script").
        /// </summary>
        public int BodyEnd;
        /// <summary>
        /// True if the type attribute is missing or a JavaScript type (including module).
        /// </summary>
        public bool IsJavaScript;
    }

    /// <summary>
    /// A tolerant HTML tokenizer: tags with attributes (quoted, unquoted, valueless), comments, declarations,
    /// text and raw text elements (script, style, textarea, title). It doesn't build a tree.
    /// Every char of the input is covered by exactly one node.
    /// </summary>
    public static class HtmlScanner
    {
        static readonly HashSet<String> RawTextElements = new HashSet<String>(StringComparer.Ordinal) { "script", "style", "textarea", "title", "xmp", "iframe", "noembed", "noframes" };

        static readonly HashSet<String> JsTypes = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "", "module", "text/javascript", "application/javascript", "text/ecmascript", "application/ecmascript", "text/jscript", "text/livescript", "application/x-javascript", "application/x-ecmascript", "text/x-javascript", "text/x-ecmascript",
        };

        static bool IsSpace(char c) => (c == ' ') || (c == '\t') || (c == '\n') || (c == '\r') || (c == '\f');

        static bool IsAsciiLetter(char c) => ((c >= 'a') && (c <= 'z')) || ((c >= 'A') && (c <= 'Z'));

        /// <summary>
        /// Scan a HTML document.
        /// </summary>
        /// <param name="s">The HTML text</param>
        /// <returns>The nodes in document order</returns>
        public static List<HtmlNode> Scan(String s)
        {
            var nodes = new List<HtmlNode>();
            int pos = 0;
            int len = s.Length;
            int textStart = 0;

            void FlushText(int end)
            {
                if (end > textStart)
                    nodes.Add(new HtmlNode { Kind = HtmlNodeKind.Text, Start = textStart, End = end });
            }

            while (pos < len)
            {
                if (s[pos] != '<')
                {
                    ++pos;
                    continue;
                }
                var n1 = pos + 1 < len ? s[pos + 1] : '\0';
                if (n1 == '!')
                {
                    FlushText(pos);
                    var node = new HtmlNode { Start = pos };
                    if ((pos + 3 < len) && (s[pos + 2] == '-') && (s[pos + 3] == '-'))
                    {
                        node.Kind = HtmlNodeKind.Comment;
                        // "<!-->" and "<!--->" are (empty) comments
                        int search = pos + 4;
                        if ((search < len) && (s[search] == '>'))
                            node.End = search + 1;
                        else if ((search + 1 < len) && (s[search] == '-') && (s[search + 1] == '>'))
                            node.End = search + 2;
                        else
                        {
                            var e = s.IndexOf("-->", search, StringComparison.Ordinal);
                            if (e < 0)
                            {
                                node.End = len;
                                node.Unterminated = true;
                            }
                            else
                                node.End = e + 3;
                        }
                    }
                    else if (String.CompareOrdinal(s, pos, "<![CDATA[", 0, 9) == 0)
                    {
                        node.Kind = HtmlNodeKind.Declaration;
                        var e = s.IndexOf("]]>", pos + 9, StringComparison.Ordinal);
                        node.Unterminated = e < 0;
                        node.End = e < 0 ? len : e + 3;
                    }
                    else
                    {
                        node.Kind = HtmlNodeKind.Declaration;
                        var e = s.IndexOf('>', pos + 2);
                        node.Unterminated = e < 0;
                        node.End = e < 0 ? len : e + 1;
                    }
                    nodes.Add(node);
                    pos = textStart = node.End;
                    continue;
                }
                if (n1 == '?')
                {
                    FlushText(pos);
                    var e = s.IndexOf('>', pos + 2);
                    var node = new HtmlNode { Kind = HtmlNodeKind.Declaration, Start = pos, End = e < 0 ? len : e + 1, Unterminated = e < 0 };
                    nodes.Add(node);
                    pos = textStart = node.End;
                    continue;
                }
                bool end = n1 == '/';
                int ns = end ? pos + 2 : pos + 1;
                if ((ns >= len) || !IsAsciiLetter(s[ns]))
                {
                    if (end && (ns < len) && (s[ns] == '>'))
                    {
                        // "</>" is ignored by browsers
                        FlushText(pos);
                        nodes.Add(new HtmlNode { Kind = HtmlNodeKind.Declaration, Start = pos, End = ns + 1 });
                        pos = textStart = ns + 1;
                        continue;
                    }
                    ++pos;
                    continue;
                }
                FlushText(pos);
                var tag = ScanTag(s, pos, end);
                nodes.Add(tag);
                pos = textStart = tag.End;
                if ((!end) && (!tag.Unterminated) && (tag.Name == "plaintext"))
                {
                    if (pos < len)
                        nodes.Add(new HtmlNode { Kind = HtmlNodeKind.RawText, Name = tag.Name, Start = pos, End = len });
                    pos = textStart = len;
                    break;
                }
                if ((!end) && (!tag.SelfClosing || tag.Name == "script") && RawTextElements.Contains(tag.Name))
                {
                    // Find the matching end tag (case insensitive), "</name" followed by space, '/' or '>'
                    var close = "</" + tag.Name;
                    int search = pos;
                    int found = -1;
                    while (search < len)
                    {
                        var e = s.IndexOf(close, search, StringComparison.OrdinalIgnoreCase);
                        if (e < 0)
                            break;
                        var a = e + close.Length;
                        if ((a >= len) || IsSpace(s[a]) || (s[a] == '/') || (s[a] == '>'))
                        {
                            found = e;
                            break;
                        }
                        search = e + 1;
                    }
                    var rawEnd = found < 0 ? len : found;
                    if (rawEnd > pos)
                        nodes.Add(new HtmlNode { Kind = HtmlNodeKind.RawText, Name = tag.Name, Start = pos, End = rawEnd, Unterminated = found < 0 });
                    else if (found < 0)
                        tag.Unterminated = true;
                    pos = textStart = rawEnd;
                }
            }
            FlushText(len);
            return nodes;
        }

        static HtmlNode ScanTag(String s, int pos, bool end)
        {
            int len = s.Length;
            var node = new HtmlNode { Kind = end ? HtmlNodeKind.EndTag : HtmlNodeKind.StartTag, Start = pos };
            int p = end ? pos + 2 : pos + 1;
            int ns = p;
            while ((p < len) && !IsSpace(s[p]) && (s[p] != '/') && (s[p] != '>'))
                ++p;
            node.Name = s.Substring(ns, p - ns).ToLowerInvariant();
            for (; ; )
            {
                while ((p < len) && (IsSpace(s[p]) || ((s[p] == '/') && ((p + 1 >= len) || (s[p + 1] != '>')))))
                    ++p;
                if (p >= len)
                {
                    node.End = len;
                    node.Unterminated = true;
                    return node;
                }
                if (s[p] == '>')
                {
                    node.End = p + 1;
                    return node;
                }
                if ((s[p] == '/') && (p + 1 < len) && (s[p + 1] == '>'))
                {
                    node.SelfClosing = true;
                    node.End = p + 2;
                    return node;
                }
                // Attribute name (a leading '=' is part of the name, as in browsers)
                var a = new HtmlAttribute { NameStart = p };
                ++p;
                while ((p < len) && !IsSpace(s[p]) && (s[p] != '/') && (s[p] != '>') && (s[p] != '='))
                    ++p;
                a.Name = s.Substring(a.NameStart, p - a.NameStart);
                int q = p;
                while ((q < len) && IsSpace(s[q]))
                    ++q;
                if ((q < len) && (s[q] == '='))
                {
                    p = q + 1;
                    while ((p < len) && IsSpace(s[p]))
                        ++p;
                    if (p < len)
                    {
                        var c = s[p];
                        if ((c == '"') || (c == '\''))
                        {
                            a.Quote = c;
                            a.ValueStart = p + 1;
                            var e = s.IndexOf(c, p + 1);
                            if (e < 0)
                            {
                                a.Value = s.Substring(p + 1);
                                p = len;
                            }
                            else
                            {
                                a.Value = s.Substring(p + 1, e - p - 1);
                                p = e + 1;
                            }
                        }
                        else
                        {
                            a.ValueStart = p;
                            while ((p < len) && !IsSpace(s[p]) && (s[p] != '>'))
                                ++p;
                            a.Value = s.Substring(a.ValueStart, p - a.ValueStart);
                        }
                    }
                    else
                    {
                        a.ValueStart = p;
                        a.Value = "";
                    }
                }
                a.End = p;
                if (!end)
                    node.Attributes.Add(a);
            }
        }

        /// <summary>
        /// Get all script elements of a scanned document.
        /// </summary>
        /// <param name="nodes">The nodes from <see cref="Scan"/></param>
        /// <returns>The script elements in document order</returns>
        public static List<HtmlScript> GetScripts(List<HtmlNode> nodes)
        {
            var res = new List<HtmlScript>();
            var c = nodes.Count;
            for (int i = 0; i < c; ++i)
            {
                var n = nodes[i];
                if ((n.Kind != HtmlNodeKind.StartTag) || (n.Name != "script"))
                    continue;
                var sc = new HtmlScript
                {
                    Tag = n,
                    Src = n.GetAttribute("src")?.Value,
                    BodyStart = n.End,
                    BodyEnd = n.End,
                };
                var type = n.GetAttribute("type")?.Value?.Trim() ?? "";
                sc.IsJavaScript = JsTypes.Contains(type);
                if ((i + 1 < c) && (nodes[i + 1].Kind == HtmlNodeKind.RawText))
                    sc.BodyEnd = nodes[i + 1].End;
                res.Add(sc);
            }
            return res;
        }
    }
}
