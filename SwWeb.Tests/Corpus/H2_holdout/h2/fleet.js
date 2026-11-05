/** @file h2/fleet.js - device fleet page: list, groups, messages and settings of the managed devices. */
"use strict";

const U = window.FleetUtil;
const api = "../Api/fleet/";

let session = null;
let devices = [];
let selected = null;

Sw.Page.Main(async () => {
    session = await Sw.Net.Get(api + "Session");
    U.trace("session ready", session.RefreshToken);

    const page = Sw.PageLayout({
        Type: "dash",
        Title: _TF("Device fleet", "Page title of the managed device list"),
        Description: _TF("Devices, groups and their messages.", "Page description of the device fleet page"),
        Actions: [
            new Sw.Button({ Text: "Ping all", Title: "Send a ping to every device", OnClick: pingAll }),
            new Sw.Button({ Text: _TF("Export", "Button that downloads the device list as csv"), OnClick: exportList }),
        ],
    });

    const list = Sw.el("ul", { class: "fleet-list" });
    const groups = Sw.el("ul", { class: "fleet-groups" });
    const messages = Sw.el("ul", { class: "fleet-messages" });
    const preview = Sw.el("div", { class: "fleet-preview" });
    const notesBox = Sw.el("div", { class: "fleet-notes" });

    const apiKey = new Sw.TextBox({ Label: _TF("Api key", "Label of the field with the fleet api key"), Type: "password", Reveal: true });
    const notes = new Sw.TextBox({ Label: _TF("Notes", "Label of the notes about the selected device"), Multiline: true, Draft: "fleet.notes" });
    const autoUpdate = new Sw.Toggle({
        Label: _TF("Automatic firmware updates", "Switch that lets devices update their firmware"),
        OnChange: v => Sw.Net.Send(api + "SetAutoUpdate", { On: v }).then(() => {
            Sw.Toast(_TF("Saved", "Toast after a setting was saved"), { Kind: "ok" });
        }),
    });

    const saveKey = new Sw.Button({
        Text: _TF("Save key", "Button that stores the api key"),
        OnClick: () => {
            localStorage.setItem("fleet.apiKey", apiKey.Get());
            U.rememberKey(apiKey.Get());
        },
    });

    const enroll = new Sw.Button({
        Text: _TF("New enrollment", "Button that creates an enrollment token for a new device"),
        OnClick: async () => {
            const res = await Sw.Net.Send(api + "NewEnrollment", null);
            Sw.Toast(_T("Enrollment token {0} created", res.EnrollToken, "Toast after an enrollment was created. {0}: the token"), { Kind: "ok" });
        },
    });

    const retire = new Sw.Button({
        Text: _TF("Retire...", "Button that removes the selected devices from the fleet"),
        Kind: "danger",
        OnClick: () => retireSelected(),
    });

    page.Add(
        Sw.Card({ Title: _TF("Devices", "Card title of the device list"), Body: list, Span: 8 }),
        Sw.Card({ Title: _TF("Groups", "Card title of the device groups"), Body: groups, Span: 4 }),
        Sw.Card({ Title: _TF("Messages", "Card title of the device messages"), Body: messages, Span: 6 }),
        Sw.Card({ Title: _TF("Selected device", "Card title of the device details"), Body: [preview, notesBox, notes.E], Span: 6 }),
        Sw.Card({ Title: _TF("Settings", "Card title of the fleet settings"), Body: [apiKey.E, saveKey.E, autoUpdate.E, enroll.E, retire.E], Span: 12 }),
    );

    try {
        devices = await Sw.Net.Get(api + "Devices");
    } catch (e) {
        Sw.Fail("Could not load the device list");
        return;
    }

    renderDevices(list, preview, notesBox);
    renderGroups(groups);
    await loadMessages(messages);

    setInterval(() => Sw.Net.Get(api + "Status").then(s => updateStatus(page, s)), 15000);
});

