/**
 * @file good/g2.js - warehouse maintenance: destructive actions confirmed, reversible ones with Undo.
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Maintenance", "Page title"), Description: _TF("Clean up the warehouse data.", "Page description") });
    document.body.append(page.E);

    const status = Sw.el("p.sw-selectable", { attrs: { role: "status" } });

    const clearCache = new Sw.Button({
        Text: _TF("Clear the cache", "Button that empties the stock count cache"),
        Title: _TF("Throws away the cached stock counts; they are read again from the scanners (asks first)", "Tool tip"),
        Kind: "danger",
        OnClick: async () => {
            const ok = await Sw.Dialog.Confirm(
                _TF("Clear the stock cache?", "Confirmation title"),
                _TF("Every cached stock count is thrown away and read again from the scanners, which takes about 10 minutes.", "Confirmation text"),
                _TF("Clear the cache", "Confirmation button"),
                { Danger: true });
            if (!ok)
                return;
            const n = await Sw.Net.Send("../Api/inv/ClearCache", null);
            status.textContent = _T("{0} cached counts were cleared.", Sw.Format.Number(n), "Status line after clearing the cache. {0}: number of counts, ex: 1 204");
        },
    });

    const removeEmpty = new Sw.Button({
        Text: _TF("Remove empty items", "Button that removes items with zero stock"),
        Title: _TF("Removes every item whose stock is zero (asks first)", "Tool tip"),
        Kind: "danger",
        OnClick: async () => {
            const items = await Sw.Net.Get("../Api/inv/GetEmptyItems") || [];
            const ok = await Sw.Cells.ConfirmDanger({
                Title: _T("Remove {0} empty items?", Sw.Format.Number(items.length), "Confirmation title. {0}: number of items, ex: 12"),
                Text: _TF("The items and their history are removed from the inventory.", "Confirmation text"),
                OkText: _TF("Remove items", "Confirmation button"),
            });
            if (!ok)
                return;
            await Sw.Net.Send("../Api/inv/RemoveEmpty", null);
            status.textContent = _T("{0} empty items were removed.", Sw.Format.Number(items.length), "Status line. {0}: number of items, ex: 12");
        },
    });

    page.Add(Sw.Card({ Title: _TF("Clean up", "Card title"), Body: [clearCache, removeEmpty, status] }));
});
