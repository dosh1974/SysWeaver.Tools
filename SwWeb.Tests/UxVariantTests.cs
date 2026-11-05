using System;
using System.Collections.Generic;
using System.Linq;
using SwWeb.Checking;
using SwWeb.Lexing;
using Xunit.Abstractions;

namespace SwWeb.Tests
{
    /// <summary>
    /// Mutation style tests of the rule families (series 4 round 4): a generator writes each mistake of a family in many spellings (sinks x
    /// sources x wrappers: direct, an alias, a helper, a class or object method, async / await, a then / catch callback, destructuring, an
    /// event listener, a timer, another call site) and the right pattern next to it in the same spellings. Every bad variant must be found
    /// by one of the family's rules, every right variant must stay without a finding of them. A rule tuned to one spelling fails here, so
    /// the gate generalizes by family (owner direction: the right patterns easy, the bad patterns hard).
    /// </summary>
    public class UxVariantTests
    {
        readonly ITestOutputHelper Out;

        public UxVariantTests(ITestOutputHelper output)
        {
            Out = output;
        }

        const String Header = "/** @file test */\r\n";

        /// <summary>One generated file: a mistake (Bad) or the right pattern (!Bad) of a family, judged by the family's rules.</summary>
        public sealed class Variant
        {
            public String Family, Name, Code, Kind = "js";
            public String[] Rules;
            public bool Bad;
            public override String ToString() => (Bad ? "bad " : "good ") + Family + " " + Name;
        }

        // ------------------------------------------------------------------ wrappers

        /// <summary>The shared names every core may use: an element, a status line, a data item, a number, ids, a select.</summary>
        const String Prelude = "const el = Sw.el(\"div.box\"), status = Sw.el(\"p.sw-selectable\", { attrs: { role: \"status\" } }), sel = Sw.el(\"select.pick\", { attrs: { \"aria-label\": _TF(\"Pick\", \"c\") } });\r\nconst d = window.Item || {}, n = 2, ids = [1, 2], items = window.Items || [];\r\n";

        /// <summary>Where the work of a user action is written (the core runs when the user acts).</summary>
        static readonly (String Name, Func<String, String> Wrap)[] ActionWrappers =
        {
            ("onclick", c => "const b = new Sw.Button({ Text: _TF(\"Go\", \"c\"), OnClick: async () => {\r\n" + c + "\r\n} });"),
            ("helper", c => "async function work() {\r\n" + c + "\r\n}\r\nconst b = new Sw.Button({ Text: _TF(\"Go\", \"c\"), OnClick: () => work() });"),
            ("method", c => "class Page {\r\n    async Work() {\r\n" + c + "\r\n    }\r\n    Start() {\r\n        return new Sw.Button({ Text: _TF(\"Go\", \"c\"), OnClick: () => this.Work() });\r\n    }\r\n}"),
            ("listener", c => "el.addEventListener(\"click\", async ev => {\r\n    if (Sw.BadClick(ev)) return;\r\n" + c + "\r\n});"),
            ("objmethod", c => "const H = {\r\n    async run() {\r\n" + c + "\r\n    },\r\n};\r\nSw.OnClick(el, () => H.run());"),
            ("named", c => "const onGo = async function () {\r\n" + c + "\r\n};\r\nconst b = new Sw.Button({ Text: _TF(\"Go\", \"c\"), OnClick: onGo });"),
            ("keydown", c => "el.addEventListener(\"keydown\", async ev => {\r\n    if (ev.key !== \"Enter\") return;\r\n" + c + "\r\n});"),
            ("then", c => "const b = new Sw.Button({ Text: _TF(\"Go\", \"c\"), OnClick: () => Sw.Dialog.Confirm(_TF(\"Send the order again?\", \"c\"), _TF(\"The customer gets a second e-mail.\", \"c\"), _TF(\"Send again\", \"c\")).then(async ok => {\r\n    if (!ok) return;\r\n" + c + "\r\n}) });"),
        };

        /// <summary>Where a page builds or wires things (the core runs while the page is made).</summary>
        static readonly (String Name, Func<String, String> Wrap)[] SetupWrappers =
        {
            ("main", c => "Sw.Page.Main(async () => {\r\n" + c + "\r\n});"),
            ("loop", c => "function render(list) {\r\n    for (const d of list) {\r\n" + c + "\r\n    }\r\n}"),
            ("method", c => "class View {\r\n    Build(d) {\r\n" + c + "\r\n    }\r\n}"),
            ("foreach", c => "items.forEach((d, i) => {\r\n" + c + "\r\n});"),
            ("function", c => "function wire(el, d) {\r\n" + c + "\r\n}"),
        };

        // ------------------------------------------------------------------ the families

        static IEnumerable<Variant> Expand(String family, String name, String[] rules, bool bad, String core, bool action, bool setup = false, String extra = "")
        {
            var ws = (action ? ActionWrappers : Array.Empty<(String, Func<String, String>)>()).Concat(setup ? SetupWrappers : Array.Empty<(String, Func<String, String>)>());
            foreach (var (wn, w) in ws)
                yield return new Variant { Family = family, Name = name + " / " + wn, Rules = rules, Bad = bad, Code = Prelude + extra + w(core) };
        }

        static readonly String[] Sec = { "ux-sec-1" };

        /// <summary>Secrets (SEC-1): sinks (storage, cookies, logs, url queries, toasts) x sources (names, members, controls, destructuring, parameters).</summary>
        static IEnumerable<Variant> Secrets()
        {
            var sources = new (String Name, String Decl, String Expr)[]
            {
                ("name", "const apiKey = d.Key;", "apiKey"),
                ("member", "", "d.refreshToken"),
                ("password", "", "d.Password"),
                ("control", "const pass = new Sw.TextBox({ Label: _TF(\"Carrier password\", \"c\"), Type: \"password\" });", "pass.Get()"),
                ("field", "const pw = document.querySelector(\"input[type=password]\");", "pw.value"),
                ("destructured", "const { clientSecret } = d;", "clientSecret"),
                ("german", "const passwort = d.P;", "passwort"),
                ("session", "", "d.SessionId"),
            };
            var sinks = new (String Name, Func<String, String> Code)[]
            {
                ("localStorage", v => "localStorage.setItem(\"app.k\", " + v + ");"),
                ("sessionStorage key", v => "sessionStorage.setItem(\"ship.\" + n, " + v + ");"),
                ("storage index", v => "localStorage[\"cfg\"] = " + v + ";"),
                ("window storage", v => "window.sessionStorage.setItem(\"x\", " + v + ");"),
                ("storage alias", v => "const st = window.localStorage;\r\nst.setItem(\"x\", " + v + ");"),
                ("Sw.Storage", v => "Sw.Storage.Local.Set(\"x\", " + v + ");"),
                ("json", v => "localStorage.setItem(\"x\", JSON.stringify({ user: d.User, value: " + v + " }));"),
                ("cookie", v => "document.cookie = \"k=\" + " + v + " + \"; path=/\";"),
                ("cookie template", v => "document.cookie = `k=${" + v + "}; path=/`;"),
                ("console", v => "console.log(\"saved\", " + v + ");"),
                ("console.debug", v => "console.debug(\"[ship]\", " + v + ");"),
                ("log helper", v => "trace(\"saved\", " + v + ");"),
                ("logger", v => "Logger.debug(\"saved\", " + v + ");"),
                ("url query", v => "await Sw.Net.Get(\"../Api/x/GetState?token=\" + " + v + ");"),
                ("url template", v => "await Sw.Net.Get(`../Api/x/GetState?id=${n}&apiKey=${" + v + "}`);"),
                ("url params", v => "const q = new URLSearchParams({ id: String(n) });\r\nq.set(\"access_token\", " + v + ");\r\nawait Sw.Net.Get(\"../Api/x/GetState?\" + q);"),
                ("window.open", v => "window.open(\"export.csv?s=\" + " + v + ");"),
                ("toast", v => "Sw.Info(_T(\"Your key: {0}\", " + v + ", \"c\"));"),
                ("status", v => "status.textContent = " + v + ";"),
            };
            const String helpers = "function trace(what, value) { if (window.DebugOn) console.debug(what, value); }\r\nconst Logger = { debug: (...a) => window.DebugOn && console.debug(...a) };\r\n";
            foreach (var (sn, sc) in sinks)
                foreach (var (on, od, oe) in sources)
                    foreach (var v in Expand("secrets", sn + " <- " + on, Sec, true, od + "\r\n" + sc(oe), true, false, helpers).Where((x, i) => i % 3 == (sn.Length + on.Length) % 3))
                        yield return v;
            //  The right patterns: no secret, metadata of a secret, the secret sent in the body
            var safe = new (String Name, String Code)[]
            {
                ("theme", "localStorage.setItem(\"app.theme\", d.Theme);"),
                ("length", "const apiKey = d.Key;\r\nlocalStorage.setItem(\"app.keylen\", String(apiKey.length));"),
                ("hasToken", "const hasToken = !!d.Token;\r\nconsole.log(\"has token\", hasToken);"),
                ("body", "const apiKey = d.Key;\r\nawait Sw.Net.Send(\"../Api/x/Check\", { Key: apiKey });"),
                ("label", "const tokenLabel = _TF(\"Token\", \"c\");\r\nstatus.textContent = tokenLabel;"),
                ("query id", "await Sw.Net.Get(\"../Api/x/GetState?id=\" + d.Id);"),
                ("masked", "const pass = new Sw.TextBox({ Label: _TF(\"Password\", \"c\"), Type: \"password\" });\r\nawait Sw.Net.Send(\"../Api/x/SetPassword\", { P: pass.Get() });"),
            };
            foreach (var (sn, sc) in safe)
                foreach (var v in Expand("secrets", "right " + sn, Sec, false, sc, true))
                    yield return v;
            //  Unmasked secret fields (SEC-1: masked by default)
            var unmasked = new (String Name, String Code)[]
            {
                ("TextBox label", "const pass = new Sw.TextBox({ Label: _TF(\"Carrier password\", \"c\") });"),
                ("TextBox placeholder", "const pass = new Sw.TextBox({ Label: _TF(\"Account\", \"c\"), Placeholder: _TF(\"Your password\", \"c\") });"),
                ("TextBox api key", "const key = new Sw.TextBox({ Label: _TF(\"API key\", \"c\") });"),
                ("TextBox german", "const pass = new Sw.TextBox({ Label: _TF(\"Kennwort\", \"c\") });"),
                ("TextBox Type text", "const pass = new Sw.TextBox({ Label: _TF(\"Password\", \"c\"), Type: \"text\" });"),
                ("input", "const pass = document.createElement(\"input\");\r\npass.type = \"text\";\r\npass.setAttribute(\"aria-label\", _TF(\"Password\", \"c\"));"),
                ("Sw.el input", "const pass = Sw.el(\"input.pw\", { attrs: { type: \"text\", \"aria-label\": _TF(\"Client secret\", \"c\") } });"),
            };
            foreach (var (un, uc) in unmasked)
                foreach (var v in Expand("secrets", "unmasked " + un, Sec, true, uc + "\r\nel.append(pass.E || pass);", false, true))
                    yield return v;
            foreach (var v in Expand("secrets", "right masked", Sec, false, "const pass = new Sw.TextBox({ Label: _TF(\"Carrier password\", \"c\"), Type: \"password\" });\r\nconst hint = new Sw.TextBox({ Label: _TF(\"Password hint\", \"c\") });\r\nel.append(pass.E, hint.E);", false, true))
                yield return v;
        }

