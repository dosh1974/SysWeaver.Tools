using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;

namespace SwWeb.Tests
{
    /// <summary>
    /// The rule families of series 4 round 3 (UxFamilies.cs): security (secrets in storage, cookies, url queries and logs; raw html of
    /// user data), formatting (numbers / dates by hand, concatenated translations), pointer events other than click, silent failures,
    /// untranslated error texts, the Sw api misuse of round 3, the weaker danger confirmation and the "no promise" error. Each family has
    /// mistakes in several spellings and right patterns next to them.
    /// </summary>
    public class UxFamilyTests
    {
        const String Header = "/** @file test */\r\n";

        static List<Finding> Js(String code, String rel = "page/x.js")
        {
            var f = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext(rel, new SourceText(Header + code), new CheckOptions { Design = true }, f));
            return f.Where(x => (x.Rule.Guide != null) || (x.Rule.Id == "tr-untranslated")).ToList();
        }

        static List<Finding> Css(String code)
        {
            var f = new List<Finding>();
            Checker.CheckCssFile(new Checker.FileContext("page/x.css", new SourceText(code), new CheckOptions { Design = true }, f));
            return f.Where(x => x.Rule.Guide != null).ToList();
        }

        static String[] Ids(List<Finding> f) => f.Select(x => x.Rule.Id).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();

