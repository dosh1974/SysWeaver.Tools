/**
 * @file R5 doc-only page: "Contacts" built from ui/README, core/README, explore/README and PORTING C11 only, using the round 4 APIs
 * (Sw.Net.Read, ReadOnly, Labels, a server value written by Create, Draft, Locked, Sw.Overlay.Register, Sw.Net.Message, RunServer).
 */
Sw.Page.Main(async () => {
    const api = Sw.Url.Root + "Api/labFake/";
    const rows = [];
    for (let i = 1; i <= 37; ++i)
        rows.push({ No: "C-" + (10000 + i), Name: ["Initech", "Globex", "Umbrella", "Hooli", "Acme"][i % 5] + " " + i, City: ["Malmo", "Lund", "Oslo"][i % 3], Open: i * 13 % 900 });
    const newContact = () => {
        const name = new Sw.TextBox({ Label: _T("Name", "Contacts"), Required: true });
        const mail = new Sw.TextBox({ Label: _T("E-mail", "Contacts"), Type: "email" });
        const no = new Sw.TextBox({ Label: _T("Customer no.", "Contacts"), ReadOnly: true, Hint: _T("Given by the server when created.", "Contacts") });
        return Sw.Dialog.Show({ Title: _T("New contact", "Contacts"), Id: "lab.contacts.new", Content: Sw.el("div.sw-stack", null, [name.E, mail.E, no.E]),
            Buttons: [{ Text: _T("Cancel", "Contacts"), Value: null, Labels: [_T("Close", "Contacts")] },
                { Text: _T("Create", "Contacts"), Kind: "primary", Value: "ok", OnClick: async () => {
                    if (name.Validate()) throw new Error(name.Validate());
                    const r = await Sw.Net.Send(api + "CreateContact", { Name: name.Get(), Mail: mail.Get() });
                    no.Set(r && r.Number); } }] });
    };
    const findDuplicates = () => Sw.Dialog.Show({ Title: _T("Find duplicates", "Contacts"), Id: "lab.contacts.dup", Content: _T("Looks for contacts with the same name or e-mail.", "Contacts"),
        Buttons: [{ Text: _T("Close", "Contacts"), Value: null },
            { Text: _T("Search", "Contacts"), Kind: "primary", Value: 1, Cancellable: true, OnClick: async (close, value, { Signal }) => { const r = await Sw.Net.Read(api + "FindDuplicates", {}, { Signal }); status.textContent = _T("{0} possible duplicates", (r && r.Count) || 0, "Contacts"); } }] });
    const exportBtn = new Sw.Button({ Text: _T("Export…", "Contacts"), Icon: "download", OnClick: () => Sw.Activity.RunServer(api + "StartContactsExport", {}, { Title: _T("Contacts export", "Contacts") }) });
    const page = Sw.PageLayout({ Type: "dash", Title: _T("Contacts", "Contacts"), Description: _T("Customers and their contact people.", "Contacts"),
        Actions: [new Sw.Button({ Text: _T("New contact…", "Contacts"), Icon: "add", Kind: "primary", OnClick: newContact }),
            new Sw.Button({ Text: _T("Find duplicates…", "Contacts"), OnClick: findDuplicates }), exportBtn] });
    const status = Sw.el("p.sw-muted", { attrs: { role: "status" } });
    const host = Sw.el("div");
    page.Add(Sw.Card({ Title: _T("All contacts", "Contacts"), Body: [status, host] }));
    window.CT = { table: await Sw.Table.Create({ Element: host, Name: _T("Contacts", "Contacts"), Rows: rows, Order: ["Name"] }) };
    // Notes with a draft and a save
    const notes = new Sw.TextBox({ Label: _T("Team notes", "Contacts"), Multiline: true, Rows: 4, Draft: "lab.contacts.notes" });
    const saveNotes = new Sw.Button({ Text: _T("Save notes", "Contacts"), Kind: "primary", OnClick: () => Sw.Net.Send(api + "SaveNotes", { Text: notes.Get() }) });
    page.Add(Sw.Card({ Title: _T("Notes", "Contacts"), Body: [notes.E], Actions: [saveNotes] }));
    // Settings: an auto saving switch and a number locked while a check runs
    const digest = new Sw.Toggle({ Label: _T("Weekly digest e-mail", "Contacts"), OnChange: v => Sw.Net.Send(api + "SetDigest", { On: v }) });
    const port = new Sw.NumberBox({ Label: _T("Import port", "Contacts"), Value: 8080, Min: 1, Max: 65535 });
    const test = new Sw.Button({ Text: _T("Test connection", "Contacts"), OnClick: async () => { port.Locked(true); try { await Sw.Net.Read(api + "TestPort", { Port: port.Get() }); } finally { port.Locked(false); } } });
    page.Add(Sw.Card({ Title: _T("Settings", "Contacts"), Body: [digest.E, port.E, test.E] }));
    // A floating "compare" bar while two rows are picked (own fixed overlay, registered)
    const bar = Sw.el("div.lab-compare", { attrs: { style: "position:fixed;right:16px;bottom:16px;padding:8px 12px;border-radius:var(--sw-radius);background:var(--sw-surface-raised, var(--sw-surface));border:1px solid var(--sw-border);display:none" } },
        [_T("2 contacts picked", "Contacts"), " ", new Sw.Button({ Text: _T("Compare", "Contacts"), OnClick: async () => 1 }).E]);
    document.body.append(bar);
    Sw.Overlay.Register(bar, { Name: _T("Compare bar", "Contacts") });
    CT.showBar = on => { bar.style.display = on ? "flex" : "none"; };
    Object.assign(CT, { notes, saveNotes, digest, port, test, exportBtn, status });
});