        static readonly String[] Html = { "ux-sec-2" };

        /// <summary>Raw html of user data (SEC-2): sinks x values (concatenation, templates, helpers, variables, map / join).</summary>
        static IEnumerable<Variant> RawHtml()
        {
            var values = new (String Name, String Decl, String Expr)[]
            {
                ("concat", "", "\"<b>\" + d.Name + \"</b>\""),
                ("template", "", "`<td>${d.Note}</td><td>${d.Owner}</td>`"),
                ("helper", "", "rowHtml(d)"),
                ("variable", "const html = \"<i>\" + d.Title + \"</i>\";", "html"),
                ("let +=", "let h = \"<ul>\";\r\nfor (const x of items) h += \"<li>\" + x.Name + \"</li>\";", "h"),
                ("map join", "", "items.map(x => `<li>${x.Comment}</li>`).join(\"\")"),
                ("array join", "", "[\"<p>\", d.Message, \"</p>\"].join(\"\")"),
                ("data only", "", "d.Description"),
            };
            var sinks = new (String Name, Func<String, String> Code)[]
            {
                ("innerHTML", v => "el.innerHTML = " + v + ";"),
                ("innerHTML +=", v => "el.innerHTML += " + v + ";"),
                ("insertAdjacentHTML", v => "el.insertAdjacentHTML(\"beforeend\", " + v + ");"),
                ("outerHTML", v => "el.outerHTML = " + v + ";"),
                ("Sw.el html", v => "el.append(Sw.el(\"div\", { html: " + v + " }));"),
                ("alias", v => "const box = el;\r\nbox.innerHTML = " + v + ";"),
            };
            const String helpers = "function rowHtml(x) { return `<td>${x.Name}</td><td>${x.City}</td>`; }\r\n";
            foreach (var (sn, sc) in sinks)
                foreach (var (vn, vd, ve) in values)
                    foreach (var v in Expand("raw html", sn + " <- " + vn, Html, true, vd + "\r\n" + sc(ve), false, true, helpers).Where((x, i) => i % 2 == (sn.Length + vn.Length) % 2))
                        yield return v;
            var safe = new (String Name, String Code)[]
            {
                ("text", "el.textContent = d.Name;"),
                ("Sw.el text", "el.append(Sw.el(\"b\", { text: d.Name }));"),
                ("escaped", "el.innerHTML = \"<b>\" + escapeHtml(d.Name) + \"</b>\";"),
                ("literal", "el.innerHTML = \"<b>static</b>\";"),
                ("translated", "el.innerHTML = _TFH(\"<b>Read</b> the guide\", \"c\");"),
                ("number", "el.innerHTML = \"<b>\" + items.length + \"</b>\";"),
                ("framework", "el.innerHTML = Sw.Markdown.ToHtml(d.Text);"),
            };
            foreach (var (sn, sc) in safe)
                foreach (var v in Expand("raw html", "right " + sn, Html, false, sc, false, true, "function escapeHtml(s) { return Sw.Html.Escape(s); }\r\n"))
                    yield return v;
        }

        static readonly String[] Fmt = { "ux-txt-20" };

        /// <summary>Numbers, sizes, durations and dates by hand (TXT-20) x sinks.</summary>
        static IEnumerable<Variant> Formatting()
        {
            var values = new (String Name, String Decl, String Expr)[]
            {
                ("toFixed KB", "", "(d.Size / 1024).toFixed(1) + \" KB\""),
                ("round %", "", "Math.round(d.Ratio * 100) + \"%\""),
                ("template s", "", "`${(d.Ms / 1000).toFixed(1)} s`"),
                ("round MB", "", "Math.round(d.Bytes / 1048576) + \" MB\""),
                ("currency", "", "d.Price.toFixed(2) + \" €\""),
                ("duration vars", "const h = Math.floor(d.Ms / 3600000);\r\nconst m = Math.floor((d.Ms % 3600000) / 60000);", "h + \"h \" + m + \"m\""),
                ("duration template", "const mins = Math.round(d.Ms / 60000);", "`${mins} min`"),
                ("date parts", "const t = new Date(d.When);", "t.getDate() + \"/\" + (t.getMonth() + 1) + \"/\" + t.getFullYear()"),
                ("time template", "const t = new Date(d.When);", "`${t.getHours()}:${t.getMinutes()}`"),
                ("padStart", "const t = new Date(d.When);", "String(t.getHours()).padStart(2, \"0\") + \":\" + String(t.getMinutes()).padStart(2, \"0\")"),
                ("parts join", "const t = new Date(d.When);", "[t.getDate(), t.getMonth() + 1, t.getFullYear()].join(\".\")"),
                ("iso slice", "", "new Date(d.When).toISOString().slice(0, 10)"),
                ("iso split", "const t = new Date(d.When);", "t.toISOString().split(\"T\")[0]"),
                ("iso substring", "", "new Date(d.When).toISOString().substring(0, 16).replace(\"T\", \" \")"),
                ("fixed locale", "", "new Date(d.When).toLocaleDateString(\"en-US\")"),
                ("Intl", "", "new Intl.DateTimeFormat(\"de-DE\").format(new Date(d.When))"),
                ("toUTCString", "", "new Date(d.When).toUTCString()"),
                ("toDateString", "", "new Date(d.When).toDateString()"),
            };
            var sinks = new (String Name, Func<String, String> Code)[]
            {
                ("textContent", v => "status.textContent = " + v + ";"),
                ("Sw.el text", v => "el.append(Sw.el(\"td\", { text: " + v + " }));"),
                ("placeholder value", v => "status.textContent = _T(\"Shipped {0}\", " + v + ", \"c\");"),
                ("const then text", v => "const shown = " + v + ";\r\nstatus.textContent = shown;"),
            };
            foreach (var (sn, sc) in sinks)
                foreach (var (vn, vd, ve) in values)
                    foreach (var v in Expand("formatting", sn + " <- " + vn, Fmt, true, vd + "\r\n" + sc(ve), false, true).Where((x, i) => i % 3 == (sn.Length + vn.Length) % 3))
                        yield return v;
            var safe = new (String Name, String Code)[]
            {
                ("Bytes", "status.textContent = Sw.Format.Bytes(d.Size);"),
                ("Number", "status.textContent = _T(\"{0} %\", Sw.Format.Number(d.Ratio * 100, 0), \"c\");"),
                ("TimeStamp", "el.append(Sw.el(\"td\", { text: Sw.Format.TimeStamp(new Date(d.When))[1] }));"),
                ("style width", "el.style.width = Math.round(d.Ratio * 100) + \"%\";"),
                ("file name", "const name = \"report-\" + new Date(d.When).toISOString().slice(0, 10) + \".csv\";\r\nawait Sw.Net.Download(\"../Api/x/GetReport\", name);"),
                ("payload", "await Sw.Net.Send(\"../Api/x/GetRange\", { From: new Date(d.When).toISOString().slice(0, 10) });"),
                ("viewer locale", "status.textContent = new Date(d.When).toLocaleDateString();"),
                ("stamp", "const stamp = t => t.getFullYear() + \"-\" + String(t.getMonth() + 1).padStart(2, \"0\");\r\nawait Sw.Net.Download(\"../Api/x/GetLog\", \"log-\" + stamp(new Date()) + \".txt\");"),
            };
            foreach (var (sn, sc) in safe)
                foreach (var v in Expand("formatting", "right " + sn, Fmt, false, sc, false, true))
                    yield return v;
        }

        static readonly String[] Concat = { "ux-txt-6" };

