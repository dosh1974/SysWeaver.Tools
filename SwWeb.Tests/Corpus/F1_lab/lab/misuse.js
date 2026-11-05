/** Misuse patterns the docs warn about or that silently break (critic 4). */
Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: "Misuse", Description: "Bad patterns." });
    const ta = Sw.el("textarea");
    page.Add(ta);
    // 1 draft never disposed, in a dialog
    const h = Sw.Dialog.Open({ Title: "Comment", Content: ta, Buttons: [{ Text: "Cancel", Value: null }, { Text: "OK", Value: 1 }] });
    Sw.Draft.Attach(ta, { Context: "c" });
    // 2 activity listener never removed in a framed page
    Sw.Activity.On(e => console.log(e));
    // 3 copy button without a value
    page.Add(Sw.CopyButton({}));
    // 4 disabled native select
    const s = Sw.el("select"); page.Add(s); Sw.Disabled.Set(s, true, "no");
    // 5 raw disabled attribute on a Sw button
    const b = new Sw.Button({ Text: "Save" }); b.E.setAttribute("disabled", ""); page.Add(b);
    // 6 dialog busy with no Cancel, never ends
    h.SetBusy(new Promise(() => {}));
    // 7 typo library name
    Sw.Ready("Tabel", T => T);
    // 8 overriding a built-in cell format
    Sw.Extend.CellFormat("Number", (e, v) => e);
    // 9 Activity on a standalone page (no list)
    Sw.Activity.Start({ Title: "Invisible export" });
    // 10 native confirm
    if (confirm("Delete?")) console.log(1);
    // 11 page dirty never cleared after save
    Sw.Page.SetDirty("x", true);
});