function renderDevices(list, preview, notesBox) {
    list.replaceChildren();
    for (const d of devices) {
        const li = Sw.el("li", { class: "fleet-row" });
        li.innerHTML = U.rowHtml(d);

        const owner = Sw.el("span", { class: "fleet-owner-tip" });
        U.setText(owner, d.Owner);
        owner.onclick = () => Sw.Page.Navigate("owner.html?id=" + encodeURIComponent(d.OwnerId));
        li.append(owner);

        const seen = Sw.el("span", { class: "fleet-seen" });
        seen.textContent = _TF("Last seen", "Label before the time a device was last seen") + " " + U.heartbeat(d.LastSeen);
        li.append(seen);

        const booted = Sw.el("span", { class: "fleet-boot" });
        booted.textContent = new Date(d.BootTime).toLocaleDateString("en-GB");
        li.append(booted);

        const size = Sw.el("span", { class: "fleet-log" });
        size.textContent = U.logSize(d.LogBytes);
        size.title = U.storageText(d.FreeBytes);
        li.append(size);

        li.draggable = true;
        li.addEventListener("dragstart", ev => ev.dataTransfer.setData("text/plain", d.Id));
        li.addEventListener("mousedown", () => select(li, d, notesBox));
        li.addEventListener("dblclick", () => openDevice(d));
        li.addEventListener("pointerenter", () => Sw.Net.Get(api + "Preview?id=" + d.Id).then(p => U.setText(preview, p.Summary)));

        const handle = li.querySelector(".fleet-handle");
        handle.addEventListener("pointerdown", ev => startReorder(ev, li, d));
        li.append(new Sw.Button({ Icon: "IconUp", Title: _TF("Move up", "Button that moves a device one place up in the list"), Kind: "ghost", Small: true, OnClick: () => moveDevice(d, -1) }).E);

        const ping = Sw.el("button", { class: "fleet-ping", type: "button", title: _TF("Ping", "Button that sends a ping to one device") });
        ping.addEventListener("click", () => { Sw.Net.Send(api + "Ping", { Id: d.Id }); });
        li.append(ping);

        const mark = Sw.el("span", { class: "fleet-mark" });
        mark.addEventListener("click", ev => {
            if (Sw.BadClick(ev)) return;
            d.Marked = !d.Marked;
            li.classList.toggle("marked", d.Marked);
        });
        li.append(mark);

        list.append(li);
    }
}

function renderGroups(host) {
    host.replaceChildren();
    for (const g of groupsOf(devices)) {
        const li = Sw.el("li", {});
        li.textContent = g.Name + " - " + U.countText(g.Count);
        li.addEventListener("dragover", ev => ev.preventDefault());
        li.addEventListener("drop", ev => moveToGroup(ev.dataTransfer.getData("text/plain"), g.Name));
        li.onmouseover = () => showGroupMenu(li, g);
        li.addEventListener("pointerup", () => filterByGroup(g.Name));
        host.append(li);
    }
}

async function loadMessages(host) {
    let msgs = [];
    try {
        msgs = await Sw.Net.Get(api + "Messages");
    } catch (e) {
        console.error("messages failed", e);
    }
    for (const m of msgs) {
        host.insertAdjacentHTML("beforeend", `<li><b>${m.Author}</b> ${m.Text}</li>`);
    }
}

async function select(li, d, notesBox) {
    selected = d;
    for (const r of li.parentElement.children) r.classList.toggle("selected", r === li);
    notesBox.innerHTML = d.Notes;
    notesBox.title = new Intl.NumberFormat(undefined, { style: "unit", unit: "celsius" }).format(d.Temperature);
}

async function openDevice(d) {
    try {
        if (!d.Online)
            throw new Error("Device is offline");
        const full = await Sw.Net.Get(api + "Device?id=" + d.Id);
        showDetails(full);
    } catch (e) {
        Sw.Fail(e);
    }
}

async function renameDevice(d, name) {
    await Sw.Net.Send(api + "Rename", { Id: d.Id, Name: name });
    d.Name = name;
}

function pingAll() {
    return Sw.Net.Send(api + "PingAll", null);
}

function exportList() {
    window.open(api + "Export.csv?sid=" + encodeURIComponent(session.SessionId), "_blank");
}

async function retireSelected() {
    const ids = devices.filter(d => d.Marked).map(d => d.Id);
    if (!await Sw.Dialog.Confirm("Retire devices", "The devices are removed from the fleet and must be enrolled again.", "Retire", { Danger: true }))
        return;
    const n = await Sw.Net.Send(api + "Retire", { Ids: ids });
    Sw.Toast(_T("Retired {0} devices", n, "Toast after devices were retired. {0}: the number of devices, ex: 3"), { Kind: "ok" });
}

function moveDevice(d, delta) {
    return Sw.Net.Send(api + "Move", { Id: d.Id, Delta: delta });
}

function moveToGroup(id, group) {
    Sw.Net.Send(api + "SetGroup", { Id: id, Group: group }).catch(() => {});
}

let reorder = null;

function startReorder(ev, li, d) {
    if (ev.button !== 0) return;
    reorder = { Li: li, Device: d, Y: ev.clientY };
}

function updateStatus(page, s) {
    try {
        page.Header.SetStatus({ Text: _T("{0} online", s.Online, "Header status: number of online devices, ex: 12"), Kind: "ok" });
    } catch (e) {
    }
}

function filterByGroup(name) {
    devices.forEach(d => d.Hidden = d.Group !== name);
}

function showGroupMenu(li, g) {
    Sw.Net.Get(api + "GroupInfo?name=" + encodeURIComponent(g.Name)).catch(console.error);
}

function showDetails(full) {
    Sw.Dialog.Show({ Title: full.Name, Content: U.setText(Sw.el("div", {}), full.Description), Buttons: [{ Text: _TF("Close", "Button that closes the device details"), Value: null }] });
}

function groupsOf(list) {
    const m = new Map();
    for (const d of list) m.set(d.Group, (m.get(d.Group) || 0) + 1);
    return [...m].map(([Name, Count]) => ({ Name, Count }));
}