        /// <summary>Sentences concatenated from translations and values, plurals by "s" (TXT-6).</summary>
        static IEnumerable<Variant> Sentences()
        {
            var bad = new (String Name, String Expr)[]
            {
                ("tr value tr", "_TF(\"Removed\", \"c\") + \" \" + n + \" \" + _TF(\"items\", \"c\")"),
                ("value tr", "n + \" \" + _TF(\"files\", \"c\")"),
                ("fragment", "_TF(\"Hello \", \"c\") + d.Name"),
                ("template", "`${_TF(\"Total\", \"c\")} ${n}`"),
                ("plural ===", "_T(\"{0} item\", n, \"c\") + (n === 1 ? \"\" : \"s\")"),
                ("plural >", "_T(\"{0} parcel\", n, \"c\") + (n > 1 ? \"s\" : \"\")"),
                ("plural !==", "_T(\"{0} file\", n, \"c\") + (n !== 1 ? \"s\" : \"\")"),
                ("plural template", "`${_T(\"{0} row\", n, \"c\")}${n == 1 ? \"\" : \"s\"}`"),
                ("join", "[_TF(\"Removed\", \"c\"), n, _TF(\"items\", \"c\")].join(\" \")"),
            };
            var sinks = new (String Name, Func<String, String> Code)[]
            {
                ("textContent", v => "status.textContent = " + v + ";"),
                ("toast", v => "Sw.Info(" + v + ");"),
                ("return", v => "const s = " + v + ";\r\nel.title = s;"),
            };
            foreach (var (sn, sc) in sinks)
                foreach (var (bn, be) in bad)
                    foreach (var v in Expand("sentences", sn + " <- " + bn, Concat, true, sc(be), false, true).Where((x, i) => i % 2 == (sn.Length + bn.Length) % 2))
                        yield return v;
            var safe = new (String Name, String Code)[]
            {
                ("placeholders", "status.textContent = _T(\"Removed {0} items\", n, \"c\");"),
                ("label", "status.textContent = _TF(\"Size\", \"c\") + \": \" + n;"),
                ("sentence reason", "status.textContent = _TF(\"Failed to save.\", \"c\") + \" \" + d.Reason;"),
                ("lines", "el.title = _TF(\"First line.\", \"c\") + \"\\n\" + d.Name;"),
            };
            foreach (var (sn, sc) in safe)
                foreach (var v in Expand("sentences", "right " + sn, Concat, false, sc, false, true))
                    yield return v;
        }

        static readonly String[] Ptr = { "ux-inp-1", "ux-inp-6", "ux-inp-8" };

        /// <summary>Pointer events other than click that act (INP-1, 6, 7, 8) x registration forms x actions.</summary>
        static IEnumerable<Variant> Pointer()
        {
            var acts = new (String Name, String Fn)[]
            {
                ("navigate", "() => open(d)"),
                ("request", "async () => { await Sw.Net.Send(\"../Api/x/Pick\", { Id: d.Id }); }"),
                ("select", "ev => { ev.preventDefault(); select(d.Id); }"),
                ("location", "() => { location.href = \"item.html?id=\" + d.Id; }"),
            };
            var regs = new (String Name, Func<String, String, String> Code)[]
            {
                ("addEventListener", (e, f) => "const row = Sw.el(\"div.row\");\r\nrow.addEventListener(\"" + e + "\", " + f + ");\r\nel.append(row);"),
                ("on property", (e, f) => "const row = Sw.el(\"div.row\");\r\nrow.on" + e + " = " + f + ";\r\nel.append(row);"),
                ("Sw.el on", (e, f) => "const row = Sw.el(\"div.row\", { on: { " + e + ": " + f + " } });\r\nel.append(row);"),
                ("querySelector", (e, f) => "const row = el.querySelector(\".row\");\r\nrow.addEventListener(\"" + e + "\", " + f + ");"),
            };
            const String helpers = "function open(x) { Sw.Page.Navigate(\"item.html?id=\" + x.Id); }\r\nfunction select(id) { window.Picked = id; Sw.Page.Announce(_TF(\"Selected\", \"c\")); }\r\n";
            foreach (var evt in new[] { "pointerdown", "mousedown", "touchstart", "pointerup", "mouseup", "touchend" })
                foreach (var (rn, rc) in regs)
                    foreach (var (an, af) in acts)
                        foreach (var v in Expand("pointer", evt + " " + rn + " " + an, Ptr, true, rc(evt, af), false, true, helpers).Where((x, i) => i % 4 == (evt.Length + rn.Length + an.Length) % 4))
                            yield return v;
            //  A press that acts and a release that acts on the same element (neither is a drag)
            foreach (var v in Expand("pointer", "press + release both act", Ptr, true, "const row = Sw.el(\"div.row\");\r\nrow.addEventListener(\"pointerdown\", () => select(d.Id));\r\nrow.addEventListener(\"pointerup\", ev => { if (Sw.IsPureEvent(ev)) open(d); });\r\nel.append(row);", false, true, helpers))
                yield return v;
            foreach (var evt in new[] { "mouseenter", "mouseover", "pointerenter", "pointerover" })
                foreach (var (hn, hf) in new[] { ("menu", "() => Sw.Menu.Show(row, [{ Text: _TF(\"Open\", \"c\"), OnClick: () => open(d) }])"), ("dialog", "() => Sw.Dialog.Open({ Title: _TF(\"Details\", \"c\"), Content: d.Name })"), ("navigate", "() => Sw.Page.Navigate(\"x.html\")"), ("write", "() => Sw.Net.Send(\"../Api/x/MarkSeen\", { Id: d.Id })") })
                    foreach (var v in Expand("pointer", evt + " " + hn, Ptr, true, "const row = Sw.el(\"div.row\");\r\nrow.addEventListener(\"" + evt + "\", " + hf + ");\r\nel.append(row);", false, true, helpers).Where((x, i) => i % 2 == (evt.Length + hn.Length) % 2))
                        yield return v;
            foreach (var v in Expand("pointer", "dblclick only", Ptr, true, "const row = Sw.el(\"div.row\");\r\nrow.addEventListener(\"dblclick\", () => open(d));\r\nel.append(row);", false, true, helpers))
                yield return v;
            //  The right patterns: a drag, a click, a preview on hover, a press that only keeps the focus
            var safe = new (String Name, String Code)[]
            {
                ("drag", "const row = Sw.el(\"div.row\");\r\nlet start = null;\r\nrow.addEventListener(\"pointerdown\", ev => { start = ev.clientY; row.setPointerCapture(ev.pointerId); });\r\nrow.addEventListener(\"pointermove\", ev => { if (start !== null) row.style.transform = \"translateY(\" + (ev.clientY - start) + \"px)\"; });\r\nrow.addEventListener(\"pointerup\", () => { if (start !== null) move(d, start); start = null; });\r\nel.append(row);"),
                ("click", "const row = Sw.el(\"div.row\");\r\nSw.OnClick(row, () => open(d));\r\nel.append(row);"),
                ("preview", "const row = Sw.el(\"div.row\");\r\nrow.addEventListener(\"mouseenter\", () => Sw.Net.Get(\"../Api/x/GetPreview\").then(p => { row.title = p.Text; }).catch(e => Sw.Fail(e)));\r\nel.append(row);"),
                ("keep focus", "const row = Sw.el(\"div.row\");\r\nrow.addEventListener(\"mousedown\", ev => ev.preventDefault());\r\nSw.OnClick(row, () => open(d));\r\nel.append(row);"),
                ("dblclick shortcut", "const row = Sw.el(\"div.row\");\r\nSw.OnClick(row, () => open(d));\r\nrow.addEventListener(\"dblclick\", () => open(d));\r\nel.append(row);"),
            };
            foreach (var (sn, sc) in safe)
                foreach (var v in Expand("pointer", "right " + sn, Ptr, false, sc, false, true, helpers + "function move(x, y) { return Sw.Net.Send(\"../Api/x/Move\", { Id: x.Id, Y: y }).catch(e => Sw.Fail(e)); }\r\n"))
                    yield return v;
        }

        static readonly String[] Silent = { "ux-fbk-5", "ux-fbk-2" };

