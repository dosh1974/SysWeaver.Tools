"use strict";
// Mail settings: a Sw.Form with required fields, a masked password, a drafted signature and Mod+S
Sw.Page.Main(async () => {
    const save = new Sw.Button({ Text: _TF("Save", "Button: save the mail settings"), Kind: "primary", Shortcut: "Mod+S",
        Title: _TF("Save the mail settings", "Tool tip of the Save button of the mail settings") });
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Mail settings", "Page title"),
        Description: _TF("The server that sends the notification mails", "Page description of the mail settings"), Actions: [save] });
    document.body.append(page.E);

    const host = new Sw.TextBox({ Label: _TF("SMTP server", "Label of the mail server host name"), AutoComplete: "off" });
    const port = new Sw.NumberBox({ Label: _TF("Port", "Label of the mail server port"), Min: 1, Max: 65535, Step: 1, Value: 587 });
    const sender = new Sw.TextBox({ Label: _TF("Sender address", "Label of the e-mail address mails are sent from"), Type: "email",
        Hint: _TF("Mails are sent from this address, ex: noreply@example.com", "Hint under the sender address") });
    const user = new Sw.TextBox({ Label: _TF("User name", "Label of the mail server user name"), AutoComplete: "username" });
    const pass = new Sw.TextBox({ Label: _TF("Password", "Label of the mail server password"), Type: "password", Reveal: true, AutoComplete: "new-password" });
    const signature = new Sw.TextBox({ Label: _TF("Signature", "Label of the text added under every mail"), Multiline: true, Rows: 4 });

    const form = Sw.Form({
        Fields: [
            { Name: "Host", Control: host, Required: _TF("Enter the name of the mail server.", "Error: the mail server is empty") },
            { Name: "Port", Control: port, Required: true },
            { Name: "Sender", Control: sender, Required: _TF("Enter the sender address.", "Error: the sender address is empty"),
                Validate: v => Sw.Format.IsValidEmail(v) ? null : _TF("Enter an address like noreply@example.com.", "Error: the sender address is not an e-mail address") },
            { Name: "User", Control: user },
            { Name: "Password", Control: pass },
            { Name: "Signature", Control: signature },
        ],
        Submit: save,
        Title: _TF("the mail settings", "Name of the mail settings in the leave question"),
        Draft: "mailsettings",
        OnSubmit: v => Sw.Net.Send("../Api/mail/SaveSettings", v),
    });

    page.Add(Sw.Card({ Title: _TF("Server", "Card title: the mail server"), Body: [host, port, user, pass] }),
        Sw.Card({ Title: _TF("Mails", "Card title: what the mails look like"), Body: [sender, signature] }));

    Sw.Keys.Add("Mod+S", () => form.Submit(), { Title: _TF("Save the mail settings", "Shortcut list: Ctrl+S"), InInputs: true });

    const s = await Sw.Net.Read("../Api/mail/GetSettings");
    if (s) {
        host.Set(s.Host || "");
        port.Set(s.Port || 587);
        sender.Set(s.Sender || "");
        user.Set(s.User || "");
    }
});
