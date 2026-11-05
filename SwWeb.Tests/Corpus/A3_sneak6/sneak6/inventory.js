/**
 * @file sneak6/inventory.js - the inventory page: items of the warehouse, preferences, maintenance.
 */
"use strict";

class InventoryPage {
    constructor(root) {
        this.Root = root;
        this.Items = [];
        this.Selected = null;
        this.Status = Sw.el("div.inv-statusline", { attrs: { role: "status" } });
    }

    async Start() {
        const page = Sw.PageLayout({ Type: "dash", Title: _TF("Inventory", "Page title of the warehouse inventory"), Description: _TF("Items in stock and their state.", "Page description") });
        this.Root.append(page.E);

        this.Search = new Sw.TextBox({ Label: _TF("Find an item", "Search field label of the inventory list"), Type: "search", OnInput: v => this.Filter(v) });
        this.Search.Input.tabIndex = 1;

        this.List = Sw.el("ul.inv-list");
        this.Bar = Sw.el("div.inv-bar");

        const wipe = new Sw.Button({
            Text: _TF("Clear the cache", "Button on the inventory page that empties the warehouse cache"),
            Title: _TF("Throws away every cached stock count of all warehouses", "Tool tip"),
            Kind: "danger",
            OnClick: () => Sw.Net.Send(InvLib.Base + "WipeCache", null),
        });

        const discard = new Sw.Button({
            Text: _TF("Discard item", "Button that removes the selected item from the inventory"),
            Title: _TF("Removes the selected item from the inventory", "Tool tip"),
            OnClick: () => {
                fetch(InvLib.Base + "DeleteItem", { method: "POST", body: JSON.stringify({ Id: this.Selected }) });
            },
        });

        const rename = new Sw.Button({
            Text: _TF("Rename", "Button that renames the selected item"),
            Title: _TF("Give the selected item a new name", "Tool tip"),
            OnClick: () => {
                const name = prompt(_TF("New name of the item", "Prompt text when renaming an item"));
                if (name)
                    return Sw.Net.Send(InvLib.Base + "Rename", { Id: this.Selected, Name: name });
            },
        });

        const archive = new Sw.Button({
            Text: _TF("Archive old items", "Button that archives items not used for a year"),
            Title: _TF("Moves items without movement for a year to the archive", "Tool tip"),
            OnClick: () => Sw.Dialog.Show({
                Title: _TF("Archive the old items?", "Dialog title"),
                Content: _TF("Items without stock movement for a year go to the archive.", "Dialog text"),
                Buttons: [
                    { Text: _TF("Yes", "Dialog button that archives"), Kind: "primary", OnClick: () => Sw.Net.Send(InvLib.Base + "ArchiveOld", null) },
                    { Text: _TF("No", "Dialog button that closes") },
                ],
            }),
        });

        const recount = new Sw.Button({
            Text: _TF("Recount", "Button that recounts the stock of the selected item"),
            Title: _TF("Asks the scanner for a new count of the selected item", "Tool tip"),
            OnClick: async () => {
                try {
                    await Sw.Net.Send(InvLib.Base + "Recount", { Id: this.Selected });
                }
                catch (e) {
                    console.error("recount failed", e);
                }
            },
        });

        const sync = new Sw.Button({
            Text: _TF("Sync", "Button that synchronises the inventory with the warehouse system"),
            Title: _TF("Fetches the latest stock from the warehouse system", "Tool tip"),
            OnClick: b => {
                b.E.style.pointerEvents = "none";
                this.Sync().finally(() => { b.E.style.pointerEvents = ""; });
            },
        });

        const purgeAll = new Sw.Button({
            Text: _TF("Remove empty items", "Button that removes every item with zero stock"),
            Title: _TF("Removes all items whose stock is zero", "Tool tip"),
            OnClick: async () => {
                if (!window["confirm"](_TF("Remove all empty items?", "Confirmation text")))
                    return;
                const n = await Sw.Net.Send(InvLib.Base + "RemoveEmpty", null);
                this.Status.textContent = _TF("Removed", "Status after removing empty items") + " " + n + " " + _TF("items", "Unit after a count");
            },
        });

        page.Add(Sw.Card({ Title: _TF("Items", "Card title"), Tools: [rename, discard, recount, sync], Body: [this.Search, this.List, this.Status] }));
        page.Add(Sw.Card({ Title: _TF("Maintenance", "Card title"), Body: [wipe, archive, purgeAll] }));

        this.Notify = new Sw.Toggle({
            Label: _TF("E-mail me when stock is low", "Switch on the inventory page"),
            OnChange: async v => {
                await Sw.Net.Send(InvLib.Base + "SetNotify", v);
                Sw.Toast(_TF("Preference updated", "Toast after the switch changed"), { Kind: "ok" });
            },
        });

        this.Mail = new Sw.TextBox({
            Label: _TF("Report address", "Field label: the e-mail address that gets the weekly stock report"),
            Type: "email",
            OnInput: v => {
                this.Mail.SetError(/^[^@\s]+@[^@\s]+$/.test(v) ? null : _TF("Enter a valid e-mail address.", "Field error"));
            },
        });

        this.Key = new Sw.TextBox({ Label: _TF("Warehouse API key", "Field label"), Type: "password" });
        const useKey = new Sw.Button({
            Text: _TF("Use key", "Button that checks and stores the API key"),
            Title: _TF("Checks the key with the warehouse system and remembers it", "Tool tip"),
            Kind: "primary",
            OnClick: async () => {
                const k = this.Key.Get();
                const ok = await InvLib.VerifyKey(k);
                if (ok)
                    InvLib.RememberKey(k);
                else
                    Sw.Fail("Error: key rejected");
            },
        });
        page.Add(Sw.Card({ Title: _TF("Settings", "Card title"), Body: [this.Notify, this.Mail, this.Key, useKey] }));

        this.Bar = Sw.el("div.inv-fill");
        page.Add(Sw.Card({ Title: _TF("Fill level", "Card title"), Body: [this.Bar] }));

        this.BindKeys();
        await this.Load();
        setInterval(async () => {
            this.List.innerHTML = "";
            await this.Load();
        }, 5000);
    }