        /// <summary>Silent failures (FBK-5): a request whose failure nobody sees, in every shape, x the places of a user action.</summary>
        static IEnumerable<Variant> SilentFailures()
        {
            const String send = "Sw.Net.Send(\"../Api/x/SaveNote\", { Id: d.Id, Text: d.Text })";
            var bad = new (String Name, String Code, String Extra)[]
            {
                ("empty catch", "try {\r\n    await " + send + ";\r\n} catch (e) {\r\n}", ""),
                ("catch without binding", "try {\r\n    await " + send + ";\r\n} catch {\r\n}", ""),
                ("catch warn", "try {\r\n    await " + send + ";\r\n} catch (e) {\r\n    console.warn(\"save failed\", e);\r\n}", ""),
                ("catch resets status", "try {\r\n    await " + send + ";\r\n    status.textContent = _TF(\"The note is saved.\", \"c\");\r\n} catch (e) {\r\n    status.textContent = \"\";\r\n}", ""),
                ("catch returns", "try {\r\n    await " + send + ";\r\n} catch (e) {\r\n    return;\r\n}", ""),
                ("catch returns null", "try {\r\n    await " + send + ";\r\n} catch (e) {\r\n    return null;\r\n}", ""),
                ("catch only ui", "try {\r\n    el.classList.add(\"busy\");\r\n    await " + send + ";\r\n} catch (e) {\r\n    el.classList.remove(\"busy\");\r\n}", ""),
                (".catch null", "await " + send + ".catch(() => null);", ""),
                (".catch empty", "await " + send + ".catch(() => {});", ""),
                (".catch undefined", send + ".catch(e => undefined);", ""),
                (".then rejected empty", send + ".then(() => el.classList.add(\"ok\"), () => {});", ""),
                ("allSettled dropped", "await Promise.allSettled(ids.map(id => Sw.Net.Send(\"../Api/x/Resend\", { Id: id })));\r\nstatus.textContent = _T(\"{0} notices were sent again.\", ids.length, \"c\");", ""),
                ("allSettled unread", "const res = await Promise.allSettled(ids.map(id => Sw.Net.Send(\"../Api/x/Resend\", { Id: id })));\r\nstatus.textContent = _T(\"{0} notices were sent again.\", res.length, \"c\");", ""),
                ("all of caught", "await Promise.all(ids.map(id => Sw.Net.Send(\"../Api/x/Resend\", { Id: id }).catch(() => null)));\r\nstatus.textContent = _TF(\"Sent again.\", \"c\");", ""),
                ("null helper", "const info = await post(\"Track\", { Id: d.Id });\r\nstatus.textContent = info.Where;", "function post(api, body) {\r\n    return fetch(\"../Api/x/\" + api, { method: \"POST\", body: JSON.stringify(body) }).then(r => r.json()).catch(() => null);\r\n}\r\n"),
                ("null helper try", "const info = await post(\"Track\", { Id: d.Id });\r\nstatus.textContent = info.Where;", "async function post(api, body) {\r\n    try {\r\n        return await Sw.Net.Send(\"../Api/x/\" + api, body);\r\n    } catch (e) {\r\n        return null;\r\n    }\r\n}\r\n"),
                ("null helper silent if", "const info = await post(\"Track\", { Id: d.Id });\r\nif (!info)\r\n    return;\r\nstatus.textContent = info.Where;", "function post(api, body) {\r\n    return Sw.Net.Send(\"../Api/x/\" + api, body).catch(() => null);\r\n}\r\n"),
            };
            foreach (var (bn, bc, bx) in bad)
                foreach (var v in Expand("silent", bn, Silent, true, bc, true, false, bx))
                    yield return v;
            //  Fire and forget outside a button (no busy, no "not done"): a listener, a timer, the page start
            var dropped = new (String Name, String Code)[]
            {
                ("dropped send", send + ";"),
                ("dropped then", "Sw.Net.Get(\"../Api/x/GetList\").then(rows => { el.textContent = String(rows.length); });"),
                ("dropped async helper", "saveAll();"),
            };
            foreach (var (dn, dc) in dropped)
                foreach (var w in new (String, Func<String, String>)[]
                {
                    ("listener", c => "el.addEventListener(\"click\", ev => {\r\n    if (Sw.BadClick(ev)) return;\r\n" + c + "\r\n});"),
                    ("change", c => "sel.addEventListener(\"change\", () => {\r\n" + c + "\r\n});"),
                    ("timer", c => "setTimeout(() => {\r\n" + c + "\r\n}, 1000);"),
                    ("keys", c => "Sw.Keys.Add(\"Mod+S\", () => {\r\n" + c + "\r\n}, { Title: _TF(\"Save\", \"c\") });"),
                })
                    yield return new Variant { Family = "silent", Name = dn + " / " + w.Item1, Rules = Silent, Bad = true, Code = Prelude + "async function saveAll() {\r\n    await " + send + ";\r\n}\r\n" + w.Item2(dc) };
            var good = new (String Name, String Code, String Extra)[]
            {
                ("Sw.Fail", "try {\r\n    await " + send + ";\r\n} catch (e) {\r\n    Sw.Fail(e);\r\n}", ""),
                ("returned", "return " + send + ";", ""),
                (".catch Fail", "await " + send + ".catch(e => Sw.Fail(e));", ""),
                ("FailText", "try {\r\n    await " + send + ";\r\n} catch (e) {\r\n    status.textContent = Sw.Net.FailText(_TF(\"Save the note\", \"c\"), e);\r\n}", ""),
                ("allSettled read", "const res = await Promise.allSettled(ids.map(id => Sw.Net.Send(\"../Api/x/Resend\", { Id: id })));\r\nconst bad = res.filter(r => r.status === \"rejected\");\r\nif (bad.length)\r\n    Sw.Fail(bad[0].reason);", ""),
                ("null helper guarded", "const info = await post(\"Track\", { Id: d.Id });\r\nif (!info) {\r\n    Sw.Fail(_TF(\"The carrier didn't answer.\", \"c\"));\r\n    return;\r\n}\r\nstatus.textContent = info.Where;", "function post(api, body) {\r\n    return Sw.Net.Send(\"../Api/x/\" + api, body).catch(() => null);\r\n}\r\n"),
                ("rethrow", "try {\r\n    await " + send + ";\r\n} catch (e) {\r\n    el.classList.remove(\"busy\");\r\n    throw e;\r\n}", ""),
                ("error helper", "try {\r\n    await " + send + ";\r\n} catch (e) {\r\n    showError(e);\r\n}", "function showError(x) { Sw.Fail(x); }\r\n"),
                ("fallback value", "let ok = false;\r\ntry {\r\n    ok = await Sw.Net.Get(\"../Api/x/IsReady\");\r\n} catch {\r\n}\r\nstatus.textContent = ok ? _TF(\"Ready.\", \"c\") : _TF(\"Not ready yet.\", \"c\");", ""),
            };
            //  (a promise returned to an event listener is dropped by the browser: "returned" is right only for the Sw handlers)
            foreach (var (gn, gc, gx) in good)
                foreach (var v in Expand("silent", "right " + gn, Silent, false, gc, true, false, gx).Where(x => !((gn == "returned") && (x.Name.EndsWith("/ listener", StringComparison.Ordinal) || x.Name.EndsWith("/ keydown", StringComparison.Ordinal)))))
                    yield return v;
            foreach (var v in Expand("silent", "returned to a listener", Silent, true, "return " + send + ";", true).Where(x => x.Name.EndsWith("/ listener", StringComparison.Ordinal) || x.Name.EndsWith("/ keydown", StringComparison.Ordinal)))
                yield return v;
        }

        static readonly String[] Tr = { "tr-untranslated" };

        /// <summary>User visible texts that are not translated (TXT-1) x sinks; the right pattern: the same text through _TF.</summary>
        static IEnumerable<Variant> Untranslated()
        {
            var sinks = new (String Name, Func<String, String> Code, bool Action)[]
            {
                ("Sw.Info", s => "Sw.Info(" + s + ");", true),
                ("Sw.Fail", s => "Sw.Fail(" + s + ");", true),
                ("Sw.Toast", s => "Sw.Toast(" + s + ", { Kind: \"warn\" });", true),
                ("Announce", s => "Sw.Page.Announce(" + s + ");", true),
                ("aria-label", s => "el.setAttribute(\"aria-label\", " + s + ");", false),
                ("title", s => "el.title = " + s + ";", false),
                ("placeholder", s => "const f = Sw.el(\"input\", { attrs: { \"aria-label\": _TF(\"Find\", \"c\") } });\r\nf.placeholder = " + s + ";\r\nel.append(f);", false),
                ("Sw.el attrs", s => "el.append(Sw.el(\"button.x\", { attrs: { title: " + s + " } }));", false),
                ("Button Text", s => "el.append(new Sw.Button({ Text: " + s + ", OnClick: () => Sw.Net.Send(\"../Api/x/Go\", 1) }).E);", false),
                ("Confirm", s => "if (!await Sw.Dialog.Confirm(" + s + ", _TF(\"It can't be undone.\", \"c\"), _TF(\"Delete\", \"c\"), { Danger: true })) return;", true),
                ("textContent", s => "status.textContent = " + s + ";", false),
                ("Sw.el text", s => "el.append(Sw.el(\"p\", { text: " + s + " }));", false),
                ("throw", s => "if (!d.Id)\r\n    throw new Error(" + s + ");", true),
            };
            foreach (var (sn, sc, act) in sinks)
            {
                foreach (var v in Expand("untranslated", sn, Tr, true, sc("\"Select a shipment first\""), act, !act))
                    yield return v;
                foreach (var v in Expand("untranslated", "right " + sn, Tr, false, sc("_TF(\"Select a shipment first\", \"c\")"), act, !act))
                    yield return v;
            }
            //  A text thrown in a helper method that a button calls (shown as the reason of "<name>: not done")
            foreach (var v in Expand("untranslated", "throw in a called method", Tr, true, "const id = one(ids);\r\nawait Sw.Net.Send(\"../Api/x/Print\", { Id: id });", true, false, "function one(list) {\r\n    if (list.length !== 1)\r\n        throw new Error(\"Select one shipment first\");\r\n    return list[0];\r\n}\r\n"))
                yield return v;
        }

