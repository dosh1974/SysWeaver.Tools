using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;

namespace SwWeb.Tests
{
    /// <summary>
    /// The usage pattern rules (UxChecker.CheckPatterns): the misuse of the framework APIs that runs without an error and breaks a guide
    /// rule silently. Each rule: the bad pattern is found, the guide's way is left alone, the allow comment works.
    /// </summary>
    public class UxPatternTests
    {
        const String Header = "/** @file test */\r\n";

        static List<Finding> Js(String code, String rel = "page/x.js")
        {
            var f = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext(rel, new SourceText(Header + code), new CheckOptions { Design = true }, f));
            return f.Where(x => (x.Rule.Guide != null) || (x.Rule == Rule.Syntax)).ToList();
        }

        static List<Finding> Css(String code)
        {
            var f = new List<Finding>();
            Checker.CheckCssFile(new Checker.FileContext("page/x.css", new SourceText(code), new CheckOptions { Design = true }, f));
            return f.Where(x => x.Rule.Guide != null).ToList();
        }

        static String[] Ids(List<Finding> f) => f.Select(x => x.Rule.Id).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();

        [Theory]
        //  FBK-2: a block body that starts a request and returns nothing
        [InlineData("new Sw.Button({ Text: _TF(\"Save\", \"c\"), OnClick: () => { Sw.Net.Send(\"../Api/x/Save\", 1).then(() => go()); } });", "ux-fbk-2")]
        [InlineData("new Sw.Button({ Text: _TF(\"Save\", \"c\"), OnClick: function () { fetch(\"../Api/x\"); } });", "ux-fbk-2")]
        [InlineData("new Sw.Button({ Text: _TF(\"Save\", \"c\"), OnClick: async () => { save().then(done); } });", "ux-fbk-2")]
        //  SAF-3: a danger button with a plain Confirm (and the positional OK of POP-9)
        [InlineData("new Sw.Button({ Text: _TF(\"Delete all\", \"c\"), Kind: \"danger\", OnClick: async () => { if (await Sw.Dialog.Confirm(_TF(\"Clear the list?\", \"c\"), _TF(\"t\", \"c\"), _TF(\"Clear\", \"c\"), { Safe: true })) return Sw.Net.Send(\"../Api/x/Clear\"); } });", "ux-saf-3")]
        [InlineData("Sw.Dialog.Confirm(_TF(\"Restart?\", \"c\"), _TF(\"t\", \"c\"));", "ux-pop-9")]
        [InlineData("Sw.Dialog.Open({ Id: \"edit\", Title: a }); Sw.Dialog.Show({ Id: \"edit\", Title: b });", "ux-pop-7")]
        [InlineData("const pop = Sw.el(\"div.sw-popup\"); document.body.append(pop); close.onclick = () => pop.remove();", "ux-mot-1")]
        [InlineData("const tip = document.createElement(\"div\"); tip.setAttribute(\"role\", \"tooltip\"); setTimeout(() => tip.remove(), 900);", "ux-mot-1")]
        [InlineData("Sw.Dialog.Show({ Title: t, Buttons: [{ Text: _TF(\"Cancel\", \"c\") }, { Text: _TF(\"Save\", \"c\"), OnClick: async () => { await check(); await Sw.Net.Send(\"../Api/x/GetRows\", 1); } }] });", "ux-pop-6")]
        [InlineData("Sw.Dialog.Alert(_TF(\"Done\", \"c\"), _TF(\"Done\", \"c\"), _TF(\"OK\", \"c\"));", "ux-pop-9")]
        //  SAF-1: a destructive request without a question
        [InlineData("del.addEventListener(\"click\", () => Sw.Net.Send(\"../Api/mail/DeleteMail\", id));", "ux-saf-1")]
        [InlineData("new Sw.Button({ Text: _TF(\"Kill\", \"c\"), OnClick: () => Sw.Net.Send(api + \"KillProcess\", pid) });", "ux-saf-1")]
        //  INP-2: click on a card / row / div without the modifier check
        [InlineData("const card = Sw.Card({ Title: _TF(\"T\", \"c\") }); card.E.addEventListener(\"click\", () => open());", "ux-inp-2")]
        [InlineData("const tr = document.createElement(\"tr\"); tr.addEventListener(\"click\", () => open(tr));", "ux-inp-2")]
        [InlineData("list.addEventListener(\"click\", ev => { const li = ev.target.closest(\"li\"); if (li) open(li); });", "ux-inp-2")]
        [InlineData("rowEl.onclick = () => open();", "ux-inp-2")]
        //  INP-12: native disabled on a button variable, the shim without a reason, no reasons
        [InlineData("const save = document.createElement(\"button\"); save.disabled = true;", "ux-inp-12")]
        [InlineData("const b = new Sw.Button({ Text: _TF(\"S\", \"c\") }); b.E.setAttribute(\"disabled\", \"\");", "ux-inp-12")]
        [InlineData("const b = new Sw.Button({ Text: _TF(\"S\", \"c\") }); b.E.disabled = true;", "ux-inp-12")]
        [InlineData("new Sw.Button({ Text: _TF(\"Go\", \"c\"), Disabled: true });", "ux-inp-12")]
        [InlineData("b.SetDisabled(!ready);", "ux-inp-12")]
        [InlineData("Sw.Disabled.Set(el, true);", "ux-inp-12")]
        //  LST-3: hand-made tables
        [InlineData("const t = document.createElement(\"table\");", "ux-lst-3")]
        [InlineData("const t = Sw.el(\"table.my-list\");", "ux-lst-3")]
        [InlineData("host.innerHTML = \"<table><tr><td>\" + x + \"</td></tr></table>\";", "ux-lst-3")]
        //  SAF-10: own beforeunload, a key never cleared
        [InlineData("window.onbeforeunload = () => \"x\";", "ux-saf-10")]
        [InlineData("window.addEventListener(\"beforeunload\", e => e.preventDefault());", "ux-saf-10")]
        [InlineData("Sw.Page.SetDirty(\"form\", true);", "ux-saf-10")]
        [InlineData("Sw.Page.SetDirty(\"form\");", "ux-saf-10")]
        //  SAF-11: own drafts, dropped handles, a handle never cleared
        [InlineData("localStorage.setItem(\"my.draft\", ta.value);", "ux-saf-11")]
        [InlineData("localStorage.setItem(`comment-draft-${id}`, ta.value);", "ux-saf-11")]
        [InlineData("Sw.Draft.Attach(ta, { Context: \"c\" });", "ux-saf-11")]
        [InlineData("const d = Sw.Draft.Attach(ta, { Context: \"c\" });", "ux-saf-11")]
        //  PRF-4, INP-31, SEC-1, POP-6, FBK-4
        [InlineData("page.Add(Sw.CopyButton({}));", "ux-inp-31")]
        [InlineData("Sw.Clipboard.Copy(row.ApiKey, { Show: true });", "ux-sec-1")]
        [InlineData("Sw.CopyButton({ Value: key.Token, Show: true });", "ux-sec-1")]
        [InlineData("setInterval(() => { tbody.replaceChildren(...rows.map(r => Sw.el(\"tr\", null, r.Name))); }, 2000);", "ux-fbk-7")]
        [InlineData("a.href = \"../Api/debug/ExportTableData?q=x\";", "ux-fbk-4")]
        [InlineData("h.SetBusy(save());", "ux-pop-6")]
        [InlineData("const close = Sw.Dialog.Working({ Title: _TF(\"Exporting\", \"c\") });", "ux-fbk-4")]
        //  A top level await in a classic script
        [InlineData("const r = await Sw.Net.Send(\"../Api/x\");", "syntax")]
        public void FindsThePattern(String code, String id)
        {
            Assert.Contains(id, Ids(Js(code)));
        }

