/**
 * @file naive/right.js - the same page done the way the UX guide asks (to compare the effort).
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Right page", "Title of a test page"), Description: _TF("The same page, done by the guide.", "Description of a test page") });

    const name = new Sw.TextBox({ Label: _TF("Name", "Label of the name field"), Required: true });
    const note = new Sw.TextBox({ Label: _TF("Note", "Label of the note field"), Multiline: true });
    const draft = Sw.Draft.Attach(note.Input || note.E.querySelector("textarea"), { Context: "naive.right.note" });

    //  SAF-10 by hand: compare with the saved values on every input
    let saved = { Name: "", Note: "" };
    const markDirty = () => {
        const dirty = name.Get() !== saved.Name || note.Get() !== saved.Note;
        Sw.Page.SetDirty("naive.right", dirty ? _TF("Your changes to the settings are lost.", "Leave question text") : null);
    };
    name.E.addEventListener("input", markDirty);
    note.E.addEventListener("input", markDirty);

    const save = new Sw.Button({ Text: _TF("Save", "Button that saves the form"), Kind: "primary", Title: _TF("Save the name and the note", "Tool tip of the save button"), Shortcut: "Mod+S" });
    Sw.Form({
        Fields: [{ Name: "Name", Control: name, Required: true }, { Name: "Note", Control: note }],
        Submit: save,
        OnSubmit: async v => {
            await Sw.Net.Send("../Api/naive/Save", v);
            saved = { Name: v.Name || "", Note: v.Note || "" };
            Sw.Page.ClearDirty("naive.right");
            if (draft)
                draft.Clear();
        },
    });
    const del = new Sw.Button({
        Text: _TF("Delete all", "Button that deletes all rows"), Kind: "danger", Title: _TF("Delete every row (asks first)", "Tool tip"),
        OnClick: async () => {
            if (await Sw.Cells.ConfirmDanger({ Action: _TF("Delete", "Verb"), Target: _TF("all rows", "Target of the delete") }))
                await Sw.Net.Send("../Api/naive/DeleteAll", null);
        },
    });
    const exp = new Sw.Button({ Text: _TF("Export", "Button that exports"), Disabled: _TF("Select a row first", "Reason the export is disabled") });
    const job = new Sw.Button({
        Text: _TF("Run long export", "Button that runs a long export"), Title: _TF("Export all rows to report.xlsx", "Tool tip"),
        OnClick: () => {
            const p = Sw.Net.Send("../Api/naive/Export", null);
            Sw.Activity.Track(p, { Key: "naive.export", Title: _TF("Exporting report.xlsx", "Activity title") });
            return p;
        },
    });
    const form = Sw.Card({ Title: _TF("Settings", "Card title"), Body: [Sw.el("div.sw-stack", null, name.E, note.E), Sw.el("div.sw-row.sw-end", null, del.E, exp.E, job.E, save.E)] });
    page.Add(form);
});