        /// <summary>Families planted for the first time in round 4 (the Architect's sneak7, the FrontendDev's misuse4): one mistake, its spellings, the right pattern.</summary>
        static IEnumerable<Variant> NewFamilies()
        {
            //  SAF-2: a confirmation that names nothing
            var saf2 = new[] { "ux-saf-2", "design-confirm" };
            foreach (var (tn, title) in new[] { ("are you sure", "_TF(\"Are you sure?\", \"c\")"), ("really", "_TF(\"Really?\", \"c\")"), ("confirm", "_TF(\"Confirm\", \"c\")"), ("continue", "_TF(\"Do you want to continue?\", \"c\")") })
                foreach (var v in Expand("confirm", "vague title " + tn, saf2, true, "if (!await Sw.Dialog.Confirm(" + title + ", \"\", _TF(\"Cancel shipments\", \"c\")))\r\n    return;\r\nawait Sw.Net.Send(\"../Api/x/CancelShipments\", { Ids: ids });", true))
                    yield return v;
            foreach (var v in Expand("confirm", "destructive without Danger", saf2, true, "const ok = await Sw.Dialog.Confirm(_T(\"Cancel {0} shipments?\", ids.length, \"c\"), _TF(\"The customers are told by e-mail.\", \"c\"), _TF(\"Cancel shipments\", \"c\"));\r\nif (ok)\r\n    await Sw.Net.Send(\"../Api/x/CancelShipments\", { Ids: ids });", true))
                yield return v;
            foreach (var v in Expand("confirm", "right danger", saf2, false, "const ok = await Sw.Dialog.Confirm(_T(\"Cancel {0} shipments?\", ids.length, \"c\"), _TF(\"The customers are told by e-mail.\", \"c\"), _TF(\"Cancel shipments\", \"c\"), { Danger: true });\r\nif (ok)\r\n    await Sw.Net.Send(\"../Api/x/CancelShipments\", { Ids: ids });", true))
                yield return v;
            foreach (var v in Expand("confirm", "right harmless", saf2, false, "if (await Sw.Dialog.Confirm(_TF(\"Apply the new settings?\", \"c\"), _TF(\"The page reloads.\", \"c\"), _TF(\"Apply\", \"c\")))\r\n    await Sw.Net.Send(\"../Api/x/ApplySettings\", { Id: d.Id });", true))
                yield return v;
            //  FOC-2: the initial focus on a destructive button
            var foc2 = new[] { "ux-foc-2" };
            foreach (var (fn, fc) in new[] { ("E.focus", "drop.E.focus();"), ("alias", "const de = drop.E;\r\nde.focus();"), ("Focus", "drop.Focus();"), ("rAF", "requestAnimationFrame(() => drop.E.focus());") })
                foreach (var v in Expand("focus", "danger focused " + fn, foc2, true, "const drop = new Sw.Button({ Text: _TF(\"Delete route\", \"c\"), Kind: \"danger\", OnClick: () => Sw.Cells.ConfirmDanger({ Title: _TF(\"Delete the route?\", \"c\"), Confirm: d.Name }) });\r\nel.append(drop.E);\r\n" + fc, false, true))
                    yield return v;
            foreach (var v in Expand("focus", "right first field", foc2, false, "const name = new Sw.TextBox({ Label: _TF(\"Name\", \"c\") });\r\nconst drop = new Sw.Button({ Text: _TF(\"Delete route\", \"c\"), Kind: \"danger\", OnClick: () => Sw.Cells.ConfirmDanger({ Title: _TF(\"Delete the route?\", \"c\"), Confirm: d.Name }) });\r\nel.append(name.E, drop.E);\r\nname.Focus();", false, true))
                yield return v;
            //  ACC-11: a value change that navigates
            var acc11 = new[] { "ux-acc-11" };
            foreach (var (an, ac) in new[]
            {
                ("select change Navigate", "sel.addEventListener(\"change\", () => Sw.Page.Navigate(\"carrier.html?c=\" + encodeURIComponent(sel.value)));"),
                ("onchange location", "sel.onchange = () => { location.href = \"x.html?c=\" + sel.value; };"),
                ("Sw.Select OnChange", "const s = new Sw.Select({ Label: _TF(\"Carrier\", \"c\"), Items: items, OnChange: v => Sw.Page.Navigate(\"carrier.html?c=\" + v) });\r\nel.append(s.E);"),
                ("input listener open", "sel.addEventListener(\"input\", () => window.open(\"x.html?c=\" + sel.value));"),
                ("focus opens dialog", "sel.addEventListener(\"focus\", () => Sw.Dialog.Open({ Title: _TF(\"Pick a carrier\", \"c\"), Content: d.Name }));"),
            })
                foreach (var v in Expand("no surprise", an, acc11, true, ac, false, true))
                    yield return v;
            foreach (var v in Expand("no surprise", "right Go button", acc11, false, "sel.addEventListener(\"change\", () => { status.textContent = _T(\"{0} is selected.\", sel.value, \"c\"); });\r\nel.append(new Sw.Button({ Text: _TF(\"Open carrier\", \"c\"), OnClick: () => Sw.Page.Navigate(\"carrier.html?c=\" + sel.value) }).E);", false, true))
                yield return v;
            //  MOT-3: smooth scrolling that ignores reduced motion
            var mot3 = new[] { "ux-mot-3" };
            foreach (var (mn, mc) in new[] { ("scrollIntoView", "el.scrollIntoView({ behavior: \"smooth\", block: \"nearest\" });"), ("scrollTo", "window.scrollTo({ top: 0, behavior: \"smooth\" });"), ("scrollBy", "el.scrollBy({ top: 200, behavior: 'smooth' });"), ("options var", "const how = { behavior: \"smooth\" };\r\nel.scrollIntoView(how);") })
                foreach (var v in Expand("reduced motion", mn, mot3, true, mc, false, true))
                    yield return v;
            foreach (var v in Expand("reduced motion", "right reduced check", mot3, false, "const reduce = matchMedia(\"(prefers-reduced-motion: reduce)\").matches;\r\nel.scrollIntoView({ behavior: reduce ? \"auto\" : \"smooth\", block: \"nearest\" });", false, true))
                yield return v;
            //  LST-4: a click on a cell copies
            var lst4 = new[] { "ux-lst-4" };
            foreach (var (ln, lc) in new[]
            {
                ("clipboard", "const cell = Sw.el(\"td.ref\", { text: d.Ref });\r\ncell.addEventListener(\"click\", () => { navigator.clipboard.writeText(d.Ref); Sw.Info(_TF(\"Copied\", \"c\")); });\r\nel.append(cell);"),
                ("Sw.Clipboard", "const cell = Sw.el(\"td.value\", { text: d.Ref });\r\nSw.OnClick(cell, () => Sw.Clipboard.Copy(d.Ref));\r\nel.append(cell);"),
                ("onclick", "const cell = Sw.el(\"td\", { text: d.Ref });\r\ncell.onclick = () => navigator.clipboard.writeText(cell.textContent);\r\nel.append(cell);"),
                ("execCommand", "const cell = Sw.el(\"li.code\", { text: d.Ref });\r\ncell.addEventListener(\"click\", () => { getSelection().selectAllChildren(cell); document.execCommand(\"copy\"); });\r\nel.append(cell);"),
            })
                foreach (var v in Expand("click copies", ln, lst4, true, lc, false, true))
                    yield return v;
            foreach (var v in Expand("click copies", "right copy button", lst4, false, "el.append(Sw.el(\"td\", { text: d.Ref }), Sw.CopyButton({ Value: d.Ref, What: _TF(\"Reference\", \"c\") }));", false, true))
                yield return v;
            //  (a code block or a gallery tile that says "Click to copy" is a copy control of its own, not a list cell)
            foreach (var v in Expand("click copies", "right code block", lst4, false, "const pre = Sw.el(\"pre.code\", { title: _TF(\"Copy to clipboard.\", \"c\") });\r\npre.addEventListener(\"click\", ev => { if (!Sw.IsPureEvent(ev)) return; Sw.Clipboard.Copy(pre.textContent); });\r\nel.append(pre);", false, true))
                yield return v;
            //  INP-20: a focusable row without a key
            var inp20 = new[] { "ux-inp-20" };
            foreach (var (rn, rc) in new[]
            {
                ("Sw.el attrs", "const tr = Sw.el(\"tr.row\", { attrs: { tabindex: \"0\" } });\r\nSw.OnClick(tr, () => open(d));\r\nel.append(tr);"),
                ("tabIndex", "const li = document.createElement(\"li\");\r\nli.tabIndex = 0;\r\nli.addEventListener(\"click\", ev => { if (!Sw.BadClick(ev)) open(d); });\r\nel.append(li);"),
                ("setAttribute", "const card = Sw.el(\"div.card\");\r\ncard.setAttribute(\"tabindex\", \"0\");\r\nSw.OnClick(card, () => open(d));\r\nel.append(card);"),
            })
                foreach (var v in Expand("keyboard rows", rn, inp20, true, rc, false, true, "function open(x) { Sw.Page.Navigate(\"item.html?id=\" + x.Id); }\r\n"))
                    yield return v;
            foreach (var v in Expand("keyboard rows", "right KeyClick", inp20, false, "const tr = Sw.el(\"tr.row\", { attrs: { tabindex: \"0\" } });\r\nSw.OnClick(tr, () => open(d));\r\nSw.KeyClick(tr);\r\nel.append(tr);", false, true, "function open(x) { Sw.Page.Navigate(\"item.html?id=\" + x.Id); }\r\n"))
                yield return v;
            foreach (var v in Expand("keyboard rows", "right keydown", inp20, false, "const li = Sw.el(\"li\", { attrs: { tabindex: \"0\" } });\r\nli.addEventListener(\"keydown\", ev => { if (ev.key === \"Enter\") open(d); });\r\nSw.OnClick(li, () => open(d));\r\nel.append(li);", false, true, "function open(x) { Sw.Page.Navigate(\"item.html?id=\" + x.Id); }\r\n"))
                yield return v;
            //  FBK-6: a list load without an empty / error state
            var fbk6 = new[] { "ux-fbk-6" };
            foreach (var (fn, fc) in new[]
            {
                ("for of", "const rows = await Sw.Net.Get(\"../Api/x/GetShipments\") || [];\r\nel.textContent = \"\";\r\nfor (const r of rows)\r\n    el.append(Sw.el(\"li\", { text: r.Name }));"),
                ("forEach", "const list = await Sw.Net.Send(\"../Api/x/ListOrders\", null);\r\nel.replaceChildren();\r\nlist.forEach(r => el.append(Sw.el(\"div.item\", { text: r.Name })));"),
                ("map", "const rows = await Sw.Net.Get(\"../Api/x/GetShipments\");\r\nel.replaceChildren(...rows.map(r => Sw.el(\"li\", { text: r.Name })));"),
            })
                foreach (var v in Expand("list states", fn, fbk6, true, fc, false, true))
                    yield return v;
            foreach (var v in Expand("list states", "right empty", fbk6, false, "const rows = await Sw.Net.Get(\"../Api/x/GetShipments\") || [];\r\nel.textContent = \"\";\r\nif (rows.length === 0)\r\n    el.append(Sw.el(\"p.sw-empty\", { text: _TF(\"No shipments yet.\", \"c\") }));\r\nfor (const r of rows)\r\n    el.append(Sw.el(\"li\", { text: r.Name }));", false, true))
                yield return v;
            //  ACC-3: a component given to the native DOM (it renders "[object Object]"); Sw.Net.Message returns a string since round 4
            var obj = new[] { "ux-txt-30", "ux-acc-3" };
            foreach (var v in Expand("object as text", "right Message", obj, false, "status.textContent = Sw.Net.Message(_TF(\"The message was not sent\", \"c\"), d.Error);\r\nSw.Fail(Sw.Net.Message(_TF(\"Not sent\", \"c\"), d.Error, { Do: _TF(\"Try again later.\", \"c\") }));", false, true))
                yield return v;
            foreach (var (on, oc) in new[]
            {
                ("append card", "el.append(Sw.Card({ Title: _TF(\"Lab\", \"c\"), Body: [] }));"),
                ("append button", "el.appendChild(new Sw.Button({ Text: _TF(\"Go\", \"c\"), OnClick: () => Sw.Net.Send(\"../Api/x/Go\", 1) }));"),
                ("append var", "const tb = new Sw.TextBox({ Label: _TF(\"Name\", \"c\") });\r\nel.append(tb);"),
            })
                foreach (var v in Expand("object as text", on, obj, true, oc, false, true))
                    yield return v;
            foreach (var v in Expand("object as text", "Message .Text", obj, true, "status.textContent = Sw.Net.Message(_TF(\"The message was not sent\", \"c\"), d.Error).Text;", false, true))
                yield return v;
            foreach (var v in Expand("object as text", "right", obj, false, "status.textContent = Sw.Net.MessageParts(_TF(\"The message was not sent\", \"c\"), d.Error).Text;\r\nconst tb = new Sw.TextBox({ Label: _TF(\"Name\", \"c\") });\r\nel.append(tb.E, Sw.Card({ Title: _TF(\"Lab\", \"c\"), Body: [] }).E);", false, true))
                yield return v;
            //  FBK-4: activity work that can't be cancelled or reports nonsense
            var fbk4 = new[] { "ux-fbk-4" };
            foreach (var (an, ac) in new[]
            {
                ("swallowed abort", "Sw.Activity.Run({ Key: \"x.reindex\", Title: _TF(\"Re-indexing\", \"c\") }, async (signal, job) => {\r\n    for (let i = 1; i <= 8; ++i) {\r\n        try { await Sw.Sleep(400, signal); } catch (e) { }\r\n        job.Update({ Progress: i / 8 });\r\n    }\r\n});"),
                ("percent progress", "const job = Sw.Activity.Start({ Key: \"x.up\", Title: _TF(\"Uploading photos\", \"c\") });\r\njob.Update({ Progress: 40, Text: _TF(\"40 of 100\", \"c\") });"),
                ("percent progress expr", "const job = Sw.Activity.Start({ Key: \"x.up\", Title: _TF(\"Uploading photos\", \"c\") });\r\njob.Update({ Progress: Math.round(n / ids.length * 100) });"),
                ("never settles", "const t = new Sw.Toggle({ Label: _TF(\"Mail\", \"c\"), OnChange: v => new Promise(() => { }) });\r\nel.append(t.E);"),
                ("one key two works", "Sw.Activity.Run({ Key: \"x.report\", Title: _TF(\"Weekly sales\", \"c\") }, s => Sw.Net.Send(\"../Api/x/Weekly\", 1, { Signal: s }));\r\nSw.Activity.Run({ Key: \"x.report\", Title: _TF(\"Stock levels\", \"c\") }, s => Sw.Net.Send(\"../Api/x/Stock\", 1, { Signal: s }));"),
            })
                foreach (var v in Expand("activity", an, fbk4, true, ac, false, true))
                    yield return v;
            foreach (var v in Expand("activity", "right", fbk4, false, "Sw.Activity.Run({ Key: \"x.reindex\", Title: _TF(\"Re-indexing\", \"c\") }, async (signal, job) => {\r\n    for (let i = 1; i <= 8; ++i) {\r\n        await Sw.Sleep(400, signal);\r\n        job.Update({ Progress: i / 8 });\r\n    }\r\n});", false, true))
                yield return v;
            //  SAF-11: the draft cleared before the send is known to have worked
            var saf11 = new[] { "ux-saf-11" };
            foreach (var v in Expand("drafts", "clear before send", saf11, true, "const tb = new Sw.TextBox({ Label: _TF(\"Reply\", \"c\"), Multiline: true });\r\nconst draft = Sw.Draft.Attach(tb, { Context: \"x.reply\" });\r\nel.append(tb.E, new Sw.Button({ Text: _TF(\"Send reply\", \"c\"), Kind: \"primary\", OnClick: () => { draft.Clear(); return Sw.Net.Send(\"../Api/x/SendReply\", { Text: tb.Get() }); } }).E);", false, true))
                yield return v;
            foreach (var v in Expand("drafts", "right clear after send", saf11, false, "const tb = new Sw.TextBox({ Label: _TF(\"Reply\", \"c\"), Multiline: true });\r\nconst draft = Sw.Draft.Attach(tb, { Context: \"x.reply\" });\r\nel.append(tb.E, new Sw.Button({ Text: _TF(\"Send reply\", \"c\"), Kind: \"primary\", OnClick: async () => { await Sw.Net.Send(\"../Api/x/SendReply\", { Text: tb.Get() }); draft.Clear(); } }).E);", false, true))
                yield return v;
            //  FBK-2: the save sent as a live / detached request
            foreach (var v in Expand("live save", "Live", new[] { "ux-fbk-2" }, true, "const tb = new Sw.TextBox({ Label: _TF(\"City\", \"c\") });\r\nel.append(tb.E, new Sw.Button({ Text: _TF(\"Save address\", \"c\"), Kind: \"primary\", OnClick: () => Sw.Net.Live(\"../Api/x/SaveAddress\", { City: tb.Get() }) }).E);", false, true))
                yield return v;
            //  POP-6: Read on a write
            foreach (var v in Expand("read on a write", "Read true", new[] { "ux-pop-6" }, true, "Sw.Dialog.Open({ Title: _TF(\"Transfer funds\", \"c\"), Content: d.Name, Buttons: [{ Text: _TF(\"Cancel\", \"c\"), Value: null }, { Text: _TF(\"Transfer\", \"c\"), Kind: \"primary\", Value: 1, OnClick: (c, v, { Signal }) => Sw.Net.Send(\"../Api/x/Transfer\", { Amount: 100 }, { Signal, Read: true }) }] });", false, true))
                yield return v;
            //  ACC-5: a control whose input id is changed (its label no longer points at it)
            foreach (var v in Expand("names", "Input.id", new[] { "ux-acc-5" }, true, "const tb = new Sw.TextBox({ Label: _TF(\"Password\", \"c\"), Type: \"password\" });\r\ntb.Input.id = \"pw\";\r\nel.append(tb.E);", false, true))
                yield return v;
        }