        [Theory]
        //  FBK-2: the promise is returned / awaited / an expression body; a dialog chain is not work
        [InlineData("new Sw.Button({ OnClick: () => Sw.Net.Send(\"../Api/x/Save\", 1) });")]
        [InlineData("new Sw.Button({ OnClick: () => { return Sw.Net.Send(\"../Api/x/Save\", 1).then(done); } });")]
        [InlineData("new Sw.Button({ OnClick: async () => { await Sw.Net.Send(\"../Api/x/Save\", 1); done(); } });")]
        [InlineData("new Sw.Button({ OnClick: () => { const p = Sw.Net.Send(\"../Api/x\"); Sw.Activity.Track(p, { Title: t }); return p; } });")]
        [InlineData("new Sw.Button({ OnClick: () => { Sw.Dialog.Confirm(a, b, c).then(ok => ok && go()); } });")]
        //  SAF-3 / SAF-1: Danger given, ConfirmDanger, a confirm in the handler, a non destructive api
        [InlineData("new Sw.Button({ Kind: \"danger\", OnClick: async () => { if (await Sw.Dialog.Confirm(t, x, v, { Danger: true })) return Sw.Net.Send(\"../Api/x/DeleteAll\"); } });")]
        [InlineData("new Sw.Button({ Kind: \"danger\", OnClick: () => Sw.Cells.ConfirmDanger({ Title: t }).then(ok => ok && Sw.Net.Send(\"../Api/x/DeleteAll\")) });")]
        [InlineData("new Sw.Button({ OnClick: () => Sw.Net.Send(\"../Api/x/GetDeletedItems\") });")]
        //  INP-2: modifier checks, controls, the page itself
        [InlineData("card.E.addEventListener(\"click\", ev => { if (Sw.BadClick(ev)) return; open(); });")]
        [InlineData("const tr = document.createElement(\"tr\"); tr.addEventListener(\"click\", ev => { if (!Sw.IsPureEvent(ev)) return; open(); });")]
        [InlineData("const b = document.createElement(\"button\"); b.addEventListener(\"click\", () => go());")]
        [InlineData("document.addEventListener(\"click\", e => { if (!e.target.closest(\".menu\")) close(); });")]
        [InlineData("saveBtn.addEventListener(\"click\", () => go());")]
        //  INP-12: reasons given, enabling
        [InlineData("b.SetDisabled(!ready, _TF(\"Select a row first\", \"c\"));")]
        [InlineData("b.SetDisabled(false);")]
        [InlineData("new Sw.Button({ Text: _TF(\"Go\", \"c\"), Disabled: _TF(\"Select a row first\", \"c\") });")]
        [InlineData("const opt = { Disabled: true }; grid.Columns.push(opt);")]
        [InlineData("Sw.Disabled.Set(el, true, _TF(\"Select a row first\", \"c\"));")]
        [InlineData("const b = new Sw.Button({ Text: _TF(\"S\", \"c\") }); b.E.disabled = false;")]
        //  LST-3: a captioned document table (the table view of a chart)
        [InlineData("const t = Sw.el(\"table\", null, Sw.el(\"caption\", { text: title }), body);")]
        //  SAF-10: cleared keys, toggles
        [InlineData("Sw.Page.SetDirty(\"form\", _TF(\"Your changes are lost.\", \"c\")); save().then(() => Sw.Page.SetDirty(\"form\", false));")]
        [InlineData("Sw.Page.SetDirty(\"form\", changed);")]
        [InlineData("Sw.Page.SetDirty(\"form\", true); Sw.Page.ClearDirty();")]
        //  SAF-11: Sw.Draft kept and cleared; other storage keys
        [InlineData("const d = Sw.Draft.Attach(ta, { Context: \"c\" }); send.O.OnClick = () => Sw.Net.Send(\"../Api/x/Send\", ta.value).then(() => d.Clear());")]
        [InlineData("localStorage.setItem(\"my.columns\", json);")]
        [InlineData("function edit() { const ta = new Sw.TextBox({ Label: l, Multiline: true }); Sw.Dialog.Open({ Title: t, Content: ta.E }); Sw.Draft.Attach(ta, { Context: \"c\" }); }")]
        //  PRF-4, INP-31, SEC-1, POP-6, FBK-4
        [InlineData("const off = Sw.Activity.On(e => show(e)); addEventListener(\"pagehide\", off);")]
        [InlineData("page.Add(Sw.CopyButton({ Value: id, What: _TF(\"Invoice id\", \"c\") }));")]
        [InlineData("Sw.Clipboard.Copy(row.ApiKey, { What: _TF(\"API key\", \"c\") });")]
        [InlineData("Sw.Clipboard.Copy(row.ApiKey);")]
        [InlineData("new Sw.Button({ Kind: \"danger\", OnClick: () => Sw.Danger.Run({ Action: a, Run: s => Sw.Net.Send(\"../Api/x/DeleteAll\", 1, { Signal: s }) }) });")]
        [InlineData("const edit = new Sw.Button({ OnClick: () => { const h = Sw.Dialog.Open({ Title: t }); h.SetBusy(save()); } });")]
        [InlineData("setInterval(() => refresh(), 2000);")]
        [InlineData("Sw.Page.Main(async () => {\r\n const name = new Sw.TextBox({ Label: _TF(\"Name\", \"c\") });\r\n save.O.OnClick = () => Sw.Net.Send(\"../Api/x/SaveName\", name.Get());\r\n});")]
        [InlineData("Sw.Clipboard.Copy(String(target.Get(key).Copy));")]
        [InlineData("h.SetBusy(save(), { Cancel: () => ctrl.abort() });")]
        [InlineData("h.SetBusy(false);")]
        [InlineData("h.SetBusy(({ Signal }) => Sw.Net.Send(\"../Api/x\", 1, { Signal }));")]
        [InlineData("h.SetBusy(p, { Abort: () => ctrl.abort() });")]
        [InlineData("const close = Sw.Dialog.Working({ Title: t, Work: ({ Signal }) => go(Signal) });")]
        [InlineData("const close = Sw.Dialog.Working({ Title: t, Promise: p });")]
        [InlineData("new Sw.Button({ Kind: \"danger\", OnClick: async () => { if (await Sw.Dialog.Confirm(t, x, _TF(\"Delete\", \"c\"))) return Sw.Net.Send(\"../Api/x/DeleteAll\"); } });")]
        [InlineData("Sw.Dialog.Open({ Id: \"edit\", Title: a }); Sw.Dialog.Open({ Id: \"view\", Title: b });")]
        [InlineData("const pop = Sw.el(\"div.sw-popup\"); close.onclick = () => Sw.Motion.Out(pop);")]
        [InlineData("const row = Sw.el(\"div.my-row\"); row.remove();")]
        [InlineData("Sw.Dialog.Show({ Title: t, Buttons: [{ Text: _TF(\"Cancel\", \"c\") }, { Text: _TF(\"Save\", \"c\"), OnClick: (close, v, { Signal }) => Sw.Net.Send(\"../Api/x/Save\", 1, { Signal }) }] });")]
        [InlineData("const close = Sw.Dialog.Working({ Title: t, Cancel: () => ctrl.abort() });")]
        //  await inside async functions
        [InlineData("Sw.Page.Main(async () => { const r = await Sw.Net.Send(\"../Api/x\"); });")]
        [InlineData("const f = async () => await go();")]
        public void LeavesTheRightWayAlone(String code)
        {
            Assert.Empty(Js(code));
        }

