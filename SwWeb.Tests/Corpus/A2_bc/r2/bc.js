/**
 * @file r2/bc.js - round 2: new patterns at run time and backward compatibility of the round 1 defaults.
 */
"use strict";
Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Round 2 checks", "t"), Description: _TF("Run time checks.", "t") });
    const W = window; W.__n = {};
    const inc = k => { W.__n[k] = (W.__n[k] || 0) + 1; };
    const send = (k, url, data) => { inc(k); return Sw.Net.Send(url, data); };
    const B = (id, o) => { const b = new Sw.Button(o); b.E.dataset.t = id; return b; };

    //  T1 a button that starts a long poll (live view) and returns nothing
    const t1 = B("t1", { Text: _TF("Start live view", "t"), OnClick: () => { startLive(); } });
    function startLive() { send("t1", "../Api/naive/Poll", null).then(() => inc("t1done")); }
    //  T2 request in a helper (V13)
    const t2 = B("t2", { Text: _TF("Save 13", "t"), OnClick: () => { doSave(); } });
    function doSave() { send("t2", "../Api/naive/Save", 2); }
    //  T3 request in a timer (V14)
    const t3 = B("t3", { Text: _TF("Save 14", "t"), OnClick: () => { setTimeout(() => send("t3", "../Api/naive/Save", 3), 0); } });
    //  T18 work > 2 s then leave
    const t18 = B("t18", { Text: _TF("Long export", "t"), OnClick: () => send("t18", "../Api/naive/Export", null) });
    const c1 = Sw.Card({ Title: _TF("Busy", "t"), Body: [Sw.el("div.sw-row", null, t1.E, t2.E, t3.E, t18.E)] });

    //  T4 a non primary save button: after the save the card is still dirty?
    const name4 = new Sw.TextBox({ Label: _TF("Display name", "t") });
    const t4 = B("t4", { Text: _TF("Save", "t"), OnClick: () => send("t4", "../Api/naive/Save", name4.Get()) });
    //  T4b save by Enter in the box
    const name4b = new Sw.TextBox({ Label: _TF("Nick name", "t"), OnEnter: v => send("t4b", "../Api/naive/Save", v) });
    const c4 = Sw.Card({ Title: _TF("Profile", "t"), Body: [Sw.el("div.sw-stack", null, name4.E, name4b.E, Sw.el("div.sw-row", null, t4.E))] });

    //  T5 a filter box with OnInput (no search icon)
    const items = Sw.el("ul.v5", null, ...["alpha", "beta", "gamma"].map(x => Sw.el("li", null, x)));
    const flt = new Sw.TextBox({ Label: _TF("Filter", "t"), OnInput: v => { for (const li of items.children) li.hidden = !li.textContent.includes(v); } });
    //  T5b a calculator field (no save at all)
    const calc = new Sw.NumberBox ? new Sw.NumberBox({ Label: _TF("Bytes", "t"), Value: 1024, OnInput: () => 0 }) : null;
    const c5 = Sw.Card({ Title: _TF("Tools", "t"), Body: [Sw.el("div.sw-stack", null, flt.E, items, calc ? calc.E : "")] });

    //  T6 a setting in a toolbar-classed row
    const tb6 = new Sw.TextBox({ Label: _TF("Server url", "t") });
    const save6 = B("t6", { Text: _TF("Save url", "t"), Kind: "primary", OnClick: () => send("t6", "../Api/naive/SetUrl", tb6.Get()) });
    const c6 = Sw.Card({ Title: _TF("Connection", "t"), Body: [Sw.el("div.sw-toolbar", null, tb6.E, save6.E)] });

    //  T7 non destructive confirm, Enter
    const t7 = B("t7", { Text: _TF("Apply settings", "t"), OnClick: async () => { W.__t7 = await Sw.Dialog.Confirm(_TF("Apply the new settings?", "t"), _TF("The service reloads its settings.", "t"), _TF("Apply", "t")); } });
    //  T8 dialog filled from code after an async load, then an outside click
    const t8 = B("t8", { Text: _TF("Edit (async load)", "t"), OnClick: () => {
        const tb = new Sw.TextBox({ Label: _TF("Title", "t") });
        const tg = new Sw.Toggle({ Label: _TF("Enabled", "t") });
        const sel = new Sw.Select({ Label: _TF("Kind", "t"), Items: [{ Text: "A", Value: "a" }, { Text: "B", Value: "b" }] });
        setTimeout(() => { tb.Set("Loaded title"); tg.Set(true); sel.Set("b"); W.__t8loaded = 1; }, 600);
        W.__t8 = Sw.Dialog.Show({ Title: _TF("Edit the item", "t"), Content: Sw.el("div.sw-stack", null, tb.E, tg.E, sel.E), Buttons: [{ Text: _TF("Cancel", "t") }, { Text: _TF("Save", "t"), Kind: "primary", OnClick: () => Sw.Net.Send("../Api/naive/Save", 8) }] });
    } });
    //  T8b raw input filled from code after an async load
    const t8b = B("t8b", { Text: _TF("Edit raw (async load)", "t"), OnClick: () => {
        const inp = Sw.el("input.v8b", { attrs: { "aria-label": "Raw title" } });
        setTimeout(() => { inp.value = "Loaded raw"; inp.dispatchEvent(new Event("input", { bubbles: true })); W.__t8bloaded = 1; }, 600);
        W.__t8b = Sw.Dialog.Show({ Title: _TF("Edit raw", "t"), Content: Sw.el("div.sw-stack", null, inp), Buttons: [{ Text: _TF("Cancel", "t") }, { Text: _TF("Save", "t"), Kind: "primary" }] });
    } });
    //  T10 custom danger dialog with Default (unknown option)
    const t10 = B("t10", { Text: _TF("Delete report", "t"), Kind: "danger", OnClick: () => Sw.Dialog.Show({ Title: _TF("Delete the report?", "t"), Buttons: [{ Text: _TF("Cancel", "t") }, { Text: _TF("Delete", "t"), Kind: "danger", Default: true, OnClick: () => send("t10", "../Api/naive/DeleteReport", null) }] }) });
    //  T11 Disabled with an expression
    let ready = false;
    const t11 = B("t11", { Text: _TF("Run", "t"), Disabled: !ready });
    //  T13 Sw.Card OnClick
    const c13 = Sw.Card({ Title: _TF("Open details", "t"), Body: [_TF("A clickable card.", "t")], OnClick: () => inc("t13") });
    //  T14 dialog save then Cancel: the save reached the server
    const t14 = B("t14", { Text: _TF("Edit and cancel", "t"), OnClick: () => {
        const tb = new Sw.TextBox({ Label: _TF("Amount", "t") });
        W.__t14 = Sw.Dialog.Show({ Title: _TF("Transfer", "t"), Content: Sw.el("div.sw-stack", null, tb.E), Buttons: [{ Text: _TF("Cancel", "t") }, { Text: _TF("Transfer", "t"), Kind: "primary", OnClick: async () => { await send("t14", "../Api/naive/Transfer", tb.Get()); } }] });
    } });
    //  T15 Activity.Track thenable from OnClick
    const t15 = B("t15", { Text: _TF("Export (activity)", "t"), OnClick: () => Sw.Activity.Track(send("t15", "../Api/naive/Export", null), { Key: "r2.export", Title: _TF("Exporting r2.xlsx", "t") }) });
    //  T16 two Working dialogs without Id, both to the background
    const t16 = B("t16", { Text: _TF("Two jobs", "t"), OnClick: () => {
        W.__w1 = Sw.Dialog.Working({ Title: _TF("Job one", "t"), Promise: send("t16", "../Api/naive/Export", 1) });
    } });
    //  T12 contenteditable
    const ed = Sw.el("div.v-ed", { attrs: { contenteditable: "true", "aria-label": "Notes", role: "textbox" } });
    const c7 = Sw.Card({ Title: _TF("Dialogs", "t"), Body: [Sw.el("div.sw-row", null, t7.E, t8.E, t8b.E, t10.E, t11.E, t14.E, t15.E, t16.E), ed] });
    page.Add(c1, c4, c5, c6, c7, c13);
});
//  r2c: one control at a time, filled from code after a delay (window.__fill(kind, ms))
window.__fill = (kind, ms, typeFirst) => {
    const W = window;
    let c;
    if (kind === "text") c = new Sw.TextBox({ Label: "Title" });
    else if (kind === "toggle") c = new Sw.Toggle({ Label: "Enabled" });
    else if (kind === "select") c = new Sw.Select({ Label: "Kind", Items: [{ Text: "A", Value: "a" }, { Text: "B", Value: "b" }] });
    else if (kind === "raw") { c = { E: Sw.el("input.vr", { attrs: { "aria-label": "Raw" } }), Set: v => { c.E.value = v; c.E.dispatchEvent(new Event("input", { bubbles: true })); } }; }
    setTimeout(() => { c.Set(kind === "toggle" ? true : kind === "select" ? "b" : "Loaded"); W.__filled = kind; }, ms);
    W.__fillDlg = Sw.Dialog.Show({ Title: "Fill " + kind, Content: Sw.el("div.sw-stack", null, c.E), Buttons: [{ Text: "Cancel" }, { Text: "Save", Kind: "primary" }] });
    return 1;
};
window.__closeAll = () => { let n = 0; for (let t = Sw.Dialog.Top(); t && n < 6; t = Sw.Dialog.Top(), n++) t.Handle.Close(null); return n; };