        /// <summary>Css and html families planted for the first time in round 4.</summary>
        static IEnumerable<Variant> CssHtml()
        {
            Variant C(String fam, String name, String rule, bool bad, String css) => new Variant { Family = fam, Name = name, Rules = new[] { rule }, Bad = bad, Code = css, Kind = "css" };
            Variant H(String fam, String name, String rule, bool bad, String html) => new Variant { Family = fam, Name = name, Rules = new[] { rule }, Bad = bad, Code = "<!DOCTYPE html>\r\n<html lang=\"en\">\r\n<head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>T</title><link rel=\"stylesheet\" href=\"../sw/ui.css\"><script defer src=\"../sw/core.js\"></script><script defer src=\"../sw/ui.js\"></script></head>\r\n<body class=\"sw-root\" data-translation_context=\"test page\">\r\n" + html + "\r\n</body>\r\n</html>\r\n", Kind = "html" };
            //  INP-24: small targets
            yield return C("touch targets", "16px icon button", "ux-inp-24", true, ".ship-ico { width: 16px; height: 16px; padding: 0; border: 0; cursor: pointer; }");
            yield return C("touch targets", "btn class", "ux-inp-24", true, ".row-btn { width: 20px; height: 20px; }");
            yield return C("touch targets", "button element", "ux-inp-24", true, ".bar button { height: 18px; min-width: 18px; }");
            yield return C("touch targets", "rem", "ux-inp-24", true, ".close-x { width: 1rem; height: 1rem; cursor: pointer; }");
            yield return C("touch targets", "inline-size", "ux-inp-24", true, ".tool-icon-button { inline-size: 14px; block-size: 14px; }");
            yield return C("touch targets", "right 24", "ux-inp-24", false, ".ship-ico { width: 24px; height: 24px; cursor: pointer; }");
            yield return C("touch targets", "right icon inside", "ux-inp-24", false, ".ship-ico svg { width: 16px; height: 16px; }\r\n.badge { width: 8px; height: 8px; }");
            yield return C("touch targets", "right min size", "ux-inp-24", false, ".ship-ico { width: 16px; height: 16px; min-width: 24px; min-height: 24px; cursor: pointer; }");
            //  LAY-8: a wide minimum
            yield return C("page width", "min-width 1280", "ux-lay-8", true, ".ship-table-wrap { min-width: 1280px; }");
            yield return C("page width", "width 1024", "ux-lay-8", true, ".page-main { width: 1024px; margin: 0 auto; }");
            yield return C("page width", "min-width rem", "ux-lay-8", true, ".layout { min-width: 64rem; }");
            yield return C("page width", "min-inline-size", "ux-lay-8", true, ".report { min-inline-size: 900px; }");
            yield return C("page width", "right max-width", "ux-lay-8", false, ".page-main { max-width: 1280px; width: 100%; }");
            yield return C("page width", "right media", "ux-lay-8", false, "@media (min-width: 1400px) { .page-main { width: 1300px; } }");
            yield return C("page width", "right scroller", "ux-lay-8", false, ".table-scroll { overflow-x: auto; }\r\n.table-scroll table { min-width: 900px; }");
            //  MOT-7: faded out is gone
            yield return C("faded out", "opacity 0", "ux-mot-7", true, ".ship-flash { transition: opacity var(--sw-dur-2); }\r\n.ship-flash.ship-gone { opacity: 0; }");
            yield return C("faded out", "opacity 0 hover", "ux-mot-7", true, ".toolbar { opacity: 0; transition: opacity var(--sw-dur-1); }\r\n@media (hover: hover) { .row:hover .toolbar { opacity: 1; } }");
            yield return C("faded out", "opacity 0.0", "ux-mot-7", true, ".note.is-hidden { opacity: 0.0; }");
            yield return C("faded out", "right visibility", "ux-mot-7", false, ".ship-flash.ship-gone { opacity: 0; visibility: hidden; transition: opacity var(--sw-dur-2), visibility 0s var(--sw-dur-2); }");
            yield return C("faded out", "right pointer events + inert", "ux-mot-7", false, ".ship-flash.ship-gone { opacity: 0; display: none; }");
            yield return C("faded out", "right keyframes", "ux-mot-7", false, "@keyframes fade { from { opacity: 0; } to { opacity: 1; } }");
            //  MOT-3: smooth scrolling in css
            yield return C("reduced motion", "scroll-behavior", "ux-mot-3", true, "html { scroll-behavior: smooth; }");
            yield return C("reduced motion", "right scroll-behavior", "ux-mot-3", false, "@media (prefers-reduced-motion: no-preference) { html { scroll-behavior: smooth; } }");
            //  html: a link without href, words in aria-description, an icon button without a name
            yield return H("links", "a without href", "ux-inp-21", true, "<a class=\"carrier-link\" data-carrier=\"dhl\">Open the carrier site</a>");
            yield return H("links", "a with onclick", "ux-inp-21", true, "<a onclick=\"go()\">Next page</a>");
            yield return H("links", "right a href", "ux-inp-21", false, "<a href=\"carrier.html\">Open the carrier site</a>\r\n<a id=\"top\"></a>");
            yield return H("attributes", "aria-description words", "ux-txt-13", true, "<div class=\"help\" aria-description=\"Shows where each parcel is right now\">x</div>");
            yield return H("attributes", "aria-valuetext words", "ux-txt-13", true, "<div role=\"meter\" aria-valuenow=\"3\" aria-valuetext=\"three of five\">3</div>");
            yield return H("attributes", "data text", "ux-txt-13", true, "<button type=\"button\" data-confirm=\"Delete all rows?\">Delete</button>");
            yield return H("attributes", "right title", "ux-txt-13", false, "<button type=\"button\" title=\"Delete every row of the list\">Delete</button>\r\n<div data-id=\"x-12\" data-kind=\"row\">x</div>");
        }

