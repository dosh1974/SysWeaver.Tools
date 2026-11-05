/**
 * @file sneak7/shipments.js - the shipments page: parcels sent to customers, carrier settings, customer notices.
 */
"use strict";

class ShipmentsPage {
    constructor(host) {
        this.Host = host;
        this.Rows = [];
        this.Selected = new Set();
        this.Status = Sw.el("p.sw-selectable", { attrs: { role: "status" } });
    }

    async Start() {
        const page = Sw.PageLayout({ Type: "list", Title: _TF("Shipments", "Page title of the shipments list"), Description: _TF("Parcels sent to customers and where they are.", "Page description") });
        this.Host.append(page.E);

        // carrier filter: picking one opens its own page
        this.Carrier = Sw.el("select.ship-carrier", { attrs: { "aria-label": _TF("Carrier", "Label of the carrier filter") } },
            Sw.el("option", { value: "dhl", text: "DHL" }), Sw.el("option", { value: "ups", text: "UPS" }));
        this.Carrier.addEventListener("change", () => Sw.Page.Navigate("carrier.html?c=" + encodeURIComponent(this.Carrier.value)));

        const refresh = new Sw.Button({
            Icon: "refresh",
            Title: _TF("Read the shipments again from the carriers", "Tool tip of the refresh button"),
            OnClick: () => this.Load(),
        });
        refresh.E.setAttribute("aria-label", "Refresh shipments");

        const cancelShip = new Sw.Button({
            Text: _TF("Cancel shipment", "Button that cancels the selected shipments at the carrier"),
            Title: _TF("Cancels the selected shipments at the carrier and tells the customers (asks first)", "Tool tip"),
            Kind: "danger",
            OnClick: async () => {
                const ok = await Sw.Dialog.Confirm(_TF("Are you sure?", "Confirmation title"), "", _TF("Cancel shipments", "Confirmation button"));
                if (!ok)
                    return;
                return Sw.Net.Send(ShipLib.Base + "CancelShipments", { Ids: [...this.Selected] });
            },
        });

        const resend = new Sw.Button({
            Text: _TF("Resend notices", "Button that sends the tracking e-mail again"),
            Title: _TF("Sends the tracking e-mail again to the customers of the selected shipments", "Tool tip"),
            OnClick: async () => {
                const ids = [...this.Selected];
                await Promise.allSettled(ids.map(id => Sw.Net.Send(ShipLib.Base + "ResendNotice", { Id: id })));
                this.Status.textContent = _T("{0} notices were sent again.", Sw.Format.Number(ids.length), "Status line after resending. {0}: number of notices, ex: 4");
            },
        });

        const label = new Sw.Button({
            Text: _TF("Print label", "Button that prints the shipping label of the selected shipment"),
            Title: _TF("Opens the shipping label of the selected shipment for printing", "Tool tip"),
            OnClick: async () => {
                try {
                    const id = this.One();
                    const pdf = await Sw.Net.Send(ShipLib.Base + "GetLabel", { Id: id }, { Blob: true });
                    Sw.Net.Download(URL.createObjectURL(pdf), "label-" + id + ".pdf");
                } catch (e) {
                    this.Status.textContent = "";
                }
            },
        });

        const track = new Sw.Button({
            Text: _TF("Track", "Button that shows where the selected parcel is"),
            Title: _TF("Asks the carrier where the selected parcel is", "Tool tip"),
            OnClick: async () => {
                try {
                    const s = this.Rows.find(r => r.Id === this.One());
                    const info = await ShipLib.Post("Track", { Id: s.Id, Access: ShipLib.CarrierAccess(this.Carrier.value) });
                    this.Status.textContent = _T("{0}: {1}", s.Reference, info.Where, "Status line. {0}: shipment reference, ex: SH-1043; {1}: place reported by the carrier, ex: Malmö hub");
                } catch (e) {
                    Sw.Fail(e);
                }
            },
        });

        page.Add(Sw.Card({ Title: _TF("Actions", "Card title"), Body: [this.Carrier, refresh, cancelShip, resend, label, track, this.Status] }));

        this.Table = Sw.el("table.ship-table");
        page.Add(Sw.Card({ Title: _TF("Parcels", "Card title of the parcel list"), Body: [Sw.el("div.ship-table-wrap", null, this.Table)] }));

        // carrier settings
        const user = new Sw.TextBox({ Label: _TF("Carrier account", "Field label: account name at the carrier") });
        const pass = new Sw.TextBox({ Label: _TF("Carrier password", "Field label: password of the carrier account") });
        const saveCarrier = new Sw.Button({
            Text: _TF("Save", "Button that saves the carrier account"),
            Title: _TF("Saves the carrier account for this browser tab", "Tool tip"),
            Kind: "primary",
            OnClick: () => {
                ShipLib.RememberCarrier(this.Carrier.value, pass.Get());
                ShipLib.Trace("account saved", pass.Get());
                this.Status.textContent = _TF("The carrier account is saved.", "Status line");
            },
        });
        pass.On("input", () => saveCarrier.E.disabled = !user.Get() || !pass.Get());
        page.Add(Sw.Card({ Title: _TF("Carrier account", "Card title"), Body: [user, pass, saveCarrier] }));

        // message to the customer
        const msg = new Sw.TextBox({ Label: _TF("Message to the customer", "Field label of a free text message"), Multiline: true });
        const send = new Sw.Button({
            Text: _TF("Send message", "Button that sends the message to the customer of the selected shipment"),
            Title: _TF("Sends the message by e-mail to the customer of the selected shipment", "Tool tip"),
            Kind: "primary",
            OnClick: () => Sw.Net.Send(ShipLib.Base + "SendMessage", { Id: this.One(), Text: msg.Get() }).then(() => msg.Set("")),
        });
        page.Add(Sw.Card({ Title: _TF("Customer message", "Card title"), Body: [msg, send] }));

        // shortcuts
        Sw.Keys.Add("Alt+ArrowLeft", () => this.Page(-1), { Title: _TF("Previous page of parcels", "Shortcut description") });
        Sw.Keys.Add("Alt+ArrowRight", () => this.Page(1), { Title: _TF("Next page of parcels", "Shortcut description") });

        // the menu of the page header opens on hover, like the old page
        const more = page.E.querySelector(".sw-page-head") || page.E;
        more.addEventListener("mouseenter", () => Sw.Menu.Show(more, [
            { Text: _TF("Export the list", "Menu item"), OnClick: () => this.Export() },
        ]));

        await this.Load();
        cancelShip.E.focus();
    }