        [Theory]
        //  SEC-1: secrets in storage, cookies, url queries, logs
        [InlineData("function keep(apiKey) { localStorage.setItem(\"app.key\", apiKey); }", "ux-sec-1")]
        [InlineData("function keep(k) { sessionStorage[\"refresh_token\"] = k; }", "ux-sec-1")]
        [InlineData("function keep(k) { Sw.Storage.Local.Set(\"x.apiKey\", k); }", "ux-sec-1")]
        [InlineData("function keep(k) { document.cookie = \"authToken=\" + k; }", "ux-sec-1")]
        [InlineData("function go(t) { return Sw.Net.Get(\"../Api/x/Verify?access_token=\" + t); }", "ux-sec-1")]
        [InlineData("function go(s) { window.open(\"Export.csv?s=\" + s.SessionId); }", "ux-sec-1")]
        [InlineData("function go(t) { return Sw.Net.Get(`../Api/x/Verify?apiKey=${t}`); }", "ux-sec-1")]
        [InlineData("function f(s) { console.info(\"ready\", s.RefreshToken); }", "ux-sec-1")]
        [InlineData("const U = { trace: (...a) => 0 };\r\nfunction f(user) { U.trace(\"pw\", user.Password); }", "ux-sec-1")]
        //  SEC-2: raw html of user data
        [InlineData("function f(el, item) { el.innerHTML = \"<b>\" + item.Name + \"</b>\"; }", "ux-sec-2")]
        [InlineData("function f(el, m) { el.insertAdjacentHTML(\"beforeend\", `<li>${m.Text}</li>`); }", "ux-sec-2")]
        [InlineData("function f(el, d) { el.innerHTML = d.Notes; }", "ux-sec-2")]
        [InlineData("function rowHtml(d) { return \"<span>\" + d.Owner + \"</span>\"; }", "ux-sec-2")]
        [InlineData("function f(el, d) { const html = \"<i>\" + d.Title + \"</i>\"; el.innerHTML = html; }", "ux-sec-2")]
        //  TXT-20: numbers, sizes, dates by hand
        [InlineData("function size(b) { return (b / 1048576).toFixed(1) + \" MB\"; }", "ux-txt-20")]
        [InlineData("function pct(p) { return `${Math.round(p * 100)}%`; }", "ux-txt-20")]
        [InlineData("function t(d) { return d.getHours() + \":\" + d.getMinutes(); }", "ux-txt-20")]
        [InlineData("function t(d) { return d.toLocaleDateString(\"en-GB\"); }", "ux-txt-20")]
        [InlineData("function t(v) { return new Intl.NumberFormat(undefined, { style: \"percent\" }).format(v); }", "ux-txt-20")]
        //  TXT-6: concatenated translations, plural "s"
        [InlineData("function f(el, n) { el.textContent = _TF(\"Removed\", \"c\") + \" \" + n + \" \" + _TF(\"items\", \"c\"); }", "ux-txt-6")]
        [InlineData("function f(n) { return n + \" \" + _TF(\"device\", \"c\") + (n === 1 ? \"\" : \"s\"); }", "ux-txt-6")]
        [InlineData("function f(v) { return `${_TF(\"Last seen\", \"c\")} ${v}`; }", "ux-txt-6")]
        //  FBK-5: silent failures, a toast after an auto-saving control, Report twice, close then fail
        [InlineData("new Sw.Button({ Text: t, OnClick: async () => { try { await Sw.Net.Send(\"../Api/x/Recount\", 1); } catch (e) { console.error(e); } } });", "ux-fbk-5")]
        [InlineData("async function load() { let a = []; try { a = await Sw.Net.Get(\"../Api/x/GetList\"); } catch (e) { console.error(\"failed\", e); } return a; }", "ux-fbk-5")]
        [InlineData("function f(id) { Sw.Net.Send(\"../Api/x/SetGroup\", id).catch(() => {}); }", "ux-fbk-5")]
        [InlineData("function f(id) { Sw.Net.Get(\"../Api/x/GetInfo\").catch(console.error); }", "ux-fbk-5")]
        [InlineData("const b = Sw.el(\"button\", { text: t });\r\nb.addEventListener(\"click\", () => { Sw.Net.Send(\"../Api/x/Ping\", 1); });", "ux-fbk-5")]
        [InlineData("setInterval(() => Sw.Net.Get(\"../Api/x/Status\").then(show), 5000);", "ux-fbk-5")]
        [InlineData("const s = new Sw.Toggle({ Label: l, OnChange: async v => { await Sw.Net.Send(\"../Api/x/SetMail\", v); Sw.Toast(_TF(\"Preference updated\", \"c\"), { Kind: \"ok\" }); } });", "ux-fbk-5")]
        [InlineData("async function f(sc, res) { const out = Sw.Work.Outcome(sc, res); Sw.Work.Report(out, { Name: n }); Sw.Work.Report(out, { Name: n }); }", "ux-fbk-5")]
        [InlineData("Sw.Dialog.Open({ Title: t, Buttons: [{ Text: c, Value: null }, { Text: r, Value: 1, OnClick: async close => { close(1); await Sw.Net.Send(\"../Api/x/Rename\", 1); } }] });", "ux-fbk-5")]
        //  INP-1 / 6 / 8 / 20: pointer events other than click
        [InlineData("function f(li, d) { li.addEventListener(\"mousedown\", () => select(d)); }\r\nfunction select(d) { return d; }", "ux-inp-1")]
        [InlineData("function f(li, g) { li.addEventListener(\"pointerup\", () => filter(g)); }\r\nfunction filter(g) { return g; }", "ux-inp-1")]
        [InlineData("function f(li, d) { li.addEventListener(\"dblclick\", () => open(d)); }\r\nfunction open(d) { return d; }", "ux-inp-6")]
        [InlineData("function f(li, d) { li.addEventListener(\"mouseenter\", () => Sw.Net.Send(\"../Api/x/MarkSeen\", d.Id)); }", "ux-inp-8")]
        [InlineData("function f(li, g) { li.onmouseover = () => Sw.Menu.Show(li, items); }", "ux-inp-8")]
        [InlineData("function f(li, d) { li.draggable = true; li.addEventListener(\"drop\", e => move(e.dataTransfer.getData(\"text/plain\"), d.Id)); }\r\nfunction move(a, b) { return Sw.Net.Send(\"../Api/x/Move\", { a, b }); }", "ux-inp-20")]
        //  INP-23: a single key shortcut on the document
        [InlineData("document.addEventListener(\"keydown\", e => { if (e.key === \"r\") reload(); });", "ux-inp-23")]
        //  FRM-3: an error set while typing
        [InlineData("const m = new Sw.TextBox({ Label: l, OnInput: v => m.SetError(ok(v) ? null : _TF(\"Enter a valid e-mail address.\", \"c\")) });", "ux-frm-3")]
        //  Sw api misuse (round 3)
        [InlineData("const ed = Sw.el(\"div\", { attrs: { contenteditable: \"true\" } });\r\nconst h = Sw.Draft.Attach(ed, { Context: \"x\" }); h.Clear();", "ux-saf-11")]
        [InlineData("Sw.Activity.Run({ Key: k, Title: t }, async (signal, job) => { for (let i = 0; i < 6; ++i) await wait(500); });", "ux-fbk-4")]
        [InlineData("Sw.Activity.Run({ Key: k, Title: t }, async (signal, job) => { while (more()) { await Sw.Util.DelayAbort(500, signal); } });", "ux-fbk-4")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => Sw.Activity.Start({ Key: k, Title: t }) });", "ux-fbk-2")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => Sw.Net.Send(\"../Api/x/SaveName\", 1, { Detached: true }) });", "ux-fbk-2")]
        [InlineData("Sw.Dialog.Open({ Title: t, Buttons: [{ Text: c, Value: null }, { Text: x, Value: 1, Cancellable: true, OnClick: (c, v, { Signal }) => Sw.Net.Send(\"../Api/x/Transfer\", 1, { Signal }) }] });", "ux-pop-6")]
        [InlineData("const tb = new Sw.TextBox({ Label: l, Multiline: true, Draft: \"m\" });\r\nconst f = Sw.Form({ Fields: [{ Name: \"M\", Control: tb, Label: l }], Submit: s });", "ux-saf-11")]
        [InlineData("Sw.Keys.Add(\"Ctrl+S\", a, { Title: t });\r\nSw.Keys.Add(\"Mod+S\", b, { Title: t });", "ux-inp-23")]
        //  S4R3 requests of the other fixers
        [InlineData("function f(job) { job.CancelFn(); }", "ux-fbk-4")]
        [InlineData("function f(id, j) { Sw.Net.Send(\"../Api/jobs/CancelJob\", id); j.Text = _TF(\"Cancelled\", \"c\"); }", "ux-fbk-4")]
        [InlineData("async function f() { try { await save(); } catch (e) { Sw.Fail(e.message); } }", "ux-txt-30")]
        [InlineData("function f(e) { return Sw.Net.ErrorText(e).Text + \" \" + _TF(\"Try again later.\", \"c\"); }", "ux-txt-30")]
        [InlineData("class L { Start() { Sw.Keys.Add(\"/\", () => this.Find(), { Title: t }); } }", "ux-inp-23")]
        [InlineData("async function del(r) { try { await Sw.Net.Send(\"../Api/x/DeleteRow\", r); } catch (e) { if (e.Status === 409) Sw.Fail(_TF(\"Changed: reload the page.\", \"c\")); } }", "ux-fbk-5")]
        [InlineData("function f(n) { Sw.Toast(_T(\"{0} is ready\", n + FileStamp() + \".csv\", \"c\")); }", "ux-txt-17")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => Sw.Net.Send(\"../Api/x/Store\", { N: name.Get() }, { Detached: true }) });", "ux-fbk-2")]
        [InlineData("const name = new Sw.TextBox({ Label: l });\r\nconst go = new Sw.Button({ Text: _TF(\"Done\", \"c\"), OnClick: () => Sw.Net.Send(\"../Api/x/SetName\", name.Get()) });\r\npage.Add(name, go);", "ux-saf-10")]
        [InlineData("Sw.Dialog.Open({ Title: t, EditWhileBusy: true, Buttons: [{ Text: s, Value: 1, OnClick: async () => { const v = name.Get(); await Sw.Net.Send(\"../Api/x/SaveName\", v); } }] });", "ux-frm-5")]
        //  SAF-4: a danger confirmation that falls back to a weaker one
        [InlineData("async function del(u) { let info = null; try { info = await Sw.Net.Send(\"../Api/x/GetToken\", null); } catch (e) { info = null; }\r\n const ok = await Sw.Cells.ConfirmDanger(info ? { Title: a, Confirm: w } : { Title: a }); }", "ux-saf-4")]
        [InlineData("async function del(u) { try { await prepare(); await Sw.Cells.ConfirmDanger({ Title: a, Confirm: w }); } catch (e) { await Sw.Dialog.Confirm(a, b, c); } }", "ux-saf-4")]
        public void MistakeIsFound(String code, String rule)
        {
            var f = Js(code);
            Assert.True(f.Any(x => x.Rule.Id == rule), rule + " not found in: " + code + "\r\nfound: " + String.Join(", ", Ids(f)));
        }

        [Theory]
        [InlineData("function f(el, d) { el.textContent = d.Notes; el.append(Sw.el(\"b\", { text: d.Name })); }")]
        [InlineData("function f(el, s) { el.innerHTML = Sw.Format.JsonToHtml(s); }")]
        [InlineData("function f(el) { el.innerHTML = \"<b>static</b>\"; }")]
        [InlineData("function f(b) { return Sw.Format.Bytes(b); }")]
        [InlineData("function f(n) { return _T(\"Removed {0} items\", n, \"c\"); }")]
        [InlineData("function f(e) { Sw.Fail(_TF(\"Failed to save.\", \"c\") + \"\\n\" + e.message); }")]
        [InlineData("function f(l) { return _TF(\"Name\", \"c\") + \": \" + l.Name; }")]
        [InlineData("function f(el, p) { el.style.width = Math.round(p * 100) + \"%\"; }")]
        [InlineData("function f(d) { return d.toLocaleString([], { dateStyle: \"medium\" }); }")]
        [InlineData("function stamp(n) { return n.getFullYear() + \"-\" + p2(n.getMonth() + 1); }")]
        [InlineData("new Sw.Button({ Text: t, OnClick: async () => { try { await Sw.Net.Send(\"../Api/x/Save\", 1); } catch (e) { Sw.Fail(e); } } });")]
        [InlineData("async function probe() { let ok = false; try { ok = await Sw.Net.Get(\"../Api/x/HaveIt\"); } catch { } return ok; }")]
        [InlineData("function f(h, li, d) { h.addEventListener(\"pointerdown\", ev => startDrag(ev, li)); }\r\nfunction startDrag(ev, li) { st = { Y: ev.clientY }; }")]
        [InlineData("function f(li) { li.addEventListener(\"click\", ev => { if (Sw.BadClick(ev)) return; open(li); }); li.addEventListener(\"dblclick\", () => open(li)); }\r\nfunction open(li) { return li; }")]
        [InlineData("function f(li, d) { li.addEventListener(\"mouseenter\", () => Sw.Net.Get(\"../Api/x/GetPreview\").then(show)); }")]
        [InlineData("document.addEventListener(\"keydown\", e => { if (e.ctrlKey && e.key === \"k\") find(); });")]
        [InlineData("Sw.Keys.Add(\"R\", reload, { Title: t });")]
        [InlineData("const m = new Sw.TextBox({ Label: l, OnInput: v => m.SetError(null) });")]
        [InlineData("Sw.Activity.Run({ Key: k, Title: t }, async (signal, job) => { await Sw.Net.Send(\"../Api/x/Run\", 1, { Signal: signal }); });")]
        [InlineData("async function w(s) { await Sw.Util.DelayAbort(5000, this.Wake); }")]
        [InlineData("const s = new Sw.Toggle({ Label: l, OnChange: v => Sw.Net.Send(\"../Api/x/SetMail\", v) });")]
        [InlineData("function f(u) { console.log(\"size: \" + pw + \"x\" + ph); }")]
        [InlineData("Sw.Dialog.Open({ Title: t, Buttons: [{ Text: c, Value: null }, { Text: x, Value: 1, Cancellable: true, OnClick: (c, v, { Signal }) => Sw.Net.Send(\"../Api/x/Search\", 1, { Signal, ReadOnly: true }) }] });")]
        [InlineData("Sw.Dialog.Open({ Title: t, EditWhileBusy: true, Buttons: [{ Text: s, Value: 1, OnClick: async () => { await Sw.Net.Send(\"../Api/x/SaveName\", name.Get()); if (name.Get() !== sent) await again(); } }] });")]
        public void RightPatternIsLeftAlone(String code)
        {
            var f = Js(code).Where(x => x.Rule.Id != "tr-untranslated").ToList();
            Assert.Empty(f.Select(x => x.Rule.Id + ": " + x.Message));
        }

        [Fact]
        public void UntranslatedFailTextsAndErrors()
        {
            Assert.Contains(Js("function f() { Sw.Fail(\"Error: key rejected\"); }"), x => x.Rule.Id == "tr-untranslated");
            Assert.Contains(Js("new Sw.Button({ Text: _TF(\"x\", \"c\"), OnClick: async () => { throw new Error(\"Name already used\"); } });"), x => x.Rule.Id == "tr-untranslated");
            Assert.Contains(Js("async function f() { try { throw new Error(\"Device is offline\"); } catch (e) { Sw.Fail(e); } }"), x => x.Rule.Id == "tr-untranslated");
            Assert.DoesNotContain(Js("function f(el) { el.style.cssText = \"color: red; margin: 0\"; }"), x => x.Rule.Id == "tr-untranslated");
        }

        [Fact]
        public void NoPromiseIsAnErrorAlsoThroughAPageMethod()
        {
            var f = Js("class P { async Delete(u) { await Sw.Net.Send(u, null); } Start() { const t = this; new Sw.Button({ Text: x, OnClick: () => { t.Delete(u); } }); } }");
            var e = f.Where(x => x.Rule.Id == "ux-fbk-2").ToList();
            Assert.NotEmpty(e);
            Assert.All(e, x => Assert.Equal(Severity.Error, x.Severity));
        }

        [Fact]
        public void NoPromiseIsCheckedInFrameworkFilesWithoutFrameworkOption()
        {
            var f = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext("ui/x.js", new SourceText(Header + "new Sw.Button({ Text: x, OnClick: () => { Sw.Net.Send(\"../Api/x/Save\", 1); } });\r\nlocalStorage.setItem(\"k.apiKey\", k);"), new CheckOptions { Design = true }, f));
            Assert.Equal(new[] { "ux-fbk-2" }, f.Where(x => x.Rule.Guide != null).Select(x => x.Rule.Id).Distinct().ToArray());
        }

        [Theory]
        [InlineData(".a { animation: p 1.2s ease-in-out infinite; }\r\n@keyframes p { from { opacity: 1; } to { opacity: .2; } }", "ux-mot-6")]
        [InlineData(".tag { display: inline-block; width: 72px; white-space: nowrap; overflow: hidden; }", "ux-txt-17")]
        [InlineData(".app-statusline { user-select: none; }", "ux-inp-30")]
        public void CssMistakeIsFound(String css, String rule)
        {
            Assert.Contains(Css(css), x => x.Rule.Id == rule);
        }

        [Theory]
        [InlineData(".spin { animation: r 1s linear infinite; }\r\n@keyframes r { to { transform: rotate(360deg); } }")]
        [InlineData(".a { animation: p 1s infinite; }\r\n@keyframes p { to { opacity: .2; } }\r\n@media (prefers-reduced-motion: reduce) { .a { animation: none; } }")]
        [InlineData(".tag { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }")]
        [InlineData(".sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }")]
        [InlineData(".toolbar-btn { user-select: none; }\r\nbody { user-select: none; }")]
        public void CssRightPatternIsLeftAlone(String css)
        {
            Assert.Empty(Css(css).Where(x => x.Rule.Id == "ux-mot-6" || x.Rule.Id == "ux-txt-17" || x.Rule.Id == "ux-inp-30" || x.Rule.Id == "ux-mot-2").Select(x => x.Rule.Id + ": " + x.Message));
        }
    }
}
