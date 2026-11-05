/** R3 secrets probe: fields with innocent / foreign / indirect labels, all drafted by Sw.Draft.Attach. */
Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: "Secrets probe", MenuTitle: false });
    const specs = [
        ["notesA", "Notes", true], ["notesB", "Notes (PEM)", true], ["pass", "Passphrase"], ["cred", "Credentials"],
        ["bearer", "Authorization header"], ["pw", "PW"], ["conn", "Connection string"], ["de", "Passwort"], ["de2", "Zugangsschlüssel"],
        ["sv", "Lösenord"], ["seed", "Recovery phrase", true], ["iban", "IBAN"], ["cc", "Card number"]];
    const L = window.Sec = { h: {}, f: {} };
    const body = [];
    for (const [id, label, multi] of specs) {
        const tb = new Sw.TextBox({ Label: label, Multiline: !!multi, Rows: 3 });
        tb.Input.id = id;
        L.f[id] = tb; body.push(tb.E);
    }
    page.Add(Sw.Card({ Title: "Fields", Body: body, Actions: [new Sw.Button({ Text: "Save", Kind: "primary", OnClick: async () => true })] }));
    for (const [id] of specs) L.h[id] = Sw.Draft.Attach(L.f[id], { Context: "lab.sec." + id });
    L.ready = true;
});