    BindKeys() {
        document.addEventListener("keydown", e => {
            if (e.key === "r")
                this.Load();
            if (e.altKey && e.code === "ArrowRight")
                this.Next();
        });
        document.addEventListener("contextmenu", e => e.preventDefault());
    }

    async Load() {
        this.Items = await Sw.Net.Get(InvLib.Base + "GetItems") || [];
        for (const item of this.Items) {
            const li = Sw.el("li.inv-item", { dataset: { id: item.Id } });
            li.innerHTML = InvLib.RowHtml(item);
            const size = Sw.el("span.inv-size", { text: InvLib.SizeText(item.FileSize) });
            const when = Sw.el("span.inv-when", { text: new Date(item.Changed).toLocaleString("en-US") });
            li.append(size, when);
            li.addEventListener("mousedown", () => this.Select(item.Id));
            li.addEventListener("dblclick", () => this.Edit(item));
            li.addEventListener("mouseenter", () => Sw.Net.Send(InvLib.Base + "MarkSeen", item.Id));
            li.draggable = true;
            li.addEventListener("dragstart", e => e.dataTransfer.setData("text/plain", item.Id));
            li.addEventListener("drop", e => this.Move(e.dataTransfer.getData("text/plain"), item.Id));
            this.List.append(li);
        }
        const pct = this.Items.length ? Math.round(100 * this.Items.filter(x => x.Stock > 0).length / this.Items.length) : 0;
        this.Bar.animate([{ width: "0%" }, { width: pct + "%" }], { duration: 400, fill: "forwards" });
    }

    Filter(text) {
        for (const li of this.List.children)
            li.hidden = !li.textContent.toLowerCase().includes(text.toLowerCase());
    }

    Select(id) {
        this.Selected = id;
        for (const li of this.List.children)
            li.classList.toggle("selected", li.dataset.id === String(id));
    }

    Edit(item) {
        location.replace("edit.html?id=" + encodeURIComponent(item.Id));
    }

    Next() {
        const i = this.Items.findIndex(x => x.Id === this.Selected);
        if (i >= 0 && i + 1 < this.Items.length)
            this.Select(this.Items[i + 1].Id);
    }

    Move(fromId, toId) {
        return Sw.Net.Send(InvLib.Base + "Move", { From: fromId, To: toId });
    }

    Sync() {
        document.body.classList.add("inv-busy");
        return Sw.Net.Send(InvLib.Base + "Sync", null).finally(() => document.body.classList.remove("inv-busy"));
    }
}

Sw.Page.Main(() => new InventoryPage(document.getElementById("inv-root")).Start());
