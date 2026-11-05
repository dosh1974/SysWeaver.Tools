// good3.js - invoice totals: every number, amount, size and date goes through Sw.Format
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({
        Type: "dash",
        Title: _TF("Invoice totals", "Title of the page with the invoice totals of this year"),
        Description: _TF("What customers owe and paid this year.", "One line description under the title of the invoice totals page"),
    });
    const t = await Sw.Net.Read(Sw.Url.Root + "Api/invoices/GetTotals", null);
    const [paidTip, paidAt] = Sw.Format.TimeStamp(t.LastPayment);
    const facts = Sw.Facts({
        Items: [
            { Label: _TF("Open", "Fact label: total amount of the unpaid invoices"), Value: Sw.Format.Amount(t.Currency, t.Open) },
            { Label: _TF("Paid this year", "Fact label: total amount paid this year"), Value: Sw.Format.Amount(t.Currency, t.Paid) },
            { Label: _TF("Invoices", "Fact label: number of invoices written this year"), Value: Sw.Format.Number(t.Count, 0) },
            { Label: _TF("PDF storage", "Fact label: disk space the invoice PDF files use"), Value: Sw.Format.Bytes(t.PdfBytes) },
            { Label: _TF("Last payment", "Fact label: when the last payment arrived"), Value: paidAt, Title: paidTip },
        ],
    });
    const share = t.Count ? t.PaidCount / t.Count : 0;
    const line = Sw.el("p.sw-selectable", {
        text: _T("{0} of {1} invoices are paid ({2} %).", Sw.Format.Number(t.PaidCount, 0), Sw.Format.Number(t.Count, 0), Sw.Format.Number(share * 100, 1),
            "Summary under the totals. {0}: paid invoices, ex: 40; {1}: all invoices, ex: 52; {2}: share in percent, ex: 76.9"),
    });
    page.Add(facts, line);
}, { Loading: true });