        [Fact]
        public void FormsWithoutDirtyTrackingAndTextWithoutDraft()
        {
            //  A page that edits and saves without SetDirty: one finding at the first field; the textarea without a draft; no labels (and the click listener's request whose failure nobody handles: ux-fbk-5)
            var f = Js("Sw.Page.Main(async () => {\r\n const name = document.createElement(\"input\");\r\n const note = document.createElement(\"textarea\");\r\n"
                + " save.addEventListener(\"click\", () => Sw.Net.Send(\"../Api/x/SaveNote\", { N: name.value, T: note.value }));\r\n});");
            Assert.Equal(new[] { "ux-fbk-5", "ux-frm-1", "ux-saf-10", "ux-saf-11" }, Ids(f));
            Assert.Single(f, x => x.Rule == Rule.UxSaf10);
            //  The same with Sw controls (they track their own edits) and a draft: nothing
            Assert.Empty(Js("Sw.Page.Main(async () => {\r\n const name = new Sw.TextBox({ Label: _TF(\"Name\", \"c\") });\r\n const note = new Sw.TextBox({ Label: _TF(\"Note\", \"c\"), Multiline: true });\r\n"
                + " const d = Sw.Draft.Attach(note.Input, { Context: \"note\" });\r\n"
                + " save.O.OnClick = () => Sw.Net.Send(\"../Api/x/SaveNote\", 1).then(() => { d.Clear(); });\r\n});"));
            //  An own SetDirty next to a Sw control that tracks itself: the leave question names the change twice
            Assert.Equal(new[] { "ux-saf-10" }, Ids(Js("const name = new Sw.TextBox({ Label: _TF(\"Name\", \"c\") });\r\nname.Input.addEventListener(\"input\", () => Sw.Page.SetDirty(\"form\", _TF(\"Your changes are lost.\", \"c\")));\r\nSw.Page.SetDirty(\"form\", false);")));
            //  Fields of a dialog (the dialog asks itself) and search fields are not page edits
            Assert.Empty(Js("function edit() {\r\n const inp = new Sw.TextBox({ Label: _TF(\"Title\", \"c\") });\r\n Sw.Dialog.Show({ Title: _TF(\"Edit\", \"c\"), Content: inp.E, Buttons: [{ Text: _TF(\"Save\", \"c\"), OnClick: () => Sw.Net.Send(\"../Api/x/Save\", inp.Get()) }] });\r\n}"));
            Assert.Empty(Js("const search = new Sw.TextBox({ Label: _TF(\"Search\", \"c\"), Search: true });\r\nsearch.Input.addEventListener(\"input\", () => Sw.Net.Send(\"../Api/x/SetFilter\", search.Get()));"));
        }

