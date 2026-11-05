/**
 * @file sneak4/s.js - ux rule variants.
 */
"use strict";
Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("S", "t"), Description: _TF("S", "t") });
    const save = document.createElement("button");
    save.disabled = true;                                    // U1 name without btn
    save.setAttribute("disabled", "");                       // U2
    const el = document.createElement("div");
    el.style.transition = "opacity 200ms";                   // U3
    el.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 300 });  // U4
    el.addEventListener("pointerup", () => run());           // U5 release only
    el.onmouseup = () => run();                              // U6
    document.addEventListener("contextmenu", e => e.preventDefault());  // U7
    el.setAttribute("tabindex", "2");                        // U8
    const a = window.alert; a("x");                          // U9
    await Sw.Dialog.Alert(_TF("Done", "t"), _TF("Done", "t"), _TF("OK", "t"));   // U10
    await Sw.Dialog.Confirm(_TF("Remove the row?", "t"), _TF("Gone", "t"), _TF("Yes", "t"), { Danger: true });  // U11
    Sw.Dialog.Show({ Title: _TF("T", "t"), Buttons: [{ Text: _TF("No", "t") }] });  // U12
    const b = new Sw.Button({ Text: _TF("Save", "t"), Title: _TF("Save", "t") });  // U13 tip repeats
    const c = new Sw.Button({ Text: _TF("Go", "t"), Disabled: true });             // U14 no reason
    c.SetDisabled(true);                                                            // U15 no reason
    const d = new Sw.Button({ Text: _TF("Send", "t"), OnClick: () => { Sw.Net.Send("../Api/x", 1); } });  // U16 no return
    el.addEventListener("click", () => run());               // U17 no BadClick on a div
    document.addEventListener("keydown", e => { if (e.altKey && e.key === "ArrowLeft") run(); });  // U18
    Sw.Fail(_TF("The name is required", "t"));               // U19
    el.addEventListener("selectstart", e => e.preventDefault());  // U20
    window.onbeforeunload = () => "x";                       // U21 own beforeunload
    localStorage.setItem("mydraft", "x");                    // U22 own draft
    const t = document.createElement("table");               // U23 hand-made table
    page.Add(save, el, b, c, d, t);
    function run() { }
});
