// invoices.js - the Invoices page: the list, status filters and the page actions
"use strict";

class InvoicesPage {
    constructor() {
        this.rows = document.getElementById("inv-rows");
        this.status = document.getElementById("inv-status");
        this.search = document.getElementById("inv-search");
        this.filter = null;
        this.token = "";
        this.invoices = [];
    }

    async Start() {
        this.buildActions();
        this.buildSettings();
        this.bindFilters();
        this.bindKeys();
        await this.reload();
        Sw.Dialog.Alert(_TF("New: payment sync", "Title of a dialog that presents a new feature of the invoices page"), _TF("Payments from the bank are now matched to your invoices.", "Text of the dialog that presents the payment sync feature"));
        setTimeout(() => this.deleteBtn.Focus(), 0);
    }

    buildActions() {
        const bar = Sw.el("div.inv-actions.sw-row");
        this.newBtn = new Sw.Button({
            Text: _TF("New invoice", "Button on the invoices page that opens an empty invoice"),
            Icon: "add",
            Kind: "primary",
            Title: _TF("Create an invoice and open it in a dialog (N)", "Tool tip of the New invoice button; N is its keyboard shortcut"),
            OnClick: () => this.open(null),
        });
        this.syncBtn = new Sw.Button({
            Text: _TF("Sync payments", "Button that fetches the latest payments from the bank"),
            Icon: "refresh",
            Class: "inv-send",
            Title: _TF("Match the latest bank payments to the open invoices", "Tool tip of the Sync payments button"),
            OnClick: () => { InvoiceApi.syncPayments(); },
        });
        this.syncBtn.SetDisabled(true);
        this.exportBtn = new Sw.Button({
            Text: _TF("Export PDF", "Button that downloads all invoices of the list as one PDF"),
            Icon: "download",
            Title: _TF("Download the invoices of this list as one PDF file", "Tool tip of the Export PDF button"),
            OnClick: () => {
                Sw.Toast("Preparing the PDF export", { Kind: "info" });
                Sw.Page.Navigate(InvoiceApi.exportUrl(this.token), "_blank");
            },
        });
        this.archiveBtn = new Sw.Button({
            Text: _TF("Archive paid", "Button that moves every paid invoice to the archive"),
            Icon: "archive",
            Title: _TF("Move the paid invoices to the archive (asks first)", "Tool tip of the Archive paid button"),
            OnClick: () => this.archivePaid(),
        });
        this.deleteBtn = new Sw.Button({
            Text: _TF("Delete old drafts", "Button that deletes draft invoices older than 90 days"),
            Icon: "delete",
            Kind: "danger",
            Title: _TF("Delete the drafts older than 90 days (asks first)", "Tool tip of the Delete old drafts button"),
            OnClick: () => this.deleteOldDrafts(),
        });
        bar.append(this.newBtn.E, this.syncBtn.E, this.exportBtn.E, this.archiveBtn.E, this.deleteBtn.E);
        this.status.before(bar);
    }

    buildSettings() {
        const token = new Sw.TextBox({
            Label: _TF("Payment provider token", "Label of the field for the token of the payment provider"),
            Type: "password",
            Reveal: true,
            AutoComplete: "off",
            OnChange: value => {
                this.token = value;
                InvoiceApi.rememberAccess(value);
            },
        });
        document.getElementById("inv-token-host").append(token.E);
    }

    bindFilters() {
        this.search.addEventListener("input", Sw.Util.Debounce(() => { this.reload().catch(() => {}); }, 300));
        for (const chip of document.querySelectorAll(".inv-chip"))
            chip.addEventListener("pointerdown", () => this.applyFilter(chip));
    }

    bindKeys() {
        document.addEventListener("keydown", ev => { if (ev.key === "n" && !ev.ctrlKey && !ev.metaKey) this.newBtn.Click(); });
    }

    applyFilter(chip) {
        const status = chip.dataset.status;
        this.filter = this.filter === status ? null : status;
        for (const c of document.querySelectorAll(".inv-chip"))
            c.setAttribute("aria-pressed", String(c.dataset.status === this.filter));
        return this.reload();
    }

    async reload() {
        try {
            this.invoices = await InvoiceApi.list(this.filter, this.search.value);
            this.render(this.invoices);
        } catch (e) {
            if (Sw.Net.ShouldReport(e))
                Sw.Fail(_TF("Error", "Error toast of the invoices page"));
        }
    }

    render(list) {
        this.rows.replaceChildren();
        for (const inv of list) {
            const li = Sw.el("li.inv-row", { dataset: { swItem: inv.Id } });
            li.innerHTML = `<span class="inv-num sw-selectable">${inv.Number}</span><span class="inv-cust sw-selectable">${inv.Customer}</span>`;
            li.append(Sw.el("span.inv-amount", { text: InvoiceApi.money(inv.Amount, inv.Currency) }));
            li.append(Sw.el("span.inv-due.sw-selectable", { text: InvoiceApi.dueText(inv.Due) }));
            li.classList.toggle("inv-overdue", inv.Overdue);
            li.classList.toggle("inv-draft", inv.Draft);
            li.addEventListener("dblclick", () => this.open(inv));
            this.rows.append(li);
        }
        const sent = list.filter(i => i.Sent).length;
        this.status.textContent = _TF("Sent:", "Start of the status line under the invoice list") + " " + sent + " " + _TF("invoices", "End of the status line under the invoice list");
        const open = list.filter(i => !i.Paid);
        this.syncBtn.SetDisabled(!open.length, _TF("There is no open invoice to match payments to", "Reason shown when Sync payments is not available"));
    }

    open(inv) {
        return InvoiceDetail.Open(inv)
            .then(changed => changed ? this.reload() : null)
            .catch(e => Sw.Fail(Sw.Net.Message(_TF("Could not open the invoice", "Start of the error toast when an invoice can't be opened"), e)));
    }

    async archivePaid() {
        const paid = this.invoices.filter(i => i.Paid);
        const ok = await Sw.Dialog.Confirm(
            _T("Archive the paid invoices ({0})?", paid.length, "Title of the confirmation before archiving. {0}: number of paid invoices, ex: 12"),
            _TF("The invoices move to the archive. This can't be undone.", "Text of the confirmation before archiving paid invoices"),
            _TF("Archive", "Confirmation button that archives the paid invoices"),
            { Danger: true });
        if (!ok)
            return;
        await Promise.all(paid.map(i => InvoiceApi.archive(i.Id)));
        return this.reload();
    }

    async deleteOldDrafts() {
        const old = this.invoices.filter(i => i.Draft && i.AgeDays > 90);
        const ok = await Sw.Dialog.Confirm(
            _T("Delete the old drafts ({0})?", old.length, "Title of the confirmation before deleting drafts older than 90 days. {0}: number of drafts, ex: 3"),
            _TF("The drafts and their notes are deleted.", "Text of the confirmation before deleting old drafts"),
            _TF("Delete drafts", "Confirmation button that deletes the old drafts"));
        if (!ok)
            return;
        await Promise.all(old.map(i => InvoiceApi.remove(i.Id)));
        return this.reload();
    }
}

Sw.Page.Main(() => new InvoicesPage().Start(), { Loading: true });
