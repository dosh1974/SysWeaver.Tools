using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;

namespace SwWeb.Tests
{
    /// <summary>
    /// The rules on the call structure and the value flow (UxFlow.cs, UxFlowRules.cs): the same mistake in other spellings (aliases,
    /// destructuring, helpers one or more calls deep, timers, Object.assign, bracket access, setProperty, markup strings) is found, and the
    /// right patterns next to it are left alone.
    /// </summary>
    public class UxFlowTests
    {
        const String Header = "/** @file test */\r\n";

        static List<Finding> Js(String code, String rel = "page/x.js")
        {
            var f = new List<Finding>();
            Checker.CheckJsFile(new Checker.FileContext(rel, new SourceText(Header + code), new CheckOptions { Design = true }, f));
            return f.Where(x => x.Rule.Guide != null).ToList();
        }

        static String[] Ids(List<Finding> f) => f.Select(x => x.Rule.Id).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();

        [Theory]
        //  FBK-2: the request in a helper, a forEach, a timer, a .then chain, an alias of Sw.Net.Send, a destructured Net
        [InlineData("function save() { Sw.Net.Send(\"../Api/x/Save\", 1); }\r\nnew Sw.Button({ Text: t, OnClick: () => { save(); } });", "ux-fbk-2")]
        [InlineData("const save = () => { Sw.Net.Send(\"../Api/x/Save\", 1); };\r\nnew Sw.Button({ Text: t, OnClick: () => save() });", "ux-fbk-2")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => { ids.forEach(id => Sw.Net.Send(\"../Api/x/Sync\", id)); } });", "ux-fbk-2")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => { setTimeout(() => Sw.Net.Send(\"../Api/x/Save\", 1), 0); } });", "ux-fbk-2")]
        [InlineData("const send = Sw.Net.Send;\r\nnew Sw.Button({ Text: t, OnClick: async () => { send(\"../Api/x/Save\", 1); } });", "ux-fbk-2")]
        [InlineData("const { Net, Button } = Sw;\r\nnew Button({ Text: t, OnClick: () => { Net.Send(\"../Api/x/Save\", 1).then(done); } });", "ux-fbk-2")]
        //  FBK-5: a "Saved" toast for an in-place result; resolving false without a reason
        [InlineData("new Sw.Button({ Text: t, OnClick: async () => { await Sw.Net.Send(\"../Api/x/SetName\", 1); Sw.Info(_TF(\"Saved\", \"c\")); } });", "ux-fbk-5")]
        [InlineData("new Sw.Button({ Text: t, OnClick: async () => { await wait(1); return false; } });", "ux-fbk-5")]
        //  SAF-1: a destructive api through a named handler, a helper, an alias, a new verb
        [InlineData("function purge() { return Sw.Net.Send(\"../Api/x/PurgeAll\", null); }\r\nnew Sw.Button({ Text: t, OnClick: purge });", "ux-saf-1")]
        [InlineData("const call = (n, a) => Sw.Net.Send(\"../Api/x/\" + n, a);\r\nnew Sw.Button({ Text: t, OnClick: () => call(\"DeleteAll\", 1) });", "ux-saf-1")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => Sw.Net.Send(\"../Api/x/ClearLog\", 1) });", "ux-saf-1")]
        [InlineData("Sw.OnClick(el, () => Sw.Net.Send(\"../Api/x/TruncateTable\", 1));", "ux-saf-1")]
        //  INP-12: Object.assign, bracket, a boolean expression, a reason that says nothing, a destructured Button, a local wrapper
        [InlineData("const go = document.createElement(\"button\"); Object.assign(go, { disabled: true });", "ux-inp-12")]
        [InlineData("const go = document.createElement(\"button\"); go[\"disabled\"] = true;", "ux-inp-12")]
        [InlineData("new Sw.Button({ Text: t, Disabled: rows.length === 0 });", "ux-inp-12")]
        [InlineData("Sw.Disabled.Set(el, true, \"no\");", "ux-inp-12")]
        [InlineData("const { Button } = Sw;\r\nnew Button({ Text: t, Disabled: !ready });", "ux-inp-12")]
        [InlineData("const B = (o) => new Sw.Button(o);\r\nB({ Text: t, Disabled: !ready });", "ux-inp-12")]
        //  POP-9 / POP-1: native dialogs by destructuring, an own dialog element
        [InlineData("const { confirm: ask } = window; if (ask(\"x\")) go();", "ux-pop-9")]
        [InlineData("const d = Sw.el(\"dialog.x\"); document.body.append(d); d.show();", "ux-pop-1")]
        [InlineData("host.innerHTML = \"<dialog open>Hi</dialog>\";", "ux-pop-1")]
        //  MOT-2 / MOT-4 / INP-10: styles set from script in any spelling
        [InlineData("box.style.setProperty(\"transition\", \"opacity 250ms\");", "ux-mot-2")]
        [InlineData("box.style[\"transitionDuration\"] = \"300ms\";", "ux-mot-2")]
        [InlineData("Object.assign(box.style, { transition: \"opacity 200ms\" });", "ux-mot-2")]
        [InlineData("box.style.cssText = \"transition: height var(--sw-dur-2)\";", "ux-mot-4")]
        [InlineData("box.style.setProperty(\"transition\", \"padding var(--sw-dur-1)\");", "ux-mot-4")]
        [InlineData("document.body.style.setProperty(\"cursor\", \"progress\");", "ux-inp-10")]
        //  INP-23: Alt+Left in other spellings, browser shortcuts, a combo twice
        [InlineData("document.addEventListener(\"keydown\", e => { if (e.getModifierState(\"Alt\") && e.key === \"ArrowLeft\") back(); });", "ux-inp-23")]
        [InlineData("document.addEventListener(\"keydown\", e => { if (e.altKey && e.keyCode === 37) back(); });", "ux-inp-23")]
        [InlineData("Sw.Keys.Add(\"Mod+W\", close);", "ux-inp-23")]
        [InlineData("Sw.Keys.Add(\"Mod+S\", a);\r\nSw.Keys.Add(\"Mod+S\", b);", "ux-inp-23")]
        //  SAF-11 / SEC-1: drafts by bracket / typed value, one Context twice, a control drafted twice, secrets drafted / said aloud
        [InlineData("ed.addEventListener(\"input\", () => { sessionStorage[\"draft_notes\"] = ed.textContent; });", "ux-saf-11")]
        [InlineData("ta.addEventListener(\"input\", () => { localStorage[\"page.notes\"] = ta.value; });", "ux-saf-11")]
        [InlineData("const a = Sw.Draft.Attach(x, { Context: \"same\" }); const b = Sw.Draft.Attach(y, { Context: \"same\" }); a.Clear(); b.Clear();", "ux-saf-11")]
        [InlineData("const tb = new Sw.TextBox({ Label: _TF(\"Notes\", \"c\"), Multiline: true, Draft: \"n\" });\r\nconst d = Sw.Draft.Attach(tb.Input, { Context: \"m\" }); d.Clear();", "ux-saf-11")]
        [InlineData("new Sw.TextBox({ Label: _TF(\"API token\", \"c\"), Draft: \"tok\" });", "ux-sec-1")]
        [InlineData("Sw.Toast(_TF(\"Copied\", \"c\") + \": \" + apiKey.Value);", "ux-sec-1")]
        //  SAF-10: own SetDirty next to a tracking control, location navigation on a page with fields, contenteditable saved untracked
        [InlineData("new Sw.TextBox({ Label: _TF(\"Subject\", \"c\"), OnInput: () => Sw.Page.SetDirty(\"s\", _TF(\"x\", \"c\")) });\r\nSw.Page.SetDirty(\"s\", false);", "ux-saf-10")]
        [InlineData("const name = new Sw.TextBox({ Label: _TF(\"Name\", \"c\") });\r\nback.onclick = () => { location.href = \"../index.html\"; };", "ux-saf-10")]
        [InlineData("const ed = Sw.el(\"div.notes\", { attrs: { contenteditable: \"true\" } });\r\nnew Sw.Button({ Text: t, OnClick: () => Sw.Net.Send(\"../Api/x/SaveNotes\", ed.textContent) });", "ux-saf-10")]
        //  FRM-1 from markup strings and Object.assign(createElement(...)); FBK-7 a polling loop; SAF-3 a default danger button; FBK-9
        [InlineData("wrap.innerHTML = \"<input class='q'>\";", "ux-frm-1")]
        [InlineData("const q = Object.assign(document.createElement(\"input\"), { name: \"qty\" });", "ux-frm-1")]
        [InlineData("const poll = async () => { const r = await Sw.Net.Get(\"../Api/x/Rows\"); list.replaceChildren(...r.map(x => Sw.el(\"li\", null, x.Name))); setTimeout(poll, 2000); };", "ux-fbk-7")]
        [InlineData("Sw.Dialog.Show({ Title: t, Buttons: [{ Text: c }, { Text: d, Kind: \"danger\", Default: true, OnClick: () => del() }] });", "ux-saf-3")]
        [InlineData("if (!Sw.Net.ShouldReportAsync(e)) return;", "ux-fbk-9")]
        [InlineData("Sw.Net.ShouldReport(e).then(r => r && Sw.Fail(e));", "ux-fbk-9")]
        //  Round 2 requests of Dialogs / Pages: a read after an await in a footer, an unowned menu, a rewritten history entry, a failure shown twice
        [InlineData("Sw.Dialog.Show({ Title: t, Buttons: [{ Text: _TF(\"Cancel\", \"c\") }, { Text: s, OnClick: async () => { await check(); await Sw.Net.Send(\"../Api/x/GetRows\", 1); } }] });", "ux-pop-6")]
        [InlineData("Sw.Dialog.Open({ Title: t }); Sw.Menu.Show(btn, items, { Owner: null });", "ux-pop-1")]
        [InlineData("history.pushState({}, \"\", \"?tab=2\");", "ux-saf-12")]
        [InlineData("new Sw.Button({ Text: t, OnClick: async () => { try { await Sw.Net.Send(\"../Api/x/Save\", 1); } catch (e) { form.SetError(\"name\", e.message); Sw.Fail(e); } } });", "ux-fbk-5")]
        public void VariantIsFound(String code, String rule)
        {
            var f = Js(code);
            Assert.Contains(rule, Ids(f));
        }

        [Theory]
        //  The right patterns next to them
        [InlineData("new Sw.Button({ Text: t, OnClick: () => Sw.Net.Send(\"../Api/x/Save\", 1) });")]
        [InlineData("const send = (k, u, d) => Sw.Net.Send(u, d);\r\nnew Sw.Button({ Text: t, OnClick: () => send(1, \"../Api/x/Save\", 2) });")]
        [InlineData("async function save() { await Sw.Net.Send(\"../Api/x/Save\", 1); }\r\nnew Sw.Button({ Text: t, OnClick: async () => { await save(); } });")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => Sw.Activity.Track(Sw.Net.Send(\"../Api/x/Export\", 1), { Title: t }) });")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => { Sw.Dialog.Working({ Title: t, Promise: Sw.Net.Send(\"../Api/x/Export\", 1) }); } });")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => { Sw.Net.Send(\"../Api/x/Telemetry\", 1, { Detached: true }); } });")]
        [InlineData("function startLive() { Sw.Net.Get(\"../Api/x/Poll\").then(show); }\r\nnew Sw.Button({ Text: t, OnClick: () => { startLive(); } });")]
        [InlineData("new Sw.Button({ Text: t, Kind: \"danger\", OnClick: async () => { if (await Sw.Dialog.Confirm(a, b, c, { Danger: true })) return Sw.Net.Send(\"../Api/x/DeleteAll\", 1); } });")]
        [InlineData("new Sw.Button({ Text: t, OnClick: () => Sw.Dialog.Show({ Title: t, Buttons: [{ Text: c }, { Text: d, Kind: \"danger\", OnClick: () => Sw.Net.Send(\"../Api/x/DeleteReport\", 1) }] }) });")]
        [InlineData("new Sw.Button({ Text: t, Disabled: rows.length === 0 ? _TF(\"Select a row first\", \"c\") : false });")]
        [InlineData("new Sw.Button({ Text: t, Disabled: _TF(\"Select a row first\", \"c\") });")]
        [InlineData("box.style.setProperty(\"transition\", \"opacity var(--sw-dur-1)\");")]
        [InlineData("Sw.Keys.Add(\"Mod+S\", a, { Title: t });\r\nSw.Keys.Add(\"Mod+Shift+S\", b, { Title: t });")]
        [InlineData("const d = Sw.Draft.Attach(notes, { Context: \"notes\" });\r\nd.Clear();")]
        [InlineData("Sw.Page.Navigate(\"../index.html\");")]
        [InlineData("if (!Sw.Net.ShouldReport(e)) return;")]
        [InlineData("const ok = await Sw.Net.ShouldReportAsync(e);")]
        [InlineData("document.addEventListener(\"keydown\", e => { if (!e.altKey && e.key === \"ArrowLeft\") prev(); });")]
        [InlineData("Sw.Dialog.Show({ Title: t, Buttons: [{ Text: _TF(\"Cancel\", \"c\") }, { Text: s, OnClick: async () => { await check(); await Sw.Net.Send(\"../Api/x/SaveRows\", 1); } }] });")]
        [InlineData("history.replaceState(history.state, \"\", \"#\" + id);")]
        public void RightPatternIsLeftAlone(String code)
        {
            Assert.Empty(Js(code).Select(x => x.Rule.Id + ": " + x.Message));
        }
    }
}
