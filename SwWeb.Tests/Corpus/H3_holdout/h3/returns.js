/*
 * Warehouse returns page: the open returns, the filters, bulk approve and the live feed of new returns.
 * The server calls are in returns-api.js, the detail dialog in returns-detail.js.
 */
(function () {
    "use strict";

    const api = window.ReturnsApi;

    class ReturnsPage {
        constructor() {
            this.host = document.getElementById("listHost");
            this.listE = document.getElementById("list");
            this.countE = document.getElementById("count");
            this.lateE = document.getElementById("late");
            this.search = document.getElementById("search");
            this.rows = new Map();
            this.items = [];
            this.state = null;
            this.loading = null;
        }

        Start() {
            this.buildActions();
            this.buildReasonFilter();
            this.search.addEventListener("input", Sw.Util.Debounce(() => this.Load(), 300));
            this.listE.addEventListener("contextmenu", ev => ev.preventDefault());
            document.addEventListener("keydown", ev => this.onKey(ev));
            Sw.Session.Subscribe("returns.created", msg => this.onCreated(msg));
            api.Reasons().then(list => this.fillReasons(list));
            setInterval(() => api.Counts().then(c => this.showCounts(c)).catch(() => { }), 30000);
            if (!Sw.Platform.IsTouch)
                this.search.focus();
            return this.Load();
        }

        buildActions() {
            this.approveButton = new Sw.Button({
                Text: _TF("Approve checked", "Page header button of the warehouse returns page: approves the returns whose check box is set"),
                Title: _TF("Move the checked returns to the refund queue (A)", "Tool tip of the Approve checked button; (A) is its keyboard shortcut"),
                Kind: "primary",
                Disabled: _TF("Check one or more returns in the list first", "Reason shown when Approve checked is used while no return is checked"),
                OnClick: () => this.approveChecked(),
            });
            const exportButton = new Sw.Button({
                Text: _TF("Export", "Page header button that exports the listed returns to a file"),
                Icon: "download",
                Title: _TF("Export the returns in the list to an Excel file", "Tool tip of the Export button on the warehouse returns page"),
                OnClick: () => { api.Export(this.filter()); },
            });
            document.getElementById("actions").append(this.approveButton.E, exportButton.E);
        }

        buildReasonFilter() {
            this.reason = new Sw.Select({
                Label: _TF("Reason", "Label of the filter that shows only returns with one return reason"),
                Items: [{ Value: "", Text: _TF("All reasons", "Choice of the return reason filter that shows every return") }],
                Value: "",
                Dirty: false,
                OnChange: () => { this.Load(); },
            });
            document.getElementById("reasonHost").append(this.reason.E);
        }

        fillReasons(list) {
            const all = { Value: "", Text: _TF("All reasons", "Choice of the return reason filter that shows every return") };
            this.reason.SetOptions([all, ...list.map(x => ({ Value: x.Id, Text: x.Name }))], this.reason.Get());
        }

        filter() {
            return { Search: this.search.value.trim(), Reason: this.reason.Get() || null };
        }

        async Load() {
            if (this.loading)
                this.loading.abort();
            const ctl = this.loading = new AbortController();
            if (!this.items.length)
                this.showState({ Kind: "loading", Small: true, Lines: 6 });
            document.body.classList.add("is-loading");
            try {
                const res = await api.List(this.filter(), ctl.signal);
                this.render(res.Items || []);
            } catch (e) {
                if (Sw.Cancel.IsAbort(e) || !Sw.Net.ShouldReport(e))
                    return;
                this.showState({
                    Kind: "error",
                    Title: _TF("The returns could not be loaded", "Title of the error state of the list of returns"),
                    Text: e.Status + " - " + e.StatusText,
                    Actions: [new Sw.Button({
                        Text: _TF("Try again", "Button in the error state of the list of returns"),
                        Title: _TF("Load the open returns again", "Tool tip of Try again in the error state of the list of returns"),
                        OnClick: () => this.Load(),
                    })],
                });
            } finally {
                if (this.loading === ctl)
                    document.body.classList.remove("is-loading");
            }
        }

        showState(o) {
            this.listE.hidden = true;
            if (!this.state) {
                this.state = Sw.StateView(o);
                this.host.append(this.state.E);
            } else
                this.state.Set(o);
            this.state.E.hidden = false;
        }

        showCount(n) {
            this.countE.textContent = _T("{0} open return", Sw.Format.Number(n), "Number of returns in the list, after the list title. {0}: a count, ex: 12") + (n === 1 ? "" : "s");
        }

        showCounts(c) {
            this.lateE.textContent = c.Late > 0 ? _T("Late: {0}", Sw.Format.Number(c.Late), "Status after the page title: returns past their check deadline. {0}: a count, ex: 3") : "";
        }

        render(items) {
            this.items = items;
            this.showCount(items.length);
            if (!items.length) {
                this.rows.clear();
                this.listE.replaceChildren();
                this.showState({
                    Kind: "empty",
                    Small: true,
                    Title: _TF("No open returns", "Title of the empty state of the list of returns"),
                    Text: _TF("Returns registered in the shop show up here as soon as the customer has printed the label.", "Text of the empty state of the list of returns"),
                });
                return;
            }
            if (this.state)
                this.state.E.hidden = true;
            this.listE.hidden = false;
            const keep = Sw.Focus.Keep(this.listE);
            this.rows.clear();
            this.listE.replaceChildren(...items.map(r => this.row(r)));
            keep.Restore();
            this.updateChecked();
        }

        row(r) {
            const { Id, Rma, Customer, Refund, Currency, Created, Late, Restock, Progress } = r;
            const li = Sw.el("li.ret-row", { dataset: { swItem: Id } });
            const check = Sw.el("input", { type: "checkbox", attrs: { "aria-label": _T("Check return {0}", Rma, "Accessible name of the check box of one return in the list. {0}: the return number, ex: RMA-10442") } });
            check.addEventListener("change", () => this.updateChecked());

            const dot = Sw.el("span.ret-dot", { style: { background: Late ? "var(--sw-danger)" : "var(--sw-ok)" } });
            const name = Sw.el("div.ret-name.sw-selectable", { text: Rma + " · " + Customer });
            const meta = Sw.el("div.ret-meta.sw-selectable", {
                text: _T("Registered {0}, refund {1}",
                    api.LabelDate(Created),
                    api.RefundText(Refund, Currency),
                    "Second line of a return in the list. {0}: the day it was registered, ex: 03.10.2026; {1}: the refund amount, ex: 49.90 EUR"),
            });
            const state = Sw.el("span.ret-state.sw-selectable", { text: _TF("Waiting", "State of a return that has not arrived at the warehouse yet") });
            const hint = Sw.el("span.ret-meta", { text: _TF("Double-click to open the details", "Hint on a return in the list") });
            const progress = Sw.el("div.ret-progress", {
                attrs: { role: "progressbar", "aria-label": "Inspection progress", "aria-valuemin": "0", "aria-valuemax": "100", "aria-valuenow": String(Math.round((Progress || 0) * 100)) },
            }, Sw.el("i", { style: { width: (Progress || 0) * 100 + "%" } }));

            const receive = Sw.el("button.sw-button.sw-small", {
                type: "button",
                text: _TF("Receive", "Button on a return in the list: the parcel arrived at the warehouse"),
                title: _T("Mark {0} as arrived at the warehouse", Rma, "Tool tip of the Receive button. {0}: the return number, ex: RMA-10442"),
            });
            receive.addEventListener("pointerdown", () => this.receive(Id, li));

            const restock = Sw.el("input", {
                type: "checkbox",
                checked: !!Restock,
                title: _T("Put the items of {0} back in stock when they arrive", Rma, "Tool tip of the Restock check box. {0}: the return number, ex: RMA-10442"),
            });
            restock.addEventListener("change", () => { api.SetRestock(Id, restock.checked); });
            const restockLabel = Sw.el("label.ret-meta", null, restock, " ", _TF("Restock", "Check box on a return: put its items back in stock"));

            const more = new Sw.Button({
                Icon: "more",
                Kind: "ghost",
                Small: true,
                AriaLabel: _T("More actions for {0}", Rma, "Accessible name of the menu button of one return. {0}: the return number, ex: RMA-10442"),
                Title: _TF("Print the label or change the carrier key", "Tool tip of the menu button of a return"),
                Menu: () => this.menuItems(r),
            });
            more.E.addEventListener("mouseenter", () => more.OpenMenu());

            li.addEventListener("dblclick", ev => {
                if (!Sw.IsPureEvent(ev))
                    return;
                window.ReturnDetail.Open(Id, { OnChanged: () => this.Load() });
            });

            li.append(
                Sw.el("div.ret-tools", null, check, dot),
                Sw.el("div.ret-main", null, name, meta, state, hint, progress),
                Sw.el("div.ret-tools", null, restockLabel, receive, more.E));
            this.rows.set(Id, { li, check });
            return li;
        }

        menuItems(r) {
            return [
                {
                    Text: _TF("Print label", "Menu item of a return: prints the return label at the packing desk"),
                    Icon: "download",
                    OnClick: () => this.printLabel(r),
                },
                {
                    Text: _TF("Change carrier key", "Menu item of a return: asks for the API key of the parcel carrier"),
                    Icon: "key",
                    OnClick: () => this.askCarrierToken(),
                },
            ];
        }

        async askCarrierToken() {
            const token = await Sw.Dialog.Prompt(
                _TF("Change the carrier key", "Title of the dialog that asks for the API key of the parcel carrier"), "", "",
                { Type: "password", Label: _TF("Carrier API key", "Label of the field for the API key of the parcel carrier"), OkText: _TF("Save key", "Button that keeps the carrier API key") });
            if (token)
                api.CarrierToken.Remember(token);
            return token;
        }

        async printLabel(r) {
            const token = api.CarrierToken.Get() || await this.askCarrierToken();
            if (!token)
                return;
            try {
                await api.PrintLabel(r.Id, token);
                Sw.Toast("Label sent to the printer at the packing desk", { Kind: "ok" });
            } catch (e) {
                if (Sw.Net.ShouldReport(e))
                    Sw.Fail(Sw.Net.FailText(_TF("Print label", "Name of the action in the failure message when a return label could not be printed"), e));
            }
        }

        async receive(id, li) {
            await api.MarkReceived(id);
            li.classList.add("is-received");
            li.querySelector(".ret-state").textContent = _TF("Received", "State of a return that arrived at the warehouse");
        }

        checkedIds() {
            return [...this.rows].filter(([, row]) => row.check.checked).map(([id]) => id);
        }

        updateChecked() {
            this.approveButton.SetDisabled(this.checkedIds().length === 0,
                _TF("Check one or more returns in the list first", "Reason shown when Approve checked is used while no return is checked"));
        }

        async approveChecked() {
            const ids = this.checkedIds();
            const ok = await Sw.Dialog.Show({
                Title: _T("Approve the checked returns ({0})?", Sw.Format.Number(ids.length), "Title of the confirmation before approving returns. {0}: how many, ex: 4"),
                Content: _TF("Approved returns move to the refund queue, where they can still be rejected.", "Text of the confirmation before approving returns"),
                Buttons: [
                    { Text: _TF("No", "Button that closes the approve confirmation without approving"), Value: false },
                    { Text: _TF("Yes", "Button that approves the checked returns"), Kind: "primary", Value: true },
                ],
            });
            if (!ok)
                return;
            await Promise.allSettled(ids.map(id => api.Approve(id)));
            return this.Load();
        }

        onCreated(r) {
            if (this.rows.has(r.Id))
                return;
            this.items.unshift(r);
            if (this.items.length === 1)
                return this.render(this.items);
            const li = this.row(r);
            li.classList.add("is-new");
            this.listE.prepend(li);
            this.showCount(this.items.length);
            Sw.Page.Announce(_TF("New return", "Read by screen readers when a return arrives") + " " + r.Rma + " " + _TF("from", "Word between the return number and the customer name") + " " + r.Customer);
        }

        onKey(ev) {
            if (ev.key !== "a" || ev.ctrlKey || ev.metaKey || ev.altKey || ev.repeat)
                return;
            ev.preventDefault();
            this.approveButton.Click();
        }
    }

    Sw.Page.Main(() => new ReturnsPage().Start());
})();
