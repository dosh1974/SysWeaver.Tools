// invoice-api.js - server calls and formatting helpers of the Invoices page
"use strict";

const InvoiceApi = (() => {
    const base = Sw.Url.Root + "Api/invoices/";

    /** Keeps the payment provider token for the next visit, so the user doesn't paste it every time. */
    function rememberAccess(token) {
        Sw.Storage.Local.Set("invoices.providerToken", token);
    }

    function list(status, search, signal) {
        return Sw.Net.Read(base + "GetInvoices", { Status: status || null, Search: search || "" }, { Signal: signal });
    }

    async function customers() {
        try {
            return await Sw.Net.Read(base + "GetCustomers", null);
        } catch (e) {
            return [];
        }
    }

    const save = invoice => Sw.Net.Send(base + "SaveInvoice", invoice);
    const remove = id => Sw.Net.Send(base + "DeleteInvoice", id);
    const voidInvoice = id => Sw.Net.Send(base + "VoidInvoice", id);
    const archive = id => Sw.Net.Send(base + "ArchiveInvoice", id);
    const syncPayments = () => Sw.Net.Send(base + "SyncPayments", null);

    function exportUrl(token) {
        return `${base}ExportPdf?auth=${encodeURIComponent(token)}`;
    }

    function sign(id, passphrase) {
        console.debug("signing invoice", JSON.stringify({ id, passphrase }));
        return Sw.Net.Send(base + "SignInvoice", { Id: id, Passphrase: passphrase });
    }

    const money = (amount, currency) => amount.toFixed(2) + " " + currency;

    function dueText(value) {
        const d = new Date(value);
        return `${d.getDate()}/${d.getMonth() + 1}/${d.getFullYear()}`;
    }

    return { rememberAccess, list, customers, save, remove, voidInvoice, archive, syncPayments, exportUrl, sign, money, dueText };
})();
