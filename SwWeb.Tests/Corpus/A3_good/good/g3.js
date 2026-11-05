/**
 * @file good/g3.js - stock files as cards (Sw.Card OnClick, Sw.Format, a Refresh that keeps the focus).
 */
"use strict";

Sw.Page.Main(async () => {
    const refresh = new Sw.Button({
        Text: _TF("Refresh", "Button that reloads the list of stock files"),
        Icon: "refresh",
        Title: _TF("Read the list of stock files again from the server", "Tool tip of the Refresh button"),
        OnClick: () => load(),
    });
    const page = Sw.PageLayout({ Type: "dash", Title: _TF("Stock files", "Page title"), Description: _TF("Files the scanners uploaded today.", "Page description"), Actions: [refresh] });
    document.body.append(page.E);

    const grid = Sw.el("div.g3-grid");
    const empty = Sw.el("p.g3-empty.sw-selectable", { text: _TF("No stock files yet. The scanners upload one every hour.", "Empty state of the stock file list"), hidden: true });
    page.Add(grid, empty);

    async function load() {
        const keep = Sw.Focus.Keep(grid);
        const files = await Sw.Net.Get("../Api/inv/GetStockFiles") || [];
        grid.replaceChildren(...files.map(f => {
            const [tip, when] = Sw.Format.TimeStamp(f.Changed);
            const card = Sw.Card({
                Title: f.Name,
                Sub: Sw.Format.Bytes(f.Size),
                Body: Sw.el("span.g3-when.sw-selectable", { text: when, title: tip }),
                OnClick: () => Sw.Page.Navigate("file.html?name=" + encodeURIComponent(f.Name)),
            });
            card.E.dataset.swFocusKey = f.Name;
            return card;
        }));
        empty.hidden = files.length > 0;
        keep.Restore();
    }

    await load();
});
