/**
 * @file good/g5.js - shift notes: a drafted text, Ctrl+S, a calm offline error, a reversible archive with Undo.
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Shift notes", "Page title"), Description: _TF("Notes for the next shift.", "Page description") });
    document.body.append(page.E);

    const notes = new Sw.TextBox({
        Label: _TF("Notes for the next shift", "Field label of the shift notes"),
        Multiline: true,
        Rows: 8,
        Draft: "shiftNotes",
    });
    const save = new Sw.Button({
        Text: _TF("Save", "Button that saves the shift notes"),
        Title: _TF("Save the notes for the next shift", "Tool tip"),
        Kind: "primary",
        Shortcut: "Mod+S",
        OnClick: async () => {
            await Sw.Net.Send("../Api/shift/SaveNotes", notes.Get());
            notes.ClearDraft();
        },
    });
    Sw.Keys.Add("Mod+S", () => save.Click(), { Title: _TF("Save the shift notes", "Keyboard shortcut help"), InInputs: true });

    const archive = new Sw.Button({
        Text: _TF("Archive notes", "Button that moves the notes to the archive"),
        Title: _TF("Move the current notes to the archive; you can undo it", "Tool tip"),
        OnClick: async () => {
            await Sw.Net.Send("../Api/shift/Archive", null);
            Sw.Toast(_TF("The notes were archived.", "Toast after archiving the shift notes"), {
                Kind: "ok",
                Action: { Text: _TF("Undo", "Toast button"), OnClick: () => Sw.Net.Send("../Api/shift/Unarchive", null) },
            });
        },
    });

    page.Add(Sw.Card({ Title: _TF("Notes", "Card title"), Body: [notes, Sw.ButtonRow(save, archive)] }));

    try {
        const cur = await Sw.Net.Get("../Api/shift/GetNotes");
        if (cur && !notes.Get())
            notes.Set(cur);
    }
    catch (e) {
        if (Sw.Net.ShouldReport(e))
            Sw.Fail(e);
    }
});