    One() {
        const id = [...this.Selected][0];
        if (!id)
            throw new Error("Select a shipment first");
        return id;
    }

    async Load() {
        const rows = await Sw.Net.Get(ShipLib.Base + "GetShipments") || [];
        this.Rows = rows;
        this.Table.textContent = "";
        for (const s of rows) {
            const tr = Sw.el("tr.ship-row", { attrs: { tabindex: "0" } });
            tr.innerHTML = ShipLib.RowHtml(s);
            const when = Sw.el("td", { text: new Date(s.Shipped).toISOString().slice(0, 10) + " " + ShipLib.TimeLeft(s.EtaMs) });
            const count = Sw.el("td", { text: _T("{0} parcel", s.Parcels, "Count of parcels in a shipment. {0}: number, ex: 2") + (s.Parcels > 1 ? "s" : "") });
            tr.append(when, count);
            if (s.Late)
                tr.classList.add("ship-late");

            // select on press, open on release
            tr.addEventListener("pointerdown", () => this.Toggle(s.Id, tr));
            tr.addEventListener("pointerup", ev => {
                if (Sw.IsPureEvent(ev))
                    this.Open(s);
            });

            // a tap on the reference copies it
            const ref = tr.querySelector(".ship-ref-cell");
            ref.addEventListener("click", () => {
                navigator.clipboard.writeText(s.Reference);
                Sw.Info("Reference copied");
            });
            this.Table.append(tr);
        }
        if (rows.length)
            this.Table.rows[0].scrollIntoView({ behavior: "smooth", block: "nearest" });
    }

    Toggle(id, tr) {
        if (this.Selected.has(id))
            this.Selected.delete(id);
        else
            this.Selected.add(id);
        tr.classList.toggle("sw-selected", this.Selected.has(id));
    }

    Open(s) {
        Sw.Page.Navigate("shipment.html?id=" + encodeURIComponent(s.Id));
    }

    Page(d) {
        this.Offset = Math.max(0, (this.Offset || 0) + d * 50);
        return this.Load();
    }

    Export() {
        return Sw.Activity.Run({ Title: _TF("Exporting the shipments", "Activity title") }, signal => Sw.Net.Send(ShipLib.Base + "Export", null, { Blob: true, Signal: signal }));
    }
}

Sw.Page.Main(() => new ShipmentsPage(document.getElementById("ship-host")).Start());