        // ------------------------------------------------------------------ round 5: near spellings of the claimed families (data-flow facts)

        /// <summary>
        /// Round 5 (Architect sneak8: "the same fact, another syntax"): a secret reaching storage / a log / a url through a helper of any
        /// shape and any wrapper, a log of a secret field's value, a loader whose catch logs and returns an empty collection, the focus moved
        /// to a danger button by any focus call, a double click whose work no click / key reaches.
        /// </summary>
        static IEnumerable<Variant> Round5()
        {
            //  ---- secrets through a helper: helper shapes x wrappers x sinks x secret sources
            var wraps = new (String Name, Func<String, String, String> Expr)[]
            {
                ("btoa", (u, p) => "btoa(" + u + " + \":\" + " + p + ")"),
                ("json", (u, p) => "JSON.stringify({ user: " + u + ", pass: " + p + " })"),
                ("template", (u, p) => "`${" + u + "}:${" + p + "}`"),
                ("encode", (u, p) => "encodeURIComponent(" + p + ")"),
                ("plain", (u, p) => p),
            };
            var sinks = new (String Name, Func<String, String> Code)[]
            {
                ("localStorage", v => "localStorage.setItem(\"app.account\", " + v + ");"),
                ("sessionStorage", v => "sessionStorage.setItem(\"app.account\", " + v + ");"),
                ("Sw.Storage", v => "Sw.Storage.Local.Set(\"app.account\", " + v + ");"),
                ("console.info", v => "console.info(\"remembered\", " + v + ");"),
                ("url", v => "return \"../Api/x/Download?id=\" + n + \"&k=\" + " + v + ";"),
            };
            var shapes = new (String Name, Func<String, String> Def, String Call)[]
            {
                ("member fn", body => "const H = {};\r\nH.remember = function (user, pass) {\r\n    " + body + "\r\n};\r\n", "H.remember"),
                ("object method", body => "const H = {\r\n    remember(user, pass) {\r\n        " + body + "\r\n    },\r\n};\r\n", "H.remember"),
                ("function", body => "function remember(user, pass) {\r\n    " + body + "\r\n}\r\n", "remember"),
                ("arrow", body => "const remember = (user, pass) => {\r\n    " + body + "\r\n};\r\n", "remember"),
                ("module", body => "const H = (() => {\r\n    function remember(user, pass) {\r\n        " + body + "\r\n    }\r\n    return { remember };\r\n})();\r\n", "H.remember"),
            };
            var sources = new (String Name, String Decl, String Expr)[]
            {
                ("field", "const pass = new Sw.TextBox({ Label: _TF(\"Encryption passphrase\", \"c\"), Type: \"password\" });\r\n", "pass.Get()"),
                ("handed on", "const f = { pass: new Sw.TextBox({ Label: _TF(\"Password\", \"c\"), Type: \"password\" }) };\r\nconst pass = f.pass;\r\n", "f.pass.Get()"),
                ("variable", "const pw = new Sw.TextBox({ Label: _TF(\"Password\", \"c\"), Type: \"password\" });\r\n", "pw.Get()"),
            };
            int k = 0;
            foreach (var (wn, we) in wraps)
                foreach (var (sn, sc) in sinks)
                    foreach (var (hn, hd, hc) in shapes)
                    {
                        var (on, od, oe) = sources[k++ % sources.Length];
                        var def = hd(sc(we("user", "pass")));
                        foreach (var v in Expand("helper secrets", wn + " / " + sn + " / " + hn + " <- " + on, Sec, true, hc + "(d.User, " + oe + ");", true, false, od + def).Where((x, i) => i % 4 == k % 4))
                            yield return v;
                        //  the right pattern: the same helper given no secret
                        if ((k % 3) == 0)
                            foreach (var v in Expand("helper secrets", "right " + wn + " / " + sn + " / " + hn, Sec, false, hc + "(d.User, d.Theme);", true, false, def).Where((x, i) => i % 4 == k % 4))
                                yield return v;
                    }
            //  ---- a secret field's value in a log, any log call, any way to read the field
            var logs = new[] { "console.log", "console.info", "console.debug", "console.warn", "console.error", "console.trace" };
            var reads = new (String Name, String Decl, String Expr)[]
            {
                ("member", "", "this.Pass.Get()"),
                ("local", "const pass = new Sw.TextBox({ Label: _TF(\"Passphrase\", \"c\"), Type: \"password\" });\r\n", "pass.Get()"),
                ("json", "const pass = new Sw.TextBox({ Label: _TF(\"Passphrase\", \"c\"), Type: \"password\" });\r\n", "JSON.stringify({ id: n, passphrase: pass.Get() })"),
                ("template", "const pass = new Sw.TextBox({ Label: _TF(\"API key\", \"c\") });\r\n", "`key ${pass.Get()}`"),
            };
            foreach (var l in logs)
                foreach (var (rn, rd, re) in reads)
                {
                    var code = rn == "member"
                        ? "class Page {\r\n    constructor() {\r\n        this.Pass = new Sw.TextBox({ Label: _TF(\"Encryption passphrase\", \"c\"), Type: \"password\" });\r\n    }\r\n    Save() {\r\n        " + l + "(\"passphrase\", " + re + ");\r\n        return Sw.Net.Send(\"../Api/x/Save\", {});\r\n    }\r\n}\r\n"
                        : rd + l + "(\"value\", " + re + ");\r\n";
                    yield return new Variant { Family = "log secrets", Name = l + " <- " + rn, Rules = Sec, Bad = true, Code = Prelude + code };
                }
            yield return new Variant { Family = "log secrets", Name = "right: log the length", Rules = Sec, Bad = false, Code = Prelude + "const pass = new Sw.TextBox({ Label: _TF(\"Passphrase\", \"c\"), Type: \"password\" });\r\nconsole.info(\"passphrase length\", pass.Get().length);\r\n" };
            yield return new Variant { Family = "log secrets", Name = "right: log the user", Rules = Sec, Bad = false, Code = Prelude + "const user = new Sw.TextBox({ Label: _TF(\"User name\", \"c\") });\r\nconsole.info(\"user\", user.Get());\r\n" };

            //  ---- a loader whose catch only logs and returns an empty collection; the caller renders "no items"
            var empties = new[] { "[]", "{}", "new Map()" };
            var loaders = new (String Name, Func<String, String> Def, String Call)[]
            {
                ("member fn", c => "const H = {};\r\nH.load = async function (url) {\r\n    try {\r\n        return await Sw.Net.Get(url);\r\n    } catch (e) {\r\n        console.debug(e);\r\n        return " + c + ";\r\n    }\r\n};\r\n", "H.load(\"../Api/x/GetRows\")"),
                ("function", c => "async function load() {\r\n    try {\r\n        return await Sw.Net.Read(\"../Api/x/GetRows\", {});\r\n    } catch (e) {\r\n        console.warn(\"load failed\", e);\r\n        return " + c + ";\r\n    }\r\n}\r\n", "load()"),
                ("arrow catch", c => "const load = () => Sw.Net.Get(\"../Api/x/GetRows\").catch(e => { console.warn(e); return " + c + "; });\r\n", "load()"),
                ("quiet catch", c => "async function load() {\r\n    try {\r\n        return await Sw.Net.Get(\"../Api/x/GetRows\");\r\n    } catch {\r\n        return " + c + ";\r\n    }\r\n}\r\n", "load()"),
            };
            var silent5 = new[] { "ux-fbk-5" };
            foreach (var c in empties)
                foreach (var (ln, ld, lc) in loaders)
                {
                    if ((ln == "arrow catch") && (c == "new Map()"))
                        continue;
                    foreach (var v in Expand("empty on failure", ln + " -> " + c, silent5, true, "const rows = await " + lc + ";\r\nfor (const r of Object.values(rows))\r\n    el.append(Sw.el(\"div\", { text: r.Name }));", true, false, ld(c)).Where((x, i) => i % 3 == ln.Length % 3))
                        yield return v;
                }
            foreach (var v in Expand("empty on failure", "right: fail and rethrow", silent5, false, "const rows = await load();\r\nfor (const r of rows)\r\n    el.append(Sw.el(\"div\", { text: r.Name }));", true, false, "async function load() {\r\n    try {\r\n        return await Sw.Net.Get(\"../Api/x/GetRows\");\r\n    } catch (e) {\r\n        Sw.Fail(e);\r\n        throw e;\r\n    }\r\n}\r\n"))
                yield return v;
            foreach (var v in Expand("empty on failure", "right: a pure helper with a fallback", silent5, false, "status.textContent = keyOf(d.Url);", true, false, "function keyOf(url) {\r\n    try {\r\n        return new URL(url, location.href).pathname;\r\n    } catch {\r\n        return \"\";\r\n    }\r\n}\r\n"))
                yield return v;

            //  ---- focus moved to a danger button: any focus call x local / member x Kind / Danger
            var makes = new (String Name, String Make, String Ref)[]
            {
                ("local kind", "const del = new Sw.Button({ Text: _TF(\"Delete all\", \"c\"), Kind: \"danger\", OnClick: () => Sw.Net.Send(\"../Api/x/DeleteAll\", {}) });\r\n", "del"),
                ("local danger", "const del = new Sw.Button({ Text: _TF(\"Erase\", \"c\"), Danger: true, OnClick: () => Sw.Net.Send(\"../Api/x/Erase\", {}) });\r\n", "del"),
                ("member", "this.Del = new Sw.Button({ Text: _TF(\"Delete all\", \"c\"), Kind: \"danger\", OnClick: () => Sw.Net.Send(\"../Api/x/DeleteAll\", {}) });\r\n", "this.Del"),
            };
            var focusCalls = new (String Name, Func<String, String> Code)[]
            {
                ("Focus()", r => r + ".Focus();"),
                ("E.focus()", r => r + ".E.focus();"),
                ("Sw.Focus.Set", r => "Sw.Focus.Set(" + r + ".E);"),
                ("Sw.Focus.Show", r => "Sw.Focus.Show(" + r + ".E);"),
                ("timer", r => "setTimeout(() => " + r + ".Focus(), 0);"),
            };
            var foc = new[] { "ux-foc-2" };
            foreach (var (mn, mm, mr) in makes)
                foreach (var (fn, fc) in focusCalls)
                {
                    var body = mm + fc(mr);
                    var code = mr.StartsWith("this.", StringComparison.Ordinal) ? "class Page {\r\n    Start() {\r\n" + body + "\r\n    }\r\n}\r\n" : "Sw.Page.Main(() => {\r\n" + body + "\r\n});\r\n";
                    yield return new Variant { Family = "danger focus", Name = mn + " / " + fn, Rules = foc, Bad = true, Code = Prelude + code };
                    var good = body.Replace("Kind: \"danger\"", "Kind: \"primary\"").Replace("Danger: true", "Kind: \"primary\"").Replace("Delete all", "Refresh").Replace("Erase", "Refresh");
                    var gcode = mr.StartsWith("this.", StringComparison.Ordinal) ? "class Page {\r\n    Start() {\r\n" + good + "\r\n    }\r\n}\r\n" : "Sw.Page.Main(() => {\r\n" + good + "\r\n});\r\n";
                    yield return new Variant { Family = "danger focus", Name = "right " + mn + " / " + fn, Rules = foc, Bad = false, Code = Prelude + gcode };
                }

            //  ---- a double click whose work no click / key of the element reaches
            var dbl = new[] { "ux-inp-6" };
            var opens = new (String Name, Func<String, String> Code)[]
            {
                ("listener + select click", o => "row.addEventListener(\"click\", ev => { if (Sw.ModifierClick(ev)) return; picked = d; });\r\nrow.addEventListener(\"dblclick\", () => " + o + ");"),
                ("ondblclick + select click", o => "row.onclick = () => { picked = d; };\r\nrow.ondblclick = () => " + o + ";"),
                ("Sw.el on", o => "const row2 = Sw.el(\"tr\", { on: { click: () => { picked = d; }, dblclick: () => " + o + " } });\r\nlist.append(row2);"),
                ("only dblclick", o => "row.addEventListener(\"dblclick\", () => " + o + ");"),
            };
            const String rowDecl = "let picked = null;\r\nconst list = Sw.el(\"tbody\");\r\nconst row = Sw.el(\"tr\");\r\nlist.append(row);\r\n";
            const String openFn = "function openItem(x) {\r\n    return Sw.Dialog.Open({ Title: x.Name, Content: x.Note });\r\n}\r\n";
            foreach (var (on, oc) in opens)
                foreach (var v in Expand("double click", on, dbl, true, rowDecl + oc("openItem(d)"), false, true, openFn))
                    yield return v;
            var dblGood = new (String Name, String Code)[]
            {
                ("click opens too", "row.addEventListener(\"click\", ev => { if (Sw.ModifierClick(ev)) return; openItem(d); });\r\nrow.addEventListener(\"dblclick\", () => openItem(d));"),
                ("key opens", "row.addEventListener(\"keydown\", ev => { if (ev.key === \"Enter\") openItem(d); });\r\nrow.addEventListener(\"dblclick\", () => openItem(d));"),
                ("KeyClick", "Sw.KeyClick(row, () => openItem(d));\r\nrow.addEventListener(\"dblclick\", () => openItem(d));"),
            };
            foreach (var (gn, gc) in dblGood)
                foreach (var v in Expand("double click", "right " + gn, dbl, false, rowDecl + gc, false, true, openFn))
                    yield return v;
        }

