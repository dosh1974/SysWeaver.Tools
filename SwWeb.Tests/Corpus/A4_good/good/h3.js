/**
 * @file good/h3.js - a list of parcels: formatted values, copy the right way, a scoped search shortcut, empty and error states.
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "list", Title: _TF("Parcels", "Page title of the parcel list"), Description: _TF("Parcels handed to the carriers today.", "Page description") });
    document.body.append(page.E);

    const list = Sw.el("ul.sw-list", { attrs: { "aria-label": _TF("Parcels", "Accessible name of the parcel list") } });
    const state = Sw.el("p.sw-selectable", { attrs: { role: "status" } });
    const search = new Sw.TextBox({ Label: _TF("Find a parcel", "Search field label"), Type: "search", Clear: true, OnInput: v => render(v) });
    Sw.Keys.Add("/", () => search.Focus(true), { Title: _TF("Find a parcel", "Shortcut description"), Scope: list });

    let rows = [];

    function render(filter) {
        const words = (filter || "").toLowerCase().split(/\s+/).filter(Boolean);
        const shown = rows.filter(r => words.every(w => (r.Reference + " " + r.Recipient).toLowerCase().includes(w)));
        list.textContent = "";
        for (const r of shown) {
            const [tip, at] = Sw.Format.TimeStamp(r.Shipped);
            const ref = Sw.el("span.sw-selectable", { text: r.Reference });
            Sw.Clipboard.CopyOnClick(ref, r.Reference, { What: _TF("Reference", "What was copied: a parcel reference") });
            list.append(Sw.el("li", null,
                ref,
                Sw.el("span", { text: r.Recipient }),
                Sw.el("span", { text: at, title: tip }),
                Sw.el("span", { text: Sw.Format.Bytes(r.LabelSize) }),
                Sw.CopyButton({ Value: r.Tracking, What: _TF("Tracking number", "What was copied"), Label: _T("Copy the tracking number of {0}", r.Reference, "Copy button. {0}: parcel reference, ex: SH-1043") })));
        }
        state.textContent = shown.length ? _T("Parcels: {0}", Sw.Format.Number(shown.length), "Count line. {0}: number of parcels shown, ex: 12")
            : _TF("No parcels match. Clear the search to see all of today's parcels.", "Empty state of the parcel list");
    }

    async function load() {
        try {
            rows = await Sw.Net.Get("../Api/ship/GetParcels") || [];
            render(search.Get());
        } catch (e) {
            if (!Sw.Net.ShouldReport(e))
                return;
            state.textContent = Sw.Net.Message(_TF("The parcels could not be loaded", "Error line of the parcel list"), e);
        }
    }

    const reload = new Sw.Button({ Text: _TF("Reload", "Button that reads the parcel list again"), Title: _TF("Read today's parcels again from the server", "Tool tip"), OnClick: () => load() });
    page.Add(Sw.Card({ Title: _TF("Today", "Card title"), Body: [search, reload, state, list] }));
    await load();
});
