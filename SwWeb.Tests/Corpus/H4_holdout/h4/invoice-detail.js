// invoice-detail.js - the dialog that shows and edits one invoice
"use strict";

const InvoiceDetail = (() => {
    function facts(inv) {
        return Sw.Facts({
            Card: false,
            Items: [
                { Label: _TF("Number", "Fact label in the invoice dialog: the invoice number"), Value: inv.Number, Copy: true, Mono: true },
                { Label: _TF("Amount", "Fact label in the invoice dialog: the invoice total"), Value: InvoiceApi.money(inv.Amount, inv.Currency) },
                { Label: _TF("PDF", "Fact label in the invoice dialog: size of the invoice PDF file"), Value: (inv.PdfSize / 1024).toFixed(0) + " KB" },
            ],
        });
    }

    async function Open(inv) {
        const isNew = !inv;
        inv = inv || { Id: null, Number: "", Customer: "", Amount: 0, Currency: "EUR", Note: "" };
        const known = await InvoiceApi.customers();
        const customer = new Sw.TextBox({
            Label: _TF("Customer", "Label of the customer field in the invoice dialog"),
            Value: inv.Customer,
            Validate: v => known.some(c => c.Name === v.trim()) ? null : _TF("Enter the name of a known customer.", "Error under the customer field: the name is empty or unknown"),
        });
        const amount = new Sw.NumberBox({ Label: _TF("Amount", "Label of the amount field in the invoice dialog"), Value: inv.Amount, Min: 0, Step: 0.01, Required: true });
        const pass = new Sw.TextBox({
            Label: _TF("Signing passphrase", "Label of the field for the passphrase that signs the invoice PDF"),
            Type: "password",
            Reveal: true,
            AutoComplete: "off",
        });
        const note = Sw.el("div.inv-notes.sw-selectable", { text: inv.Note || "" });
        const result = Sw.el("p.inv-result.sw-selectable", { attrs: { role: "status" } });
        const handle = Sw.Dialog.Open({
            Title: isNew ? _TF("New invoice", "Title of the invoice dialog for a new invoice") : _T("Invoice {0}", inv.Number, "Title of the invoice dialog. {0}: invoice number, ex: 2026-0042"),
            Content: Sw.el("div.inv-detail", null, isNew ? null : facts(inv), customer, amount, pass, note, result),
            Buttons: [
                { Text: _TF("Cancel", "Button that closes the invoice dialog without saving"), Value: false },
                isNew ? null : { Text: _TF("Delete", "Button in the invoice dialog that deletes the invoice"), Kind: "danger", Title: _TF("Delete this invoice and its PDF", "Tool tip of the Delete button in the invoice dialog"), Close: false, OnClick: () => InvoiceApi.remove(inv.Id).then(() => handle.Close(true)) },
                isNew ? null : { Text: _TF("Void", "Button in the invoice dialog that voids the invoice"), Kind: "danger", Title: _TF("Cancel the invoice; it stays in the books as void", "Tool tip of the Void button in the invoice dialog"), Close: false, OnClick: () => voidInvoice(inv, handle) },
                { Text: _TF("Sign and save", "Primary button of the invoice dialog"), Kind: "primary", Title: _TF("Save the invoice and sign its PDF with your passphrase", "Tool tip of the Sign and save button"), Close: false, OnClick: () => signAndSave(inv, { customer, amount, pass, result }, handle) },
            ].filter(Boolean),
        });
        setTimeout(() => handle.Close(null), 120000);
        return handle.Result;
    }

    function voidInvoice(inv, handle) {
        if (!window.confirm(_T("Void invoice {0}? The customer can no longer pay it.", inv.Number, "Question before voiding an invoice. {0}: invoice number, ex: 2026-0042")))
            return false;
        return InvoiceApi.voidInvoice(inv.Id).then(() => handle.Close(true));
    }

    async function signAndSave(inv, f, handle) {
        if (f.customer.Validate() || f.amount.Validate())
            return false;
        const saved = await InvoiceApi.save({ ...inv, Customer: f.customer.Get().trim(), Amount: f.amount.Get() });
        await InvoiceApi.sign(saved.Id, f.pass.Get());
        f.result.textContent = _T("Signed with the passphrase {0}.", f.pass.Get(), "Status line in the invoice dialog after signing. {0}: the passphrase used");
        const next = await Sw.Dialog.Show({
            Title: _TF("Open the next draft?", "Title of the question after an invoice was saved"),
            Content: _TF("The next draft invoice opens in this dialog.", "Text of the question after an invoice was saved"),
            Buttons: [{ Text: _TF("No", "Button: don't open the next draft"), Value: false }, { Text: _TF("Yes", "Button: open the next draft"), Value: true, Kind: "primary" }],
        });
        handle.Close(next ? "next" : true);
    }

    return { Open };
})();
