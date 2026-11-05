// Holdout h1: planted UX mistakes (see manifest.txt)
Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("H", "t"), Description: _TF("H", "t") });
    const { Net, Dialog, Button } = Sw;
    const api = (name, data) => Net.Send("../Api/h/" + name, data);
    const send = Sw.Net.Send;
    const host = document.createElement("div");
    host.className = "h-host";
    page.Add(host);
    const selected = [];
    const ids = [1, 2, 3];

    const openItem = id => Sw.Page.Navigate("item.html?id=" + encodeURIComponent(id)); // OK1
    const goBack = () => { window.location.assign("../index.html"); }; // H1 SAF-10

    const save = new Button({ Text: _TF("Save", "t"), Kind: "primary", Disabled: selected.length === 0, OnClick: () => api("Save", { Ids: selected }) }); // H2 INP-12
    const archive = new Button({ Text: _TF("Archive", "t"), OnClick: () => Sw.Net.Send("../Api/h/Archive", { Ids: selected }) }); // OK2
    archive.SetDisabled(!selected.length); // H3 INP-12
    const exportBtn = new Button({ Text: _TF("Export", "t"), Disabled: _TF("Select a row first", "t"), OnClick: () => api("Export", { Ids: selected }) }); // OK3
    page.Add(save, archive, exportBtn);

    const legacy = document.createElement("button");
    legacy.type = "button";
    legacy.textContent = _TF("Legacy import", "t");
    legacy.toggleAttribute("disabled", !selected.length); // H4 INP-12
    host.append(legacy);

    const discard = new Button({ Text: _TF("Discard", "t"), OnClick: () => { if (self.confirm(_TF("Discard all changes?", "t"))) Sw.Page.Navigate("../index.html"); } }); // H5 POP-9
    const overwrite = new Button({ Text: _TF("Overwrite", "t"), OnClick: () => Dialog.Show({ Title: _TF("Overwrite the file", "t"), Buttons: [{ Text: _TF("Yes", "t"), Value: true }, { Text: _TF("No", "t"), Value: false }] }) }); // H6 POP-9
    const about = new Button({ Text: _TF("About", "t"), OnClick: () => { const d = Object.assign(document.createElement("dialog"), { className: "h-dlg", textContent: _TF("Version 1", "t") }); document.body.append(d); d["showModal"](); } }); // H7 POP-1
    page.Add(discard, overwrite, about);

    const purge = new Button({ Text: _TF("Purge log", "t"), Kind: "danger", OnClick: () => send("../Api/h/PurgeLog", {}) }); // H8 SAF-1
    function resetCounters() { return Net.Send("../Api/h/ResetCounters", {}); }
    const reset = new Button({ Text: _TF("Reset counters", "t"), Kind: "danger", OnClick: resetCounters }); // H9 SAF-1
    const remove = new Button({ Text: _TF("Delete item", "t"), Kind: "danger", OnClick: async () => { if (!await Dialog.Confirm(_TF("Delete item", "t"), _TF("This cannot be undone.", "t"), _TF("Delete", "t"), { Danger: true })) return; return api("DeleteItem", { Id: ids[0] }); } }); // OK4
    page.Add(purge, reset, remove);

    const syncAll = new Button({ Text: _TF("Sync all", "t"), OnClick: () => { ids.forEach(id => api("Sync", { Id: id })); } }); // H10 FBK-2
    const refresh = new Button({ Text: _TF("Refresh", "t"), OnClick: () => { api("Load", {}).then(r => { host.dataset.count = String(r?.length ?? 0); }); } }); // H11 FBK-2
    const ping = new Button({ Text: _TF("Ping", "t"), OnClick: () => { setTimeout(() => send("../Api/h/Ping", {}), 0); } }); // H12 FBK-2
    page.Add(syncAll, refresh, ping);

    const nameBox = new Sw.TextBox({ Label: _TF("Name", "t") });
    const shown = document.createElement("p");
    host.append(shown);
    const rename = new Button({ Text: _TF("Rename", "t"), OnClick: async () => { await api("Rename", { Name: nameBox.Value }); shown.textContent = nameBox.Value; Sw.Info(_TF("Done", "t")); } }); // H13 FBK-5
    page.Add(nameBox, rename);

    const notesDiv = document.createElement("div");
    notesDiv.setAttribute("role", "textbox");
    notesDiv.setAttribute("aria-label", _TF("Notes", "t"));
    notesDiv.contentEditable = "true"; // H14 SAF-10
    notesDiv.addEventListener("input", () => { sessionStorage["h.notes"] = notesDiv.textContent; }); // H15 SAF-11
    notesDiv.style.cssText = "transition: height var(--sw-dur-2);"; // H16 MOT-4
    notesDiv.setAttribute("tabindex", "1"); // H17 FOC-4
    host.append(notesDiv);
    const saveNotes = new Button({ Text: _TF("Save notes", "t"), OnClick: () => api("SaveNotes", { Text: notesDiv.textContent }) });
    page.Add(saveNotes);

    const apiKey = new Sw.TextBox({ Label: _TF("API key", "t"), Type: "password", Draft: "h.settings.apikey" }); // H18 SAF-11
    const copyKey = new Button({ Text: _TF("Copy key", "t"), OnClick: async () => { await navigator.clipboard.writeText(apiKey.Value); Sw.Toast(`${_TF("Copied", "t")}: ${apiKey.Value}`, { Kind: "ok" }); } }); // H19 SEC-1
    page.Add(apiKey, copyKey);

    const notesBox = new Sw.TextBox({ Label: _TF("Note to the team", "t"), Multiline: true, Rows: 4 });
    const draft = Sw.Draft.Attach(notesBox, { Context: "h.team.note" }); // OK5
    const sendNote = new Button({ Text: _TF("Send note", "t"), Kind: "primary", OnClick: async () => { await api("AddNote", { Text: notesBox.Value }); draft.Clear(); } });
    page.Add(notesBox, sendNote);

    const qty = Object.assign(document.createElement("input"), { type: "number", name: "qty", min: 1 }); // H20 FRM-1
    host.append(qty);

    const grid = Sw.el("table", { class: "h-grid" }); // H21 LST-3
    for (const id of ids) { const tr = grid.insertRow(); tr.insertCell().textContent = String(id); }
    host.append(grid);

    const listEl = document.createElement("ul");
    listEl.className = "h-list";
    host.append(listEl);
    const poll = async () => { const items = await api("Items", {}); listEl.replaceChildren(...items.map(i => Object.assign(document.createElement("li"), { textContent: i.Name }))); setTimeout(poll, 3000); }; // H22 FBK-7
    poll();

    window.addEventListener("keydown", e => { if (e.altKey && e.code === "ArrowLeft") { e.preventDefault(); goBack(); } }); // H23 INP-23
    Sw.Keys.Add("Ctrl+W", () => host.remove(), { Title: _TF("Close panel", "t") }); // H24 INP-23

    const card = document.createElement("div");
    card.className = "h-card";
    card.textContent = _TF("Open item", "t");
    card.onclick = function () { openItem(ids[0]); }; // H25 INP-2
    host.append(card);
    card.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 250, easing: "ease-out" }); // H26 MOT-2

    host.insertAdjacentHTML("afterbegin", `<h1 class="h-sub">${_TF("Details", "t")}</h1>`); // H27 ACC-3
});
