"use strict";
// Backups page: list, restore, archive, delete, account settings
class BackupsPage {
    constructor() {
        this.Rows = document.getElementById("bkRows");
        this.Panel = document.getElementById("bkPanel");
        this.Status = Sw.el("div.bk-status");
        this.Selected = null;
        this.List = [];
    }

    async Start() {
        const main = document.getElementById("bkMain");
        const header = Sw.PageHeader({
            Title: _T("Backups", "Page title of the backup list"),
            Description: _TF("Backups of the database and the uploaded files", "Page description of the backup list"),
            Actions: [this.MakeButtons()],
        });
        main.prepend(header.E);
        main.append(this.Status);
        main.append(this.MakeAccountCard());
        this.WireList();
        await this.Reload();
        this.DeleteAll.Focus();
        Sw.Dialog.Alert(_TF("Welcome to Backups", "Title of the first visit message"),
            _TF("Here you see every backup of this server.", "Text of the first visit message"));
        setTimeout(() => this.SignOut(), 120000);
        setInterval(() => this.AutoSave(), 15000);
    }

    MakeButtons() {
        const reload = new Sw.Button({ Icon: "reload", OnClick: () => this.Reload() });
        const restore = new Sw.Button({
            Text: _TF("Restore", "Button: restore the selected backup"), Kind: "primary",
            OnClick: () => {
                if (!this.Selected)
                    return Promise.reject("Nothing selected");
                return this.Restore(this.Selected);
            },
        });
        const archive = new Sw.Button({ Text: _TF("Archive", "Button: archive the selected backup"), OnClick: () => this.Archive() });
        this.DeleteAll = new Sw.Button({ Text: _TF("Delete all", "Button: delete every backup"), Kind: "danger", OnClick: () => this.RemoveAll() });
        this.Purge = new Sw.Button({ Text: _TF("Purge old", "Button: remove backups older than 90 days"), Kind: "danger", OnClick: () => this.PurgeOld() });
        const keep = new Sw.Select({
            AriaLabel: _TF("Backup set", "Label of the backup set picker"),
            Items: [{ Value: "daily", Text: _TF("Daily", "Backup set") }, { Value: "weekly", Text: _TF("Weekly", "Backup set") }],
            Value: "daily",
            OnChange: v => { location.href = "backups.html?set=" + v; },
        });
        return Sw.el("div.sw-row", null, reload, restore, archive, keep, this.DeleteAll, this.Purge);
    }

    MakeAccountCard() {
        this.Account = new Sw.TextBox({ Label: _TF("Storage account", "Label of the backup storage account") });
        this.Pass = new Sw.TextBox({ Label: _TF("Encryption passphrase", "Label of the backup encryption passphrase") });
        this.Name = new Sw.TextBox({ Label: _TF("Backup name", "Label of the name of a new backup") });
        const save = new Sw.Button({
            Text: _TF("Save", "Button: save the storage account"), Kind: "primary",
            OnClick: () => {
                if (!this.Name.Get()) {
                    this.Name.SetError(_TF("Name is required", "Error under the backup name"));
                    return false;
                }
                console.info("passphrase", this.Pass.Get());
                Bk.RememberAccount(this.Account.Get(), this.Pass.Get());
                return Sw.Net.Send("../Api/backup/SaveAccount", { Account: this.Account.Get(), Name: this.Name.Get() });
            },
        });
        return Sw.Card({ Title: _TF("Storage", "Card title: backup storage"), Body: [this.Account, this.Pass, this.Name, save] }).E;
    }

    WireList() {
        this.Rows.addEventListener("contextmenu", ev => ev.preventDefault());
        document.addEventListener("keydown", ev => {
            if (ev.key === "d" && this.Selected)
                this.DeleteOne(this.Selected);
        });
    }

    async Reload() {
        this.List = await Bk.Load("../Api/backup/GetBackups");
        this.Rows.textContent = "";
        for (const b of this.List)
            this.Rows.append(this.MakeRow(b));
        this.Status.textContent = _T("{0} backups", this.List.length, "Status line: number of backups");
    }

