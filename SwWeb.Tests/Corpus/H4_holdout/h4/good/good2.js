// good2.js - one invoice: delete and void are confirmed as danger, name their target and say the consequence
"use strict";

Sw.Page.Main(async () => {
    const api = Sw.Url.Root + "Api/invoices/";
    const inv = await Sw.Net.Read(api + "GetInvoice", Sw.Url.Get("id"));
    const page = Sw.PageLayout({
        Type: "form",
        Title: _T("Invoice {0}", inv.Number, "Title of the page of one invoice. {0}: invoice number, ex: 2026-0042"),
        Description: _TF("Details and actions of one invoice.", "One line description under the title of the invoice page"),
        MenuTitle: false,
    });
    const status = Sw.el("p.sw-selectable", { attrs: { role: "status" } });

    const del = new Sw.Button({
        Text: _TF("Delete invoice", "Button that deletes the invoice shown on the page"),
        Icon: "delete",
        Kind: "danger",
        Title: _TF("Delete this invoice and its PDF (asks first)", "Tool tip of the Delete invoice button"),
        OnClick: () => {
            Sw.Danger.Run({
                Action: _TF("Delete", "Verb on the confirmation button that deletes an invoice"),
                Target: inv.Number,
                Consequence: _TF("The invoice and its PDF are removed. This can't be undone.", "Text of the confirmation before deleting an invoice"),
                Button: del,
                Run: signal => Sw.Net.Send(api + "DeleteInvoice", inv.Id, { Signal: signal }),
                Done: _T("Invoice {0} deleted", inv.Number, "Result after deleting an invoice. {0}: invoice number, ex: 2026-0042"),
            });
        },
    });

    const voidBtn = new Sw.Button({
        Text: _TF("Void invoice", "Button that voids the invoice shown on the page"),
        Kind: "danger",
        Title: _TF("Cancel the invoice for good; it stays in the books as void (asks first)", "Tool tip of the Void invoice button"),
        OnClick: async () => {
            const ok = await Sw.Dialog.Confirm(
                Sw.Dialog.DangerTitle(_TF("Void", "Verb in the title of the confirmation before voiding an invoice"), inv.Number),
                _TF("The customer can no longer pay it, and it stays in the books as void. This can't be undone.", "Text of the confirmation before voiding an invoice"),
                _TF("Void invoice", "Confirmation button that voids the invoice"),
                { Danger: true });
            if (!ok)
                return;
            await Sw.Net.Send(api + "VoidInvoice", inv.Id);
            status.textContent = _T("Invoice {0} is void.", inv.Number, "Status line after voiding an invoice. {0}: invoice number, ex: 2026-0042");
        },
    });

    page.Add(Sw.Card({
        Title: _TF("Actions", "Card title of the actions on the invoice page"),
        Body: [Sw.el("div.sw-row", null, voidBtn, del), status],
    }));
});
