/**
 * @file sneak5/v.js - round 2 planted mistakes (new patterns).
 */
"use strict";
Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("V", "t"), Description: _TF("V", "t") });
    let ready = false;
    const go = document.createElement("button");
    Object.assign(go, { disabled: true });                                         // V1 disabled via Object.assign
    const b3 = new Sw.Button({ Text: _TF("Run", "t"), Disabled: !ready });         // V3 Disabled expression, no reason
    const box = Sw.el("div.v-box");
    const { confirm: ask } = window;                                               // V5 destructured native confirm
    if (ask("Sure?")) ready = true;
    await Sw.Dialog.Confirm(_TF("Archive?", "t"), _TF("Archive it", "t"), { OkText: _T("OK") });   // V6 OkText "OK"
    const clr = new Sw.Button({ Text: _TF("Clear all", "t"), OnClick: () => Sw.Net.Send("../Api/naive/ClearAll", null) });   // V7 destructive verb not in the list
    const purge = new Sw.Button({ Text: _TF("Purge", "t"), OnClick: purgeNow });   // V8 destructive by a named handler
    function purgeNow() { return Sw.Net.Send("../Api/naive/PurgeAll", null); }
    const del = new Sw.Button({                                                     // V9 custom danger dialog, Delete is the default
        Text: _TF("Delete report", "t"), Kind: "danger",
        OnClick: () => Sw.Dialog.Show({ Title: _TF("Delete the report?", "t"), Buttons: [{ Text: _TF("Cancel", "t") }, { Text: _TF("Delete", "t"), Kind: "danger", Default: true, OnClick: () => Sw.Net.Send("../Api/naive/DeleteReport", null) }] }),
    });
    const item = Sw.el("div.v-item", { onclick: () => open() }, _TF("Item", "t"));   // V10 onclick in Sw.el attributes
    const art = Sw.el("article.v-art", null, _TF("Article", "t"));
    art.addEventListener("click", () => open());                                   // V12 click on an article
    const s13 = new Sw.Button({ Text: _TF("Save 13", "t"), OnClick: () => { doSave(); } });   // V13 request in a helper
    const s14 = new Sw.Button({ Text: _TF("Save 14", "t"), OnClick: () => { setTimeout(() => Sw.Net.Send("../Api/naive/Save", 14), 0); } });   // V14 request in a timer
    const s15 = new Sw.Button({ Text: _TF("Save 15", "t"), OnClick: async () => { Sw.Net.Send("../Api/naive/Save", 15); } });   // V15 async without await
    const s16 = new Sw.Button({ Text: _TF("Apply", "t"), OnClick: async () => { await Sw.Net.Send("../Api/naive/SetName", 1); Sw.Info(_TF("Saved", "t")); } });   // V16 toast for an in-place result
    const ed = Sw.el("div.v-ed", { contentEditable: "true" });                     // V17 contenteditable, no dirty
    const s17 = new Sw.Button({ Text: _TF("Save notes", "t"), OnClick: () => Sw.Net.Send("../Api/naive/SaveNotes", ed.textContent) });
    ed.addEventListener("input", () => { sessionStorage["draft_notes"] = ed.textContent; });   // V19 own draft by bracket
    const wrap = Sw.el("div.v-wrap");
    wrap.innerHTML = "<textarea class='v-ta'></textarea>";                          // V20 textarea from a string: no label, no draft
    const t = document.createElement("TABLE");                                     // V22 hand-made table, upper case
    const list = Sw.el("ul.v-list");
    const poll = async () => { const rows = await Sw.Net.Get("../Api/naive/Rows"); list.innerHTML = rows.map(r => "<li>" + r.Name + "</li>").join(""); setTimeout(poll, 2000); };   // V23 rebuild by a setTimeout loop
    poll();
    box.style.setProperty("transition", "opacity 250ms");                          // V26 transition by setProperty
    const dlg = Sw.el("dialog.v-dlg", null, _TF("Hi", "t"));                       // V28 own dialog element
    document.body.append(dlg); dlg.show();
    document.documentElement.style.cursor = "progress";                            // V29 page wide busy cursor on html
    document.addEventListener("keydown", e => { if (e.getModifierState("Alt") && e.key === "ArrowLeft") open(); });   // V30 Alt+Left by getModifierState
    page.Add(go, b3.E, box, clr.E, purge.E, del.E, item, art, s13.E, s14.E, s15.E, s16.E, ed, s17.E, wrap, t, list);
    function open() { }
    function doSave() { Sw.Net.Send("../Api/naive/Save", 13); }
});
