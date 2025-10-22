using SysWeaver.Compression;
using SysWeaver.Serialization;
using Svg;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SysWeaver
{



    internal class SvgOptProgram
    {

        static Func<SvgElement, T> GetInternalProp<T>(String name)
        {
            var c = Expression.Variable(typeof(SvgElement));
            return Expression.Lambda<Func<SvgElement, T>>(
                Expression.Property(c, typeof(SvgElement).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)), c).Compile();
        }

        static readonly Func<SvgElement, SvgAttributeCollection> GetAttributes = GetInternalProp<SvgAttributeCollection>("Attributes");
        static readonly Func<SvgElement, String> GetElementName = GetInternalProp<String>("ElementName");



        static void OnAllElements<T>(SvgElement doc, Action<SvgElement, T> onNode, T t)
        {
            onNode(doc, t);
            foreach (var x in doc.Descendants())
                OnAllElements(x, onNode, t);
        }

        static void OnAllElements(SvgElement element, Action<SvgElement> onElement)
        {
            onElement(element);
            foreach (var x in element.Descendants())
                OnAllElements(x, onElement);
        }

        static void OnAllElementsX(XElement element, Action<XElement> onElement)
        {
            foreach (var x in element.DescendantsAndSelf())
                onElement(x);
        }


        static void RemoveAttribute(XElement element, String name)
        {
            if (element == null)
                return;
            foreach (var a in element.Attributes())
            {
                if (a.Name.LocalName == name)
                    a.Remove();
            }
        }

        static void RemoveAttributeIf(XElement element, String name, Func<XAttribute, bool> doRemove)
        {
            if (element == null)
                return;
            foreach (var a in element.Attributes())
            {
                if (a.Name.LocalName == name)
                    if (doRemove(a))
                        a.Remove();
            }
        }

        static void RemoveAttribute(SvgElement element, String name)
        {
            if (element == null)
                return;
            var a = GetAttributes(element);
            if (!a.ContainsKey(name))
                return;
            a.Remove(name);
        }


        static String GetAttributeValue(SvgElement element, String name, String defaultValue = null)
        {
            if (element == null)
                return defaultValue;
            return GetAttributes(element).TryGetValue(name, out var value) ? value?.ToString() : defaultValue;
        }

        static SvgElement FindFirst(SvgElement element, String name)
        {
            if (GetElementName(element) == name)
                return element;
            foreach (var x in element.Descendants())
            {
                var y = FindFirst(x, name);
                if (y != null)
                    return y;
            }
            return null;
        }


        static XElement FindFirst(XElement element, String name)
        {
            foreach (var e in element.DescendantNodesAndSelf())
            {
                var x = e as XElement;
                if (x == null)
                    continue;
                if (x.Name.LocalName == name)
                    return x;
            }
            return null;
        }

        static double ReadDoubleAttr(XElement element, String attr, double def = 0)
        {
            var a = element.Attribute(attr);
            if (a == null)
                return def;
            return double.TryParse(a.Value, CultureInfo.InvariantCulture, out var value) ? value : def;
        }

        static bool MatchSize(XElement element, double x, double y, double width, double height)
        {
            if (element == null)
                return false;
            if (ReadDoubleAttr(element, "width") == width)
                return false;
            if (ReadDoubleAttr(element, "height") == height)
                return false;
            if (ReadDoubleAttr(element, "x") == x)
                return false;
            if (ReadDoubleAttr(element, "y") == y)
                return false;
            return true;
        }


        static bool AreEqual(Bitmap a, Bitmap b)
        {
            var w = a.Width;
            var h = a.Height;
            if (w != b.Width)
                return false;
            if (h != b.Height)
                return false;
            var r = new Rectangle(0, 0, w, h);
            var al = a.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            unsafe
            {
                try
                {
                    var ad = new ReadOnlySpan<Byte>(al.Scan0.ToPointer(), h * al.Stride);
                    var bl = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        var bd = new ReadOnlySpan<Byte>(bl.Scan0.ToPointer(), h * bl.Stride);
                        var rowLen = w * 4;
                        for (int i = 0; i < h; ++i)
                        {
                            if (!ad.Slice(i * al.Stride, rowLen).SequenceEqual(bd.Slice(i * bl.Stride, rowLen)))
                                return false;
                        }
                    }
                    finally
                    {
                        b.UnlockBits(bl);
                    }
                }
                finally
                {
                    a.UnlockBits(al);
                }
            }
            return true;
        }



        static bool GetRender(SvgDocument svg, out int w, out int h)
        {
            w = (int)Math.Ceiling(svg.ViewBox.Width * 10);
            h = (int)Math.Ceiling(svg.ViewBox.Height * 10);
            const int max = 2048;
            bool l = false;
            if (w > max)
            {
                h = (int)((((long)h) * (long)max + (w - 1L)) / w);
                w = max;
                l = true;
            }
            if (h > max)
            {
                w = (int)((((long)w) * (long)max + (h - 1L)) / h);
                h = max;
                l = true;
            }
            return l;
        }


        //static int CP = 0;

        static Bitmap GetValidationRef(XDocument doc)
        {
            try
            {
                SvgDocument svg;
                using (var ms = new MemoryStream())
                {
                    doc.Save(ms);
                    ms.Position = 0;
                    svg = SvgDocument.Open<SvgDocument>(ms);
                }
                GetRender(svg, out var w, out var h);
                var bm = new Bitmap(w, h);
                try
                {
                    using var g = Graphics.FromImage(bm);
                    g.Clear(Color.Red);
                    g.FillRectangle(Brushes.Green, 0, 0, w, h >> 1);
                    svg.Draw(g, new SizeF(w, h));
                    //bm.Save("DebugApa" + CP + ".png");
                    //++CP;
                    return bm;
                }
                catch
                {
                    bm.Dispose();
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        static bool IsSame(Bitmap bitmap, XDocument doc)
        {
            if (bitmap == null)
                return false;
            try
            {
                using (var d = GetValidationRef(doc))
                    return AreEqual(bitmap, d);
            }
            catch
            {
            }
            return false;
        }


        static void Validate(Bitmap bitmap, XDocument doc)
        {
            if (bitmap == null)
                return;
            using (var d = GetValidationRef(doc))
            {
                if (!AreEqual(bitmap, d))
                {
                    bitmap.Save("RefImage.png");
                    d.Save("FailedImage.png");
                    throw new Exception("Invalid!");
                }
            }
        }

        static bool TryRemoveAttribute(ref Bitmap refImage, XElement element, String attribName)
        {
            var attr = element.Attribute(attribName);
            if (attr == null)
                return false;
            var xname = attr.Name;
            var prev = attr.Value;
            if (refImage == null)
            {
                refImage = GetValidationRef(element.Document);
                //refImage.Save(@"D:\Temp\SvgOut\a.png");
            }
            attr.Remove();
            if (IsSame(refImage, element.Document))
                return true;
            element.SetAttributeValue(xname, prev);
            /*            using (var img = doc.Draw(w, h))
                            img.Save(@"D:\Temp\SvgOut\c.png");*/
            return false;
        }


        static bool TryRemoveElement(ref Bitmap refImage, XElement element)
        {
            if (refImage == null)
            {
                refImage = GetValidationRef(element.Document);
                //refImage.Save(@"D:\Temp\SvgOut\a.png");
            }
            var an = XName.Get("visibility");
            var vis = element.Attribute(an);
            var old = vis?.Value;
            if (old == "hidden")
            {
                element.Remove();
                return true;
            }
            element.SetAttributeValue(an, "hidden");
            if (IsSame(refImage, element.Document))
            {
                element.Remove();
                return true;
            }
            if (vis != null)
            {
                element.SetAttributeValue(an, old);
            }else
            {
                element.Attribute(an).Remove();
            }
            return false;
        }

        static readonly HashSet<String> DoNotRemove = new HashSet<string>
        {
            "svg",
        };

        static readonly HashSet<String> ColorAttributes = new HashSet<string>
        {
            "fill",
            "stroke",
        };


        static readonly Dictionary<String, String> DecimalLimits = new(StringComparer.Ordinal)
        {
            { "circle", "cx,cy,r,stroke-width" },
            { "ellipse", "cx,cy,rx,ry,stroke-width" },
            { "line", "x1,y1,x2,y2,stroke-width" },
            { "path", "d,stroke-width" },
            { "polygon", "points,stroke-width" },
            { "polyline", "points,stroke-width" },
            { "rect", "x,y,width,height,rx,ry,stroke-width" },
            { "svg", "viewbox" },
        };

        static readonly Dictionary<String, String> DecimalLimitsLong = new(StringComparer.Ordinal)
        {
            { "animate", "values,from,to" },
            { "animateTransform", "values,from,to" },
        };


        static readonly HashSet<Char> NumbersAndDecimalPoint = new HashSet<char>("0123456789.");

        static String SetMaxDecimals(String value, int maxDecimals)
        {
            var vl = value.Length;
            StringBuilder b = new StringBuilder(vl);
            int i = 0;
            int last = 0;
            for (; ; )
            {
                i = value.IndexOf('.', i);
                if (i < 0)
                    break;
                if (i == 0)
                {
                    ++i;
                    continue;
                }
                var start = i;
                while (start > 0)
                {
                    --start;
                    if (!Char.IsDigit(value[start]))
                    {
                        ++start;
                        break;
                    }
                }
                if (start == i)
                {
                    ++i;
                    continue;
                }
                int end;
                for (end = i + 1; end < vl; ++end)
                {
                    if (!Char.IsDigit(value[end]))
                        break;
                }
                if (last < start)
                    b.Append(value, last, start - last);
                var val = Decimal.Parse(value.Substring(start, end - start), CultureInfo.InvariantCulture);
                val = Math.Round(val, maxDecimals);
                var x = val.ToString(CultureInfo.InvariantCulture);
                if (x.IndexOf('.') >= 0)
                {
                    x = x.TrimEnd('0').TrimEnd('.');
                    if (x.Length == 0)
                        x = "0";
                }
                if (x.StartsWith("0."))
                    x = "." + x.Substring(2);
                b.Append(x);
                last = end;
                i = end;
            }
            if (last == 0)
                return value;
            b.Append(value, last, vl - last);
            return b.ToString();
        }

        static int SkipSpaces(String value, int start)
        {
            var vl = value.Length;
            for (; ;)
            {
                if (start >= vl)
                    return vl;
                if (value[start] != ' ')
                    return start;
                ++start;
            }
        }

        static int SkipNumber(String value, int start)
        {
            var vl = value.Length;
            if (start >= vl)
                return vl;
            if (value[start] == '-')
                ++start;
            bool haveDecimals = false;
            for (; ;)
            {
                if (start >= vl)
                    return vl;
                var c = value[start];
                if (Char.IsDigit(c))
                {
                    ++start;
                    continue;
                }
                if (c == '.')
                {
                    if (haveDecimals)
                        return start;
                    haveDecimals = true;
                    ++start;
                    continue;
                }
                return start;
            }
        }

        static String OptimizePathArcs(String value)
        {
            var vl = value.Length;
            var b = new StringBuilder(vl);
            int i = 0;
            int last = 0;
            for (; ; )
            {
                i = value.IndexOf("a", i, StringComparison.OrdinalIgnoreCase);
                if (i < 0)
                    break;
                ++i;
                for (; ; )
                {
                    //  Skip rx
                    i = SkipNumber(value, i);
                    i = SkipSpaces(value, i);
                    //  Skip ry
                    i = SkipNumber(value, i);
                    i = SkipSpaces(value, i);
                    //  Skip x-axis-rotation
                    i = SkipNumber(value, i);
                    i = SkipSpaces(value, i);
                    ++i;
                    //  If large-arc-flag end with a space, remove it
                    if (value[i] == ' ')
                    {
                        b.Append(value, last, i - last);
                        ++i;
                        last = i;
                    }
                    //  If sweep-flag end with a space, remove it
                    ++i;
                    if (value[i] == ' ')
                    {
                        b.Append(value, last, i - last);
                        ++i;
                        last = i;
                    }
                    //  Skip x/dx
                    i = SkipNumber(value, i);
                    i = SkipSpaces(value, i);
                    //  Skip y/dy
                    i = SkipNumber(value, i);
                    i = SkipSpaces(value, i);
                    if (i >= vl)
                        break;
                    Char c = value[i];
                    if (Char.IsLetter(c))
                        break;
                    if (c == ' ')
                        ++i;
                }
                if (i < 0)
                    break;
            }
            if (last == 0)
                return value;
            b.Append(value, last, vl - last);
            return b.ToString();
        }

        static int IndexOf(String value, int startIndex, Func<Char, int, bool> isFound)
        {
            var vl = value.Length;
            for (int i = startIndex; i < vl; ++i)
            {
                if (isFound(value[i], i))
                    return i;
            }
            return -1;
        }

        static readonly HashSet<Char> AllowedJoins = new HashSet<char>("aAlL");

        static String JoinPathCommands(String value)
        {
            var vl = value.Length;
            Char[] str = null;
            int i = 0;
            for (; ; )
            {
                i = IndexOf(value, i, (c, p) => Char.IsLetter(c));
                if (i < 0)
                    break;
                var start = i;
                Char cmd = value[i];
                ++i;
                if (!AllowedJoins.Contains(cmd))
                    continue;
                for (; ; )
                {
                    i = IndexOf(value, i, (c, p) => Char.IsLetter(c));
                    if (i < 0)
                        break;
                    var nextCmd = value[i];
                    if (nextCmd != cmd)
                        break;
                    var join = value.Substring(start);
                    if (str == null)
                        str = value.ToCharArray();
                    str[i] = ' ';
                    ++i;
                }
                if (i < 0)
                    break;
            }
            if (str == null)
                return value;
            return new string(str);
        }

        static String TrimDoubleZeros(String value, bool isArc)
        {
            var vl = value.Length;
            var b = new StringBuilder(vl);
            int i = 0;
            int last = 0;
            var nums = NumbersAndDecimalPoint;
            int ok = -1;
            for (; ; )
            {
                i = value.IndexOf('0', i);
                if (i < 0)
                    break;
                ++i;
                if (i >= vl)
                    continue;
                if (!nums.Contains(value[i]))
                    continue;
                if ((i > 1) && (i != ok) && nums.Contains(value[i - 2]))
                    continue;
                if (isArc)
                {
                    char cmd = '0';
                    var o = i - 1;
                    while (o > 0)
                    {
                        --o;
                        cmd = value[o];
                        if (char.IsLetter(cmd))
                            break;
                    }
                    if ((cmd == 'a') || (cmd == 'A'))
                        continue;
                }
                ok = i + 1;
                b.Append(value, last, i - 1 - last);
                last = i;
            }
            if (last == 0)
                return value;
            b.Append(value, last, vl - last);
            return b.ToString();
        }


        static String TrimLeadingZeros(String value, bool isArc)
        {
            value = TrimDoubleZeros(value, isArc);
            var vl = value.Length;
            var b = new StringBuilder(vl);
            int i = 0;
            int last = 0;
            for (; ; )
            {
                i = value.IndexOf('.', i);
                if (i < 0)
                    break;
                ++i;
                if (i < 2)
                    continue;
                if (value[i - 2] != '0')
                    continue;
                bool canRemove = i < 3 || (!Char.IsDigit(value[i - 3]));
                if (!canRemove)
                    continue;
                int r = 2;
                if (i > 4)
                {
                    if (value[i - 3] == ' ')
                        if (!Char.IsDigit(value[i - 4]))
                            ++r;
                }
                b.Append(value, last, i - r - last);
                last = i - 1;
            }
            if (last == 0)
                return NegZeroToZero(value);
            b.Append(value, last, vl - last);
            return NegZeroToZero(b.ToString());
        }

        static String NegZeroToZero(String value)
        {
            var vl = value.Length;
            var b = new StringBuilder(vl);
            int i = 0;
            int last = 0;
            for (; ; )
            {
                i = value.IndexOf('-', i);
                if (i < 0)
                    break;
                ++i;
                if (i >= vl)
                    break;
                if (value[i] != '0')
                    continue;
                ++i;
                if ((i < vl) && (value[i] == '.'))
                    continue;
                if ((i > 2) && NumbersAndDecimalPoint.Contains(value[i - 3]))
                    continue;
                b.Append(value, last, i - 2 - last);
                --i;
                last = i;
            }
            if (last == 0)
                return value;
            b.Append(value, last, vl - last);
            return b.ToString();

        }


        static String TrimTrailingZeros(String value)
        {
            var vl = value.Length;
            var b = new StringBuilder(vl);
            int i = 0;
            int last = 0;
            for (; ; )
            {
                i = value.IndexOf('.', i);
                if (i < 0)
                    break;
                ++i;
                var e = IndexOf(value, i, (c, p) => !Char.IsDigit(c));
                if (e < 0)
                    e = vl;
                var end = e;
                while (e > i)
                {
                    --e;
                    if (value[e] != '0')
                    {
                        ++e;
                        break;
                    }
                }
                if (e == end)
                    continue;
                if (e == i)
                {
                    --e;
                    //  All decimals
                    //  1.00 => 1      .000 => 0    -.00 => 0
                    bool prevIsDigit = i > 1 && Char.IsDigit(value[i - 2]);
                    if (prevIsDigit)
                    {
                        // 1.00 => 1
                        b.Append(value, last, e - last);
                    }
                    else
                    {
                        // .00 => 0   or  -.00 => 0
                        if ((i > 1) && (value[i - 2] == '-'))
                            --e;
                        b.Append(value, last, e - last);
                        b.Append('0');
                    }
                }
                else
                {
                    //  Some zeros
                    // 4.10 => 4.1
                    b.Append(value, last, e - last);
                }
                last = end;
                i = end;
            }
            if (last == 0)
                return value;
            b.Append(value, last, vl - last);
            return b.ToString();
        }

        static String OptimizePath(String value)
        {
            value = value.Trim();
            value = value.Replace(',', ' ');
            value = value.Replace('\t', ' ');
            value = JoinPathCommands(value);
            for (; ; )
            {
                var t = value;
                value = value.Replace("  ", " ");
                if (value == t)
                    break;
            }
            var vl = value.Length;
            var b = new StringBuilder(vl);
            int i = 0;
            int last = 0;
            for (; ; )
            {
                i = value.IndexOf(' ', i);
                if (i < 0)
                    break;
                var prev = value[i - 1];
                var pd = Char.IsDigit(prev);
                if (pd == NumbersAndDecimalPoint.Contains(value[i + 1]))
                {
                    if (pd)
                    {
                        ++i;
                        continue;
                    }
                }
                b.Append(value, last, i - last);
                ++i;
                last = i;
            }
            if (last == 0)
                return OptimizePathArcs(value);
            b.Append(value, last, vl - last);
            return OptimizePathArcs(b.ToString());
        }

        static String ReplaceAll(String s, String v = " ", String to = "")
        {
            for (; ; )
            {
                var t = s;
                s = s.Replace(v, to);
                if (s == t)
                    return s;
            }
        }

        static String FixValuesUpUntilP(String value, int decimalLimit)
        {
            var l = value.IndexOf(')');
            if (l < 0)
                return value;
            return SetMaxDecimals(value.Substring(0, l), decimalLimit) + value.Substring(l);
        }

        static String FixTransform(String value, int decimalLimit)
        {
            value = value.Trim();
            value = ReplaceAll(value, "  ", " ");
            value = value.Replace(',', ' ');
            value = ReplaceAll(value, "  ", " ");
            var parts = value.Split('(', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var l = parts.Length - 1;
            for (int i = 0; i < l; ++i)
            {
                var x = parts[i];
                if (x.EndsWith("rotate", StringComparison.OrdinalIgnoreCase))
                {
                    var y = parts[i + 1];
                    y = FixValuesUpUntilP(y, 4);
                    y = y.Replace(" 0 0)", ")");
                    parts[i + 1] = y;
                    continue;
                }
                if (x.EndsWith("scale", StringComparison.OrdinalIgnoreCase))
                {
                    var y = parts[i + 1];
                    y = FixValuesUpUntilP(y, 10);
                    var pp = y.IndexOf(')');
                    if (pp > 0)
                    {
                        var p = y.Substring(0, pp).Split(' ');
                        if (p.Length == 2)
                        {
                            if (p[0] == p[1])
                                y = p[0] + y.Substring(pp);
                        }
                    }
                    parts[i + 1] = y;
                    continue;
                }

                if (x.EndsWith("translate", StringComparison.OrdinalIgnoreCase))
                {
                    var y = parts[i + 1];
                    y = FixValuesUpUntilP(y, decimalLimit >= 0 ? decimalLimit : 10);
                    y = ReplaceAll(y, " 0)", ")");
                    parts[i + 1] = y;
                    continue;
                }
            }
            value = String.Join('(', parts);
            value = ReplaceAll(value, "  ", " ");
            return value;

        }

        static String TrimNonDigits(string value, bool acceptDecimalComma = true)
        {
            value = value.Trim();
            var l = value.Length;
            bool haveComma = false;
            int i;
            for (i = 0; i < l; ++ i)
            {
                var c = value[i];
                if ((c >= '0') && (c <= '9'))
                    continue;
                if (i == 0)
                {
                    if (c == '-')
                        continue;
                    if (c == '+')
                        continue;
                }
                if (acceptDecimalComma)
                {
                    if (c == '.')
                    {
                        if (!haveComma)
                        {
                            haveComma = true;
                            continue;
                        }
                    }
                }
                break;
            }
            return value.Substring(0, i);
        }

        const String First = "abcdefghijklmnopqrstuvwxyz";
        const String Other = First + "_0123456789";

        static String GenShort(int index)
        {
            var sb = new StringBuilder(10);
            var c = First;
            var cl = c.Length;
            sb.Append(c[index % cl]);
            if (index < cl)
                return sb.ToString();
            c = Other;
            index /= cl;
            cl = c.Length;
            while (index > 0)
            {
                sb.Append(c[index % cl]);
                index /= cl;
            }
            return sb.ToString();
        }


        static readonly Char[] SplitChars = " ,".ToCharArray();
        static int Optimize(String sourceFile, String destFile, IMessageHost msg, SvgOptParams opt)
        {
            try
            {
                XDocument xdoc = XDocument.Load(sourceFile, opt.RemoveWhiteSpaces ? LoadOptions.None : LoadOptions.PreserveWhitespace);
                var xSvg = FindFirst(xdoc.Root, "svg");
                var va = xSvg.Attribute("viewBox");
                double x = 0, y = 0, width = 0, height = 0;
                if (va != null)
                {
                    var coords = va.Value.Trim().Split(SplitChars, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (coords.Length >= 4)
                    {
                        double.TryParse(coords[0], CultureInfo.InvariantCulture, out x);
                        double.TryParse(coords[1], CultureInfo.InvariantCulture, out y);
                        double.TryParse(coords[2], CultureInfo.InvariantCulture, out width);
                        double.TryParse(coords[3], CultureInfo.InvariantCulture, out height);
                    }
                }
                if (opt.RemoveComments)
                {
                    xdoc.DescendantNodes().OfType<XComment>().Remove();
                }
                bool haveViewport = width > 0 && height > 0;
                bool canDoVisualOpts = haveViewport;
                if (haveViewport || opt.RemoveSize)
                {
                    RemoveAttribute(xSvg, "width");
                    RemoveAttribute(xSvg, "height");
                    bool doRemove(XAttribute v)
                    {
                        if (double.Parse(TrimNonDigits(v.Value)) == 0)
                            return true;
                        canDoVisualOpts = false;
                        return false;
                    }
                    RemoveAttributeIf(xSvg, "x", doRemove);
                    RemoveAttributeIf(xSvg, "y", doRemove);
                }
                if (canDoVisualOpts)
                {
                    if (opt.RemoveClipRule || opt.RemoveFillRule || opt.TryRemoveAttributes)
                    {
                        Bitmap refImage = null;
                        try
                        {
                            OnAllElementsX(xdoc.Root, element =>
                            {
                                if (!DecimalLimits.ContainsKey(element.Name.LocalName))
                                    return;
                                if (element == xSvg)
                                    return;
                                if (opt.TryRemoveAttributes)
                                {
                                    var attr = element.Attributes();
                                    foreach (var at in attr.Select(x => x.Name.LocalName).ToList())
                                        TryRemoveAttribute(ref refImage, element, at);
                                }
                                else
                                {
                                    if (opt.RemoveClipRule)
                                        TryRemoveAttribute(ref refImage, element, "clip-rule");
                                    if (opt.RemoveFillRule)
                                        TryRemoveAttribute(ref refImage, element, "fill-rule");
                                }

                            });
                        }
                        finally
                        {
                            if (refImage != null)
                                refImage.Dispose();
                        }
                    }
                }

                if (opt.RemoveBackground)
                {
                    var e = FindFirst(xdoc.Root, "rect");
                    if (MatchSize(e, x, y, width, height))
                        e.Remove();
                    RemoveAttribute(xSvg, "enable-background");
                }
                if (opt.RemoveFill)
                {
                    bool didRemove = false;
                    OnAllElementsX(xdoc.Root, element => RemoveAttributeIf(element, "fill", a =>
                    {
                        if (element == xSvg)
                            return false;
                        if (a.Value == "none")
                            return false;
                        didRemove = true;
                        return true;
                    }));
                    RemoveAttributeIf(xSvg, "fill", a =>
                    {
                        if (didRemove)
                            return true;
                        return a.Value != "none";
                    });
                }

                if (opt.RemoveUseSize)
                {
                    OnAllElementsX(xdoc.Root, element =>
                    {
                        if (element.Name.LocalName != "use")
                            return;
                        RemoveAttributeIf(element, "width", x => x.Value == "100%");
                        RemoveAttributeIf(element, "height", x => x.Value == "100%");
                    });
                }

                int decimalLimit = opt.MaxDecimals;
                if (opt.MaxDecimalRes > 0)
                {
                    //  TODO: Repsect scale transforms
                    double trScale = 1;
                    OnAllElementsX(xdoc.Root, element =>
                    {
                        var tr = element.Attribute(XName.Get("transform"));
                        if (tr == null)
                            return;
                        var s = tr.Value.Split('(');
                        var sl = s.Length - 1;
                        for (int i = 0; i < sl; ++i)
                        {
                            var ss = s[i].Trim();
                            if (ss.EndsWith("scale", StringComparison.OrdinalIgnoreCase))
                            {
                                ss = s[i + 1].Trim();
                                var si = ss.IndexOf(')');
                                if (si >= 0)
                                {
                                    ss = ss.Substring(0, si).TrimEnd().Replace(',', ' ');
                                    var cords = ss.Split(' ');
                                    foreach (var x in cords)
                                    {
                                        if (double.TryParse(x.Trim(), out var zx))
                                        {
                                            if (zx > trScale)
                                                trScale = zx;
                                        }
                                    }
                                }
                            }
                        }
                    });
                    if (haveViewport) 
                    {
                        var scale = Math.Ceiling((double)trScale * opt.MaxDecimalRes / Math.Min(width, height));
                        int decimals = 0;
                        double tscale = 1;
                        while (tscale < scale)
                        {
                            ++decimals;
                            tscale *= 10;
                        }
                        decimalLimit = decimals;
                    }
                }



                if (opt.RemoveIds)
                {
                    var data = GetIds(xdoc);
                    var idHist = data.Item1;
                    foreach (var ids in data.Item2)
                    {
                        TryReadId(ids.Name.LocalName, ids.Value, out var id);
                        if (idHist[id] == 1)
                            RemoveId(ids);
                    }
                }

                if (opt.RenameIds)
                {
                    var data = GetIds(xdoc);
                    var idHist = data.Item1;
                    if (idHist.Count > 0)
                    {
                        Dictionary<String, String> map = new Dictionary<string, string>();
                        int index = 0;
                        foreach (var id in idHist.OrderByDescending(x => x.Value))
                        {
                            var n = id.Key;
                            map[n] = GenShort(index);
                            ++index;
                        }
                        foreach (var ids in data.Item2)
                        {
                            var name = ids.Name.LocalName;
                            TryReadId(name, ids.Value, out var id);
                            if (idHist[id] != 1)
                                if (map.TryGetValue(id, out var newId))
                                    ids.Value = FormatNewId(name, newId);
                        }
                    }
                }

                if (opt.RemoveGraphs)
                {
                    for (; ; )
                    {
                        var empty = xdoc.DescendantNodes().OfType<XElement>().FirstOrDefault(x =>
                        {
                            if (x.Name.LocalName != "g")
                                return false;
                            if (x.Attributes().FirstOrDefault() != null)
                                return false;

                            

                            bool haveAnim = false;
                            foreach (var v in x.Elements())
                            {
                                haveAnim = v.Name.LocalName.FastStartsWith("animate");
                                if (haveAnim)
                                    break;
                            }
                            if (haveAnim)
                                return false;

                            var addAfter = x;
                            for (; ;)
                            {
                                var t = x.Descendants().FirstOrDefault();
                                if (t == null)
                                    break;
                                t.Remove();
                                addAfter.AddAfterSelf(t);
                                addAfter = t;
                            }
                            return true;
                        });
                        if (empty == null)
                            break;
                        empty.Remove();
                    }


                    for (; ; )
                    {
                        var empty = xdoc.DescendantNodes().OfType<XElement>().FirstOrDefault(x =>
                        {
                            if (x.Name.LocalName != "g")
                                return false;
                            var f = x.Descendants().Count();
                            if (f == 0)
                                return true;
                            return false;
                        });
                        if (empty == null)
                            break;
                        empty.Remove();
                    }

                }

                if (opt.RemoveHidden)
                {
                    for (; ; )
                    {
                        var empty = xdoc.DescendantNodes().OfType<XElement>().FirstOrDefault(x =>
                        {
                            var da = x.Attributes().LastOrDefault(y => y.Name.LocalName == "display");
                            if (da == null)
                                return false;
                            if (da.Value?.Trim() == "none")
                                return true;
                            return false;
                        });
                        if (empty == null)
                            break;
                        empty.Remove();
                    }
                }

                if (decimalLimit >= 0)
                {
                    var test = DecimalLimits;
                    OnAllElementsX(xdoc.Root, element =>
                    {
                        if (!test.TryGetValue(element.Name.LocalName, out var attrs))
                            return;
                        var hs = new HashSet<string>(attrs.Split(',').Select(x => x.Trim()));
                        foreach (var attr in element.Attributes())
                        {
                            var an = attr.Name.LocalName;
                            if (!hs.Contains(an))
                                if (an != "transform")
                                    continue;
                            attr.Value = SetMaxDecimals(attr.Value, decimalLimit);
                        }
                    });
                }
                {
                    var test = DecimalLimitsLong;
                    OnAllElementsX(xdoc.Root, element =>
                    {
                        if (!test.TryGetValue(element.Name.LocalName, out var attrs))
                            return;
                        var hs = new HashSet<string>(attrs.Split(',').Select(x => x.Trim()));
                        foreach (var attr in element.Attributes())
                        {
                            var an = attr.Name.LocalName;
                            if (!hs.Contains(an))
                                    continue;
                            attr.Value = SetMaxDecimals(attr.Value, 10);
                        }
                    });
                }

                
                Bitmap bref = null;
#if DEBUG
                try
                {
                    bref = GetValidationRef(xdoc);
                }
                catch
                {
                }
#endif//DEBUG
                using (bref)
                {
                    HashSet<String> namespaces = new HashSet<string>();
                    foreach (var el in xdoc.Descendants())
                    {
                        namespaces.Add(el.Name.NamespaceName);
                        foreach (var at in el.Attributes())
                        {
                            if (!at.IsNamespaceDeclaration)
                                namespaces.Add(at.Name.NamespaceName);
                        }
                    }
                    foreach (var el in xdoc.Descendants())
                    {
                        List<XAttribute> toDelete = new List<XAttribute>();
                        foreach (var at in el.Attributes())
                        {
                            if (!at.IsNamespaceDeclaration)
                                continue;
                            if (namespaces.Contains(at.Value))
                                continue;
                            toDelete.Add(at);
                        }
                        var l = toDelete.Count;
                        while (l > 0)
                        {
                            --l;
                            toDelete[l].Remove();
                        }
                    }
                    xdoc.DocumentType?.Remove();

                    if (opt.RemoveAttributeSpaces)
                    {
                        foreach (var el in xdoc.Descendants())
                        {
                            foreach (var at in el.Attributes())
                            {
                                var value = at.Value;
                                if (value != null)
                                {
                                    value = value.Replace(", ", ",");
                                    for (; ; )
                                    {
                                        var p = value;
                                        value = value.Replace("  ", " ");
                                        if (p == value)
                                            break;
                                    }
                                    at.Value = value;
                                }
                            }
                        }
                        Validate(bref, xdoc);
                    }

                    if (opt.RemoveTitle)
                    {
                        List<XElement> els = new List<XElement>();
                        foreach (var el in xdoc.Descendants())
                        {
                            if (el.Name.LocalName == "title")
                                els.Add(el);
                        }
                        var ec = els.Count;
                        while (ec > 0)
                        {
                            --ec;
                            els[ec].Remove();
                        }
                    }
                    if (opt.RemoveMetadata)
                    {
                        List<XElement> els = new List<XElement>();
                        foreach (var el in xdoc.Descendants())
                        {
                            if (el.Name.LocalName == "metadata")
                                els.Add(el);
                        }
                        var ec = els.Count;
                        while (ec > 0)
                        {
                            --ec;
                            els[ec].Remove();
                        }
                    }

                    OnAllElementsX(xdoc.Root, element =>
                    {
                        var style = element.Attribute(XName.Get("style"));
                        if (style == null)
                            return;
                        var value = style.Value.Split(';');
                        style.Remove();
                        foreach (var s in value)
                        {
                            var yy = s.Trim();
                            if (yy.Length <= 0)
                                continue;
                            var i = s.IndexOf(':');
                            var key = s.Substring(0, i).TrimEnd();
                            var val = s.Substring(i + 1).TrimStart();
                            if (key.StartsWith("-"))
                                continue;
                            try
                            {
                                element.SetAttributeValue(XName.Get(key), val);
                            }
                            catch
                            {
                            }
                        }
                    });
                    Validate(bref, xdoc);

                    if (opt.ShortenColors)
                    {
                        var colA = ColorAttributes;
                        OnAllElementsX(xdoc.Root, element =>
                        {
                            foreach (var attr in element.Attributes())
                            {
                                if (!colA.Contains(attr.Name.LocalName.FastToLower()))
                                    continue;
                                var color = attr.Value;
                                color = HtmlColors.GetShortest(color);
                                attr.Value = color;
                            }
                        });
                        Validate(bref, xdoc);
                    }

                    if (opt.RemoveVersion)
                    {
                        OnAllElementsX(xdoc.Root, element =>
                        {
                            if (element.Name.LocalName.FastToLower() != "svg")
                                return;
                            var attr = element.Attribute(XName.Get("version"));
                            if (attr == null)
                                return;
                            attr.Remove();
                        });
                    }

                    if (opt.TrimTrailingZeros)
                    {
                        OnAllElementsX(xdoc.Root, element =>
                        {
                            foreach (var attr in element.Attributes())
                            {
                                var val = attr.Value;
                                val = TrimTrailingZeros(val);
                                attr.Value = val;
                            }
                        });
                        Validate(bref, xdoc);
                    }

                    if (opt.TrimLeadingZeros)
                    {
                        OnAllElementsX(xdoc.Root, element =>
                        {
                            var elName = element.Name.LocalName;
                            bool isPath = elName == "path";
                            if (DecimalLimits.TryGetValue(elName, out var a))
                            {
                                foreach (var t in a.Split(','))
                                {
                                    var attrName = t.Trim();
                                    var attr = element.Attribute(XName.Get(attrName));
                                    if (attr == null)
                                        continue;
                                    var org = attr.Value;
                                    var val = TrimLeadingZeros(org, isPath && (attrName == "d"));
                                    if (val != org)
                                        attr.Value = val;
                                }
                            }
                        });
                        Validate(bref, xdoc);
                    }

                    if (opt.ShortenPath)
                    {
                        OnAllElementsX(xdoc.Root, element =>
                        {
                            if (element.Name.LocalName.FastToLower() != "path")
                                return;
                            var attr = element.Attribute(XName.Get("d"));
                            if (attr == null)
                                return;
                            var org = attr.Value;
                            var path = OptimizePath(org);
                            if (org != path)
                                attr.Value = path;
                        });
                        Validate(bref, xdoc);
                    }

                    if (opt.TrimTransform)
                    {
                        OnAllElementsX(xdoc.Root, element =>
                        {
                            var attr = element.Attribute(XName.Get("transform"));
                            if (attr == null)
                                return;
                            var org = attr.Value;
                            var val = FixTransform(org, decimalLimit);
                            if (org != val)
                            {
                                attr.Value = val;
                                Validate(bref, xdoc);
                            }
                        });
                        Validate(bref, xdoc);
                    }

                }



                if (opt.RemoveTextNodes)
                {
                    xdoc.DescendantNodes().OfType<XText>().Where(x => String.IsNullOrEmpty(x.Value?.Trim())).Remove();
                }


                String text;
                using (var tw = new StringWriter())
                {
                    xdoc.Save(tw, SaveOptions.OmitDuplicateNamespaces | (opt.RemoveWhiteSpaces ? SaveOptions.DisableFormatting : SaveOptions.None));
                    text = tw.ToString();
                }

                int pos = -1;
                if (opt.RemoveDocType)
                {
                    pos = text.IndexOf("?>", StringComparison.Ordinal);
                    if (pos >= 0)
                        pos += 2;
                }
                else
                {
                    pos = text.IndexOf("<?", StringComparison.Ordinal);
                }
                if (pos < 0)
                    pos = 0;
                text = text.Substring(pos);
                if (opt.TrimTagClose)
                    text = text.Replace(" />", "/>");
                File.WriteAllText(destFile, text);
            }
            catch (Exception ex)
            {
                msg.AddMessage("Failed to read or process svg, using a copy of the original", ex, MessageLevels.Warning);
                Copy(sourceFile, destFile);
            }
            return 0;
        }


        static Tuple<Dictionary<String, int>, List<XAttribute>> GetIds(XDocument xdoc)
        {
            var idHist = new Dictionary<String, int>();
            var allIds = new List<XAttribute>();
            OnAllElementsX(xdoc.Root, element =>
            {
                foreach (var attr in element.Attributes())
                {
                    if (!TryReadId(attr.Name.LocalName, attr.Value, out var id))
                        continue;
                    allIds.Add(attr);
                    idHist.TryGetValue(id, out var c);
                    ++c;
                    idHist[id] = c;
                }
            });
            return Tuple.Create(idHist, allIds);
        }

        static bool TryReadId(String attr, String value, out String id)
        {
            value = value.Trim();
            if (attr == "id")
            {
                id = value;
                return true;
            }
            if (attr == "href")
            {
                if (value.StartsWith("#"))
                {
                    id = value.Substring(1);
                    return true;
                }
                id = null;
                return false;
            }
            if (value.StartsWith("url(#"))
            {
                if (value.EndsWith(')'))
                {
                    id = value.Substring(5, value.Length - 6).Trim();
                    return true;
                }
            }
            id = null;
            return false;
        }


        static readonly HashSet<String> Keep = new HashSet<string>()
        {
            "fill", "stroke",
        };

        static void RemoveId(XAttribute x)
        {
            var name = x.Name.LocalName.Trim();
            if (Keep.Contains(name))
            {
                //x.Value = "href(#)";
                return;
            }
            x.Remove();
        }

        static String FormatNewId(String attr, String newId)
        {
            if (attr == "id")
                return newId;
            if (attr == "href")
                return "#" + newId;
            return String.Join(newId, "url(#", ')');
        }


        sealed class SvgMeta
        {
            public long SrcSize { get; set; }
            public long Size { get; set; }
            public string Filename { get; set; }
        }

        static bool Copy(String source, String dest)
        {
            var fs = new FileInfo(source);
            var fd = new FileInfo(dest);
            if (fd.Exists)
            {
                if (fs.Length == fd.Length)
                    if (fs.LastWriteTimeUtc == fd.LastWriteTimeUtc)
                        return false;
            }
            File.Copy(source, dest, true);
            fd = new FileInfo(dest);
            fd.LastWriteTimeUtc = fs.LastWriteTimeUtc;
            return true;
        }

        static async Task<int> ProcessFile(IMessageHost msg, SvgOptParams opt, String sourceFile, String relSource, String destFolder)
        {
            var filename = Path.GetFileName(sourceFile);
            var destFilename = Path.Combine(destFolder, filename);
            var suffix = opt.CacheKey;
            bool didProcces = false;
            var meta = await FileMetaData.ProcessAsync<SvgMeta>("SvgOptMeta", sourceFile, (file, baseName, data) =>
            {
                //  If we have some cached data
                if (data != null)
                {
                    //  Validate the data and if ok use it
                    var fi = new FileInfo(data.Filename);
                    if (fi.Exists && (fi.Length == data.Size))
                        return null;
                }
                //  Need to create some new data
                var dest = baseName + ".svg";
                data = new SvgMeta
                {
                    Filename = dest,
                };
                data.SrcSize = new FileInfo(sourceFile).Length;
                var r = Optimize(sourceFile, dest, msg, opt);
                if (r == 0)
                {
                    data.Size = new FileInfo(dest).Length;
                }
                else
                {
                    data.Size = -1;
                }
                didProcces = true;
                return data;
            }, 30, suffix);
            if (meta.Size >= 0)
            {
                msg.AddMessage(String.Concat(filename.ToQuoted(), didProcces ? " [processed] " : " [cached] ", meta.SrcSize, " => ", meta.Size, " [", ((100M * meta.Size) / meta.SrcSize).ToString("0.00", CultureInfo.InvariantCulture), "%]"));
                Copy(meta.Filename, destFilename);
            }
            else
            {
                msg.AddMessage(String.Concat(filename.ToQuoted(), " [failed!]"));
            }
            return 0;
        }


        static readonly String[] TestZero =
        [
            "22", "22",
            "1.00023", "1.00023",
            "3.0002300", "3.00023",
            "10.500", "10.5",
            "13.0", "13",
            "0.0" , "0",
            "0.97", ".97",
            ".10" , ".1",
            ".0" , "0",
            "-0.0", "0",
            "-0.01", "-.01",
            "-.000" , "0",
            "04", "4",
            "005.0", "5",
            "000", "0",
            "000.000", "0",
            "-000", "0",
            "-000.000", "0",
        ];

        
        static async Task<int> Main(string[] args)
        {
            CompGZipNET.Register();
            /*

            Func<String, String> method = s => TrimLeadingZeros(TrimTrailingZeros(s));

            var l = TestZero.Length;
            for (int i = 0; i < l; i += 2)
            {
                var s = TestZero[i];
                var d = TestZero[i + 1];
                var g = method(s);
                if (g != d)
                {
                    method(s);
                    throw new Exception();
                }
                s = "Apa" + s;
                d = "Apa" + d;
                g = method(s);
                if (g != d)
                {
                    method(s);
                    throw new Exception();
                }
                s += s;
                d += d;
                g = method(s);
                if (g != d)
                {
                    method(s);
                    throw new Exception();
                }

                s += "z";
                d += "z";
                g = method(s);
                if (g != d)
                {
                    method(s);
                    throw new Exception();
                }

            }
            */

            //var c = OptimizePath("M2400 0h4800v4800H2400zm2490 4430l-45-863a95 95 0 01111-98l859 151-116-320a65 65 0 0120-73l941-762-212-99a65 65 0 01-34-79l186-572-542 115a65 65 0 01-73-38l-105-247-423 454a65 65 0 01-111-57l204-1052-327 189a65 65 0 01-91-27l-332-652-332 652a65 65 0 01-91 27l-327-189 204 1052a65 65 0 01-111 57l-423-454-105 247a65 65 0 01-73 38l-542-115 186 572a65 65 0 01-34 79l-212 99 941 762a65 65 0 0120 73l-116 320 859-151a95 95 0 01111 98l-45 863z");
            //var c = OptimizePathArcs("M3 1.052zm-.718.157a2.26 2.26 0 0 0-.055.023l.06.935c.041.489.36.773.713.885.354-.112.672-.396.714-.885l.059-.932a1.362 1.362 0 0 0-.055-.026l-.056.927a.947.947 0 0 1-.662.86.947.947 0 0 1-.662-.86z");
            //var c = OptimizePath("2 .4 2.7");


            int ret;
            SvgOptParams p = null;
            int Setup(SvgOptParams x)
            {
                p = x;
                return 0;
            }

            List<Tuple<String, String>> allFiles = new List<Tuple<string, string>>();
            String dest = null;
            Task<int> proc(IMessageHost msg, SvgOptParams opt, String sourceFile, String relSource, String destFolder)
            {
                lock (allFiles)
                {
                    allFiles.Add(Tuple.Create(sourceFile, Path.Combine(destFolder, relSource)));
                    if ((dest == null) || (destFolder.Length < dest.Length))
                        dest = destFolder;
                }
                return ProcessFile(msg, opt, sourceFile, relSource, destFolder);
            }

#if DEBUG
            ret = await FilesToFolderTool.OnFilesAsync<SvgOptParams>(args, proc, Setup).ConfigureAwait(false);
#else//DEBUG
            ret = await FilesToFolderTool.OnFilesParallelAsync<SvgOptParams>(args, proc, Setup).ConfigureAwait(false);
#endif//DEBUG 
            var c = p.Compare;
            if (c != null) 
            {
                var e = Path.GetExtension(p.Compare)?.FastToLower();
                if ((e != ".htm") || (e != ".html"))
                    c += ".html";
                if (String.IsNullOrEmpty(Path.GetDirectoryName(c)))
                    c = Path.Combine(dest, c);
                c = new FileInfo(c).FullName;
                ImageCompareGallery.Create(c, allFiles.OrderBy(x => x.Item1));
                Console.WriteLine("Save image comparsion file to " + c.ToFilename());
            }
            return ret;
        }
    }


}
