/** Critic 4 lab: an "Invoice notes" page built only from the READMEs (ui, core, explore) with every new UX 1.0 API. */
Sw.Page.Main(async () => {
    const q = new URLSearchParams(location.search);
    const delay = (ms, signal, fail) => new Promise((res, rej) => {
        const t = setTimeout(() => fail ? rej(new Error(fail)) : res(), ms);
        if (signal) signal.addEventListener("abort", () => { clearTimeout(t); const e = new Error("aborted"); e.name = "AbortError"; rej(e); });
    });
    const L = window.Lab = { log: [] };
    let saved = "";
    // --- page header with Save (disabled with a reason until dirty) and Export (activity)
    const save = new Sw.Button({ Text: "Save", Icon: "save", Kind: "primary", Disabled: "Change the notes first",
        OnClick: async () => { await delay(+(q.get("saveMs") || 800)); saved = notes.Get(); L.log.push("saved"); markDirty(); draft && draft.Clear(); } });
    const exportB = new Sw.Button({ Text: "Export", Icon: "download", OnClick: () => startExport() });
    const editB = new Sw.Button({ Text: "Edit customer…", Icon: "edit", OnClick: () => editCustomer() });
    const page = Sw.PageLayout({ Type: "form", Title: "Invoice 2026-0042", Description: "Notes and customer of one invoice.", MenuTitle: false, Actions: [editB, exportB, save] });
    // --- facts with copy button
    const copy = Sw.CopyButton({ Value: "INV-2026-0042", What: "Invoice id", Label: "Copy the invoice id" });
    const idRow = Sw.el("div.lab-id", null, Sw.el("span.sw-selectable", { text: "INV-2026-0042" }), copy);
    // --- notes with a draft and SetDirty
    const notes = new Sw.TextBox({ Label: "Notes", Multiline: true, Rows: 6, Hint: "Shown on the printed invoice.", OnInput: () => markDirty(), OnChange: () => markDirty() });
    function markDirty() {
        const d = notes.Get() !== saved;
        Sw.Page.SetDirty("notes", d ? "Your changes to the notes of invoice 2026-0042 are lost." : null);
        save.SetDisabled(!d, "Change the notes first");
    }
    const hint = Sw.Help.Learned("lab.notes-save", { Text: "Press Save (Ctrl+S) to keep the notes; they are drafted while you type.", Target: notes.E });
    const card = Sw.Card({ Title: "Invoice", Body: [idRow, hint.E, notes.E] });
    page.Add(card);
    let draft = null;
    if (!q.get("nodraft"))
        draft = Sw.Draft.Attach(notes.Input, { Context: "lab.invoice.42.notes", OnRestore: () => markDirty() });
    L.notes = notes; L.save = save; L.draft = draft; L.hint = hint; L.copy = copy; L.markDirty = markDirty;
    // --- export as background work
    function startExport(fail) {
        const ac = new AbortController();
        const job = Sw.Activity.Start({ Key: "lab.export.42", Title: "Exporting invoice-0042.pdf", Text: "Rendering pages", Progress: 0,
            Cancel: () => ac.abort(), Retry: () => startExport(false) });
        L.job = job;
        if (!job.IsNew) { Sw.Info("The export is already running"); return; }
        (async () => {
            try {
                for (let i = 1; i <= 5; ++i) { await delay(+(q.get("stepMs") || 600), ac.signal, (fail || q.get("fail")) && i === 3 ? "Renderer crashed (502)" : null); job.Update({ Progress: i / 5 }); }
                job.Done({ Text: "invoice-0042.pdf is ready", Url: "../lab/ux.html" });
            } catch (e) { if (e.name === "AbortError") job.Cancelled(); else job.Fail(e); }
        })();
        return job;
    }
    L.startExport = startExport;
    // --- dialog with Id, fields, async Save, SetBusy
    function editCustomer(o) {
        o = o || {};
        const name = new Sw.TextBox({ Label: "Customer name", Value: "Initech", Required: true });
        const email = new Sw.TextBox({ Label: "E-mail", Value: "billing@initech.example", Type: "email" });
        const vip = new Sw.Toggle({ Label: "VIP customer" });
        const h = Sw.Dialog.Open({ Title: "Edit customer", Id: o.Id === undefined ? "lab.customer" : o.Id, Content: Sw.el("div.sw-stack", null, name.E, email.E, vip.E),
            Buttons: [{ Text: "Cancel", Value: null }, { Text: "Save", Kind: "primary", Value: 1, OnClick: async () => { await delay(+(q.get("dlgMs") || 1500)); L.log.push("dlg saved"); return true; } }] });
        L.dlg = h; L.dlgFields = { name, email, vip };
        return h;
    }
    L.editCustomer = editCustomer;
    // --- local table, live
    if (!q.get("notable")) {
        const mk = n => Array.from({ length: n }, (_, i) => ({ Line: i + 1, Item: ["Stapler", "TPS cover", "Printer", "Toner"][i % 4] + " " + (i + 1), Qty: (i * 7) % 13 + 1, Price: ((i * 31) % 900) / 10 + 1 }));
        const host = Sw.el("div");
        page.Add(Sw.Card({ Title: "Lines", Flush: true, Body: host }));
        L.table = await Sw.Table.Create({ Element: host, Name: "Invoice lines", Rows: mk(+(q.get("n") || 40)),
            Cols: [{ Name: "Line", Type: "System.Int32", Props: 1 | 8 | 32 }, { Name: "Item" }, { Name: "Qty", Type: "System.Int32" }, { Name: "Price", Type: "System.Double", Format: "Number;2" }] });
        L.mk = mk;
    }
    Sw.Keys.Add("Mod+S", () => save.Click(), { Title: "Save the notes", InInputs: true });
    L.ready = true;
});