        public static IEnumerable<Variant> All() => Secrets().Concat(RawHtml()).Concat(Formatting()).Concat(Sentences()).Concat(Pointer()).Concat(SilentFailures()).Concat(Untranslated()).Concat(NewFamilies()).Concat(CssHtml()).Concat(Round5());

        static Dictionary<String, Variant> byKey;

        static Variant Get(String key)
        {
            if (byKey == null)
            {
                var d = new Dictionary<String, Variant>(StringComparer.Ordinal);
                foreach (var v in All())
                    d[v.ToString()] = v;
                byKey = d;
            }
            return byKey[key];
        }

        public static IEnumerable<object[]> BadKeys() => All().Where(x => x.Bad).Select(x => new object[] { x.ToString() }).Distinct(new KeyCmp());
        public static IEnumerable<object[]> GoodKeys() => All().Where(x => !x.Bad).Select(x => new object[] { x.ToString() }).Distinct(new KeyCmp());

        sealed class KeyCmp : IEqualityComparer<object[]>
        {
            public bool Equals(object[] a, object[] b) => (String)a[0] == (String)b[0];
            public int GetHashCode(object[] a) => ((String)a[0]).GetHashCode();
        }

        public static List<Finding> Run(Variant v)
        {
            var f = new List<Finding>();
            var opts = new CheckOptions { Design = true };
            if (v.Kind == "css")
                Checker.CheckCssFile(new Checker.FileContext("page/x.css", new SourceText(v.Code), opts, f));
            else if (v.Kind == "html")
                Checker.CheckHtmlFile(new Checker.FileContext("page/x.html", new SourceText(v.Code), opts, f));
            else
                Checker.CheckJsFile(new Checker.FileContext("page/x.js", new SourceText(Header + v.Code), opts, f));
            return f;
        }

        [Theory]
        [MemberData(nameof(BadKeys))]
        public void BadVariantIsFound(String key)
        {
            var v = Get(key);
            var f = Run(v);
            Assert.True(f.Any(x => v.Rules.Contains(x.Rule.Id)), key + ": none of " + String.Join("|", v.Rules) + " in\r\n" + v.Code + "\r\nfound: " + String.Join(", ", f.Select(x => x.Rule.Id).Distinct()));
        }

        [Theory]
        [MemberData(nameof(GoodKeys))]
        public void RightVariantIsLeftAlone(String key)
        {
            var v = Get(key);
            var f = Run(v).Where(x => v.Rules.Contains(x.Rule.Id)).ToList();
            Assert.True(f.Count == 0, key + ": " + String.Join("; ", f.Select(x => x.Line + " " + x.Rule.Id + ": " + x.Message)) + "\r\n" + v.Code);
        }

        /// <summary>The catch rate per family (printed).</summary>
        [Fact]
        public void CatchRatePerFamily()
        {
            int all = 0, ok = 0;
            //  SWWEB_VARIANT_DUMP=<file>: every variant that fails, with its code and findings (to tune the rules)
            var dump = Environment.GetEnvironmentVariable("SWWEB_VARIANT_DUMP");
            var sb = new System.Text.StringBuilder();
            foreach (var g in All().GroupBy(x => x.Family))
            {
                int bad = 0, found = 0, good = 0, clean = 0;
                foreach (var v in g)
                {
                    var f = Run(v);
                    bool pass;
                    if (v.Bad)
                    {
                        ++bad;
                        if (pass = f.Any(x => v.Rules.Contains(x.Rule.Id)))
                            ++found;
                    }
                    else
                    {
                        ++good;
                        if (pass = !f.Any(x => v.Rules.Contains(x.Rule.Id)))
                            ++clean;
                    }
                    if (!pass)
                        sb.Append("==== ").Append(v).Append("\r\n").Append(v.Code).Append("\r\n---- ").Append(String.Join("; ", f.Select(x => x.Line + " " + x.Rule.Id + (v.Rules.Contains(x.Rule.Id) ? ": " + x.Message : "")))).Append("\r\n");
                }
                Out.WriteLine(g.Key + ": " + found + " of " + bad + " mistakes found, " + clean + " of " + good + " right variants clean");
                all += bad + good;
                ok += found + clean;
            }
            Out.WriteLine("all: " + ok + " of " + all);
            if (!String.IsNullOrEmpty(dump))
                System.IO.File.WriteAllText(dump, sb.ToString());
        }
    }
}
