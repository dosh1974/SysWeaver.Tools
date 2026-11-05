/**
 * @file naive/sw.js - Sw components used the naive way (the quick start, no UX guide reading).
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Naive Sw page", "Title of a test page"), Description: _TF("Sw components, used naively.", "Description of a test page") });

    const name = new Sw.TextBox({ Label: _TF("Name", "Label of the name field") });
    const note = new Sw.TextBox({ Label: _TF("Note", "Label of the note field"), Multiline: true });

    //  N1: OnClick that doesn't return the promise (no busy state, double submit possible)
    const save = new Sw.Button({
        Text: _TF("Save", "Button that saves the form"), Kind: "primary",
        OnClick: () => { Sw.Net.Send("../Api/naive/Save", { Name: name.Get(), Note: note.Get() }).then(() => { window.__saves = (window.__saves || 0) + 1; Sw.Info(_TF("Saved", "Toast after save")); }); },
    });
    //  N2: busy by hand with the native disabled
    const save2 = new Sw.Button({
        Text: _TF("Save copy", "Button that saves a copy"),
        OnClick: async () => { save2.E.disabled = true; await Sw.Net.Send("../Api/naive/Save", {}); save2.E.disabled = false; window.__saves2 = (window.__saves2 || 0) + 1; },
    });
    //  N3: a destructive action with a plain confirm (no Danger)
    const del = new Sw.Button({
        Text: _TF("Delete all", "Button that deletes all rows"), Kind: "danger",
        OnClick: async () => {
            if (await Sw.Dialog.Confirm(_TF("Delete", "Title of a dialog"), _TF("Are you sure?", "Text of a dialog"), _TF("OK", "Button of a dialog")))
                await Sw.Net.Send("../Api/naive/DeleteAll", null);
        },
    });
    //  N4: a disabled button without a reason
    const exp = new Sw.Button({ Text: _TF("Export", "Button that exports"), Disabled: true });
    //  N5: a dialog with a form built from raw inputs and an async save
    const edit = new Sw.Button({
        Text: _TF("Edit in a dialog", "Button that opens an edit dialog"),
        OnClick: () => {
            const inp = document.createElement("input");
            inp.className = "nv-dlg-input";
            const tb = new Sw.TextBox({ Label: _TF("Title", "Label of a field in a dialog") });
            const tg = new Sw.Toggle({ Label: _TF("Enabled", "Label of a switch in a dialog") });
            window.__dlg = Sw.Dialog.Show({
                Title: _TF("Edit the item", "Dialog title"),
                Content: Sw.el("div.sw-stack", null, inp, tb.E, tg.E),
                Buttons: [
                    { Text: _TF("Cancel", "Dialog button") },
                    { Text: _TF("Save", "Dialog button"), Kind: "primary", OnClick: async () => { await Sw.Net.Send("../Api/naive/Save", {}); } },
                ],
            });
        },
    });
    //  N6: a long job, no progress, no activity
    const job = new Sw.Button({
        Text: _TF("Run long export", "Button that runs a long export"),
        OnClick: async () => { await Sw.Net.Send("../Api/naive/Export", null); Sw.Info(_TF("Export done", "Toast")); },
    });
    //  N7: a clickable card without BadClick
    const card = Sw.Card({ Title: _TF("Open details", "Card title"), Body: [_TF("Click the card to open the details.", "Card text")] });
    card.E.addEventListener("click", () => { window.__cardOpened = (window.__cardOpened || 0) + 1; });

    const form = Sw.Card({ Title: _TF("Settings", "Card title"), Body: [name, note, Sw.el("div.sw-row.sw-start", null, save.E, save2.E, del.E, exp.E, edit.E, job.E)] });
    page.Add(form, card);
});
