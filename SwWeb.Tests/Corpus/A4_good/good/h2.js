/**
 * @file good/h2.js - carrier account form: validation by Sw.Form, a masked password, server reasons at the form.
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Carrier account", "Page title"), Description: _TF("The account the app uses to book parcels.", "Page description") });
    document.body.append(page.E);

    const account = new Sw.TextBox({ Label: _TF("Account name", "Field label: account name at the carrier"), AutoComplete: "off", SpellCheck: false });
    const mail = new Sw.TextBox({ Label: _TF("Contact e-mail", "Field label: e-mail the carrier writes to"), Type: "email", AutoComplete: "email" });
    const password = new Sw.TextBox({ Label: _TF("Account password", "Field label: password of the carrier account"), Type: "password", Reveal: true, AutoComplete: "new-password" });
    const note = new Sw.TextBox({ Label: _TF("Note for colleagues", "Field label of a free text note"), Multiline: true, Hint: _TF("Ex: who to call when bookings fail", "Hint under the note field") });
    const save = new Sw.Button({ Text: _TF("Save", "Button that saves the carrier account"), Title: _TF("Saves the carrier account on the server (Ctrl+S)", "Tool tip"), Kind: "primary", Shortcut: "Mod+S" });

    const form = Sw.Form({
        Fields: [
            { Name: "Account", Control: account, Required: _TF("Enter the account name.", "Field message") },
            { Name: "Mail", Control: mail, Required: _TF("Enter a contact e-mail.", "Field message"), Validate: v => Sw.Format.IsValidEmail(v) ? null : _TF("Enter an e-mail like name@example.com.", "Field message") },
            { Name: "Password", Control: password, Required: _TF("Enter the password.", "Field message") },
            { Name: "Note", Control: note },
        ],
        Submit: save,
        Draft: "ship.carrier",
        Title: _TF("the carrier account", "Used in: Your changes to the carrier account are lost."),
        OnSubmit: v => Sw.Net.Send("../Api/ship/SaveCarrier", v),
    });
    Sw.Keys.Add("Mod+S", () => form.Submit(), { Title: _TF("Save the carrier account", "Shortcut description"), InInputs: true });

    page.Add(Sw.Card({ Title: _TF("Account", "Card title"), Body: [account, mail, password, note, save] }));
});