        [Fact]
        public void CssBusyLookDistancesAndWholePageProgress()
        {
            Assert.Equal(new[] { "ux-fbk-2" }, Ids(Css(".row[aria-busy=\"true\"] { opacity: 0.5; }")));
            Assert.Equal(new[] { "ux-fbk-2" }, Ids(Css(".my-busy .x { pointer-events: none; }")));
            Assert.Empty(Css(".my-busy { color: var(--sw-text-2); }"));
            //  One finding per keyframes
            var k = Css("@keyframes sheet-in { 0% { transform: translateY(24px); } 50% { transform: translateY(12px); } }");
            Assert.Equal("ux-mot-2", Assert.Single(k).Rule.Id);
            Assert.Empty(Css("@keyframes shake { 0% { transform: translateX(-4px); } 50% { transform: translateX(4px); } }"));
            Assert.Empty(Css("@keyframes shake { 0% { transform: translateX(calc(-1 * var(--sw-dist-1))); } 100% { transform: none; } }"));
            Assert.Empty(Css("@keyframes spin { to { transform: rotate(360deg); } }"));
            Assert.Equal(new[] { "ux-inp-10" }, Ids(Css("body.my-busy { cursor: progress; }")));
            Assert.Empty(Css(".my-busy-button { cursor: progress; }"));
            Assert.Equal(new[] { "ux-mot-4" }, Ids(Css(".x { transition: all var(--sw-dur-1); }")));
        }

        [Fact]
        public void AllowCommentAndCitation()
        {
            Assert.Empty(Js("/* SwWeb:allow ux-lst-3 (a print layout) */\r\nconst t = document.createElement(\"table\");"));
            var f = Assert.Single(Js("page.Add(Sw.CopyButton({}));"));
            Assert.EndsWith("(UX guide INP-31: _docs/UX_Guide.md#1-input-pointer-touch-and-keyboard)", f.Message);
            Assert.Equal(Severity.Warning, f.Severity);
            //  Without --design the pattern rules are off (they are UX rules)
            var g = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext("page/x.js", new SourceText(Header + "page.Add(Sw.CopyButton({}));"), new CheckOptions(), g));
            Assert.Empty(g);
        }
    }
}