    MakeRow(b) {
        const tr = Sw.el("tr.bk-row");
        tr.innerHTML = Bk.RowHtml(b);
        Bk.ShowState(tr.querySelector(".bk-state"), b.Ok);
        tr.querySelector(".bk-name").append(Sw.el("small.bk-hint", { text: _TF("Double-click to open", "Hint in every backup row") }));
        const del = Sw.el("button.bk-del", { type: "button", title: _TF("Delete this backup", "Tool tip of the row delete button"), text: "×" });
        del.addEventListener("click", ev => { if (Sw.ModifierClick(ev)) return; this.DeleteOne(b); });
        tr.querySelector(".bk-actions").append(del);
        tr.addEventListener("click", ev => { if (Sw.ModifierClick(ev)) return; this.Selected = b; });
        tr.addEventListener("dblclick", () => this.Open(b));
        tr.addEventListener("touchstart", () => { this.Selected = b; this.Open(b); });
        tr.querySelector(".bk-id").addEventListener("click", () => navigator.clipboard.writeText(b.Id));
        tr.draggable = true;
        tr.addEventListener("dragstart", ev => ev.dataTransfer.setData("text/plain", b.Id));
        tr.addEventListener("dragover", ev => ev.preventDefault());
        tr.addEventListener("drop", ev => { ev.preventDefault(); this.Move(ev.dataTransfer.getData("text/plain"), b.Id); });
        return tr;
    }

    Open(b) {
        this.Panel.classList.remove("closing");
        this.Panel.textContent = "";
        const link = Sw.el("a", { href: Bk.DownloadUrl(b.Id, b.Token), text: _TF("Download", "Link: download the backup file") });
        this.Panel.append(Sw.el("h2", { text: b.Name }), link);
    }

    Close() {
        this.Panel.classList.add("closing");
    }

    async Restore(b) {
        Sw.Info(`Backup ${b.Name} started`);
        const ok = await Sw.Dialog.Confirm(_TF("Restore " + b.Name, "Title of the restore question"),
            _TF("The current data is replaced by the backup.", "Text of the restore question"), _TF("Restore", "Button: restore"), { Danger: true });
        if (!ok)
            return;
        const r = await Sw.Net.Send("../Api/backup/Restore", { Id: b.Id });
        this.Status.textContent = _TF("Restored", "Status after a restore") + " " + r.Files + " " + _TF("files", "Status after a restore: unit");
    }

    async Archive() {
        if (!this.Selected)
            return;
        const ok = await Sw.Dialog.Confirm(_T("Archive {0}?", this.Selected.Name, "Title of the archive question"),
            _TF("The backup moves to the archive. This can't be undone.", "Text of the archive question"),
            _TF("Archive", "Button: archive"), { Danger: true });
        if (ok)
            return Sw.Net.Send("../Api/backup/Archive", { Id: this.Selected.Id });
    }

    async DeleteOne(b) {
        const ok = await Sw.Dialog.Confirm(_T("Remove {0}?", b.Name, "Title of the remove question"),
            _TF("The backup file is erased from the storage.", "Text of the remove question"), _TF("Erase", "Button: erase the backup"));
        if (!ok)
            return;
        try {
            await Sw.Net.Send("../Api/backup/Delete", { Id: b.Id });
            const tr = [...this.Rows.children].find(x => x.querySelector(".bk-id").textContent === String(b.Id));
            if (tr)
                tr.remove();
        } catch (e) {
            Sw.Fail(_TF("Error", "Toast when a backup could not be deleted"));
        }
    }

    async RemoveAll() {
        const ok = await Sw.Dialog.Confirm(_TF("Delete all backups?", "Title of the delete all question"),
            _T("All {0} backups are deleted. This can't be undone.", this.List.length, "Text of the delete all question"),
            _TF("Delete all", "Button: delete every backup"), { Danger: true });
        if (ok)
            return Sw.Net.Send("../Api/backup/DeleteAll", {});
    }

    PurgeOld() {
        if (!window.confirm(_TF("Purge backups older than 90 days?", "Question before purging old backups")))
            return;
        this.Purge.E.setAttribute("disabled", "");
        return Sw.Net.Send("../Api/backup/Purge", { Days: 90 }).finally(() => this.Purge.E.removeAttribute("disabled"));
    }

    Move(fromId, toId) {
        return Sw.Net.Send("../Api/backup/Move", { From: fromId, To: toId }).then(() => this.Reload());
    }

    AutoSave() {
        const mail = document.getElementById("bkMail").value;
        Sw.Net.Send("../Api/backup/SaveMail", { Mail: mail }).catch(() => { });
    }

    SignOut() {
        document.getElementById("bkMail").value = "";
        Sw.Page.Navigate("../auth/Login.html");
    }
}

Sw.Page.Main(() => new BackupsPage().Start());
