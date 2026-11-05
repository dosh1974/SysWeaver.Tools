/**
 * @file good/g4.js - edit an item in a dialog (async fill, Save / Cancel), and a report download with a toast.
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Item", "Page title of one inventory item"), Description: _TF("Details of one item.", "Page description") });
    document.body.append(page.E);
    const id = new URLSearchParams(location.search).get("id");

    const edit = new Sw.Button({
        Text: _TF("Edit", "Button that opens the edit dialog of the item"),
        Title: _TF("Change the name and the shelf of this item", "Tool tip"),
        OnClick: () => openEdit(),
    });

    const report = new Sw.Button({
        Text: _TF("Download report", "Button that downloads the stock report of the item"),
        Icon: "download",
        Title: _TF("Download the stock movements of this item as a CSV file", "Tool tip"),
        OnClick: async () => {
            const blob = await Sw.Net.Send("../Api/inv/GetItemReport", { Id: id }, { Blob: true });
            const a = Sw.el("a", { href: URL.createObjectURL(blob), download: "item-" + id + ".csv" });
            a.click();
            URL.revokeObjectURL(a.href);
            Sw.Info(_TF("The report was downloaded.", "Toast after the download of an item report"));
        },
    });

    page.Add(Sw.Card({ Title: _TF("Actions", "Card title"), Body: [edit, report] }));

    function openEdit() {
        const name = new Sw.TextBox({ Label: _TF("Name", "Field label in the edit item dialog") });
        const shelf = new Sw.TextBox({ Label: _TF("Shelf", "Field label in the edit item dialog"), Hint: _TF("Ex: B-12", "Hint with an example shelf code") });
        Sw.Dialog.Show({
            Title: _TF("Edit the item", "Dialog title"),
            Content: Sw.el("div", null, name, shelf),
            Buttons: [
                { Text: _TF("Cancel", "Dialog button") },
                { Text: _TF("Save", "Dialog button that saves the item"), Kind: "primary", OnClick: () => Sw.Net.Send("../Api/inv/SaveItem", { Id: id, Name: name.Get(), Shelf: shelf.Get() }) },
            ],
        });
        Sw.Net.Get("../Api/inv/GetItem?id=" + encodeURIComponent(id)).then(it => {
            if (!it)
                return;
            name.Set(it.Name || "");
            shelf.Set(it.Shelf || "");
        });
    }
});
