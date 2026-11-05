/**
 * @file good/h5.js - delivery route: reorder stops by drag, keys or menu; remove a stop with Undo; drop the route with type to confirm.
 */
"use strict";

Sw.Page.Main(async () => {
    const route = new URLSearchParams(location.search).get("route") || "";
    const page = Sw.PageLayout({ Type: "list", Title: _TF("Delivery route", "Page title"), Description: _TF("The order in which the driver visits the stops.", "Page description") });
    document.body.append(page.E);

    const list = Sw.el("ol.sw-list", { attrs: { "aria-label": _TF("Stops", "Accessible name of the list of stops") } });
    const status = Sw.el("p.sw-selectable", { attrs: { role: "status" } });
    const stops = await Sw.Net.Get("../Api/ship/GetStops?route=" + encodeURIComponent(route)) || [];

    /** Saves the order; a failure is said once at the list (the order on screen is put back by the caller). */
    async function save(what) {
        try {
            await Sw.Net.Send("../Api/ship/SaveStopOrder", { Route: route, Ids: stops.map(s => s.Id) });
            return true;
        } catch (e) {
            if (Sw.Net.ShouldReport(e))
                status.textContent = Sw.Net.FailText(what, e);
            return false;
        }
    }

    async function move(i, d) {
        const j = i + d;
        if (j < 0 || j >= stops.length)
            return;
        [stops[i], stops[j]] = [stops[j], stops[i]];
        render(j);
        if (!await save(_TF("Move stop", "Name of the action in a failure line"))) {
            [stops[i], stops[j]] = [stops[j], stops[i]];
            render(i);
            return;
        }
        status.textContent = _T("{0} is now stop {1}.", stops[j].Name, Sw.Format.Number(j + 1), "Status after a move. {0}: stop name, ex: Ica Maxi; {1}: position, ex: 3");
    }

    function render(focusAt) {
        list.textContent = "";
        stops.forEach((s, i) => {
            const handle = Sw.el("span.sw-drag-handle", { attrs: { "aria-hidden": "true" } }, Sw.Icon("grip"));
            const more = new Sw.Button({
                Icon: "more", Kind: "ghost", Small: true,
                Title: _T("Move or remove {0}", s.Name, "Tool tip of the stop menu. {0}: stop name"),
                Menu: () => [
                    { Text: _TF("Move up", "Menu item"), Disabled: i === 0, OnClick: () => move(i, -1) },
                    { Text: _TF("Move down", "Menu item"), Disabled: i === stops.length - 1, OnClick: () => move(i, 1) },
                    "-",
                    { Text: _TF("Remove stop", "Menu item"), Danger: true, OnClick: () => removeStop(i) },
                ],
            });
            const li = Sw.el("li", { attrs: { tabindex: i === (focusAt || 0) ? "0" : "-1", "aria-keyshortcuts": "Alt+ArrowUp Alt+ArrowDown" } }, handle, Sw.el("span", { text: s.Name }), more);
            li.addEventListener("keydown", ev => {
                if (ev.key !== "ArrowUp" && ev.key !== "ArrowDown")
                    return;
                ev.preventDefault();
                const d = ev.key === "ArrowUp" ? -1 : 1;
                if (ev.altKey)
                    move(i, d);
                else
                    list.children[Math.max(0, Math.min(stops.length - 1, i + d))]?.focus();
            });
            Sw.Drag(handle, {
                OnEnd: (ev, st, cancelled) => {
                    const steps = cancelled ? 0 : Math.round(st.DY / li.offsetHeight);
                    if (steps)
                        move(i, Math.sign(steps));
                },
            });
            list.append(li);
            if (i === focusAt)
                li.focus();
        });
    }

    async function removeStop(i) {
        const [gone] = stops.splice(i, 1);
        render(Math.min(i, stops.length - 1));
        if (!await save(_TF("Remove stop", "Name of the action in a failure line"))) {
            stops.splice(i, 0, gone);
            render(i);
            return;
        }
        Sw.Toast(_T("{0} was removed from the route.", gone.Name, "Toast after removing a stop. {0}: stop name"), {
            Kind: "info",
            Action: { Text: _TF("Undo", "Toast button that puts the stop back"), OnClick: () => { stops.splice(i, 0, gone); render(i); return save(_TF("Undo", "Name of the action in a failure line")); } },
        });
    }

    const drop = new Sw.Button({
        Text: _TF("Delete route", "Button that deletes the whole route"),
        Title: _TF("Deletes this route and all its stops for every driver (asks first)", "Tool tip"),
        Kind: "danger",
        OnClick: async () => {
            const ok = await Sw.Cells.ConfirmDanger({
                Title: _T("Delete the route {0}?", route, "Confirmation title. {0}: route name, ex: North 2"),
                Text: _TF("The route and its stops are removed for every driver.", "Confirmation text"),
                Confirm: route,
                OkText: _TF("Delete route", "Confirmation button"),
            });
            if (!ok)
                return;
            await Sw.Net.Send("../Api/ship/DeleteRoute", { Route: route });
            Sw.Page.Navigate("routes.html");
        },
    });

    page.Add(Sw.Card({ Title: _TF("Stops", "Card title"), Body: [list, status] }));
    page.Add(Sw.Card({ Title: _TF("Route", "Card title"), Body: [drop] }));
    render(0);
});
