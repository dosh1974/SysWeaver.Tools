// good4.js - invoice overview: a list card with loading, empty and error states and a live search
"use strict";

Sw.Page.Main(async () => {
    const main = document.getElementById("ov-main");
    const search = document.getElementById("ov-search");
    const status = document.getElementById("ov-status");
    let all = [];

    const list = Sw.ListCard({
        Title: _TF("Invoices", "Title of the card with the list of invoices"),
        Count: true,
        Span: 12,
        Empty: {
            Icon: "file",
            Title: _TF("No invoices here", "Title of the empty state of the invoice list"),
            Text: _TF("Invoices you create, or that match the search, show up here.", "Text of the empty state of the invoice list"),
        },
    });
    const retry = new Sw.Button({
        Text: _TF("Try again", "Button that loads the invoice list again after an error"),
        Icon: "refresh",
        Title: _TF("Load the invoice list from the server again", "Tool tip of the Try again button of the invoice list"),
        OnClick: () => load(),
    });
    const error = Sw.StateView({
        Kind: "error",
        Small: true,
        Title: _TF("The invoices could not be loaded", "Title of the error state of the invoice list"),
        Actions: [retry],
    });
    error.E.hidden = true;
    main.append(list.E, error.E);

    function show() {
        const words = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
        const hits = all.filter(inv => words.every(w => (inv.Number + " " + inv.Customer).toLowerCase().includes(w)));
        list.SetItems(hits.map(inv => ({
            Key: inv.Id,
            Text: inv.Customer,
            Meta: Sw.Format.Amount(inv.Currency, inv.Amount),
            Status: inv.Paid
                ? { Text: _TF("Paid", "Status badge of an invoice that was paid"), Kind: "ok" }
                : { Text: _TF("Open", "Status badge of an invoice that is not paid yet"), Kind: "warning" },
            Href: "invoice.html?id=" + encodeURIComponent(inv.Id),
            Title: _T("Open invoice {0}", inv.Number, "Tool tip of a row of the invoice list. {0}: invoice number, ex: 2026-0042"),
        })));
        status.textContent = _T("Invoices shown: {0}", Sw.Format.Number(hits.length, 0), "Status line under the search field. {0}: number of invoices shown, ex: 12");
    }

    async function load() {
        try {
            all = await Sw.Net.Read(Sw.Url.Root + "Api/invoices/GetInvoices", null);
            error.E.hidden = true;
            list.E.hidden = false;
            show();
        } catch (e) {
            if (!Sw.Net.ShouldReport(e))
                return;
            error.Set({ Text: Sw.Net.Message(_TF("Loading the invoices failed", "Start of the error text of the invoice list"), e) });
            error.E.hidden = false;
            list.E.hidden = true;
        }
    }

    search.addEventListener("input", Sw.Util.Debounce(show, 300));
    await load();
}, { Loading: true });
