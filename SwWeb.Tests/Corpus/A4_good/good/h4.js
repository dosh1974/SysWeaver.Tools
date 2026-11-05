/**
 * @file good/h4.js - a message to a customer: drafted text, one result path, the server's reason shown at the form.
 */
"use strict";

Sw.Page.Main(async () => {
    const id = new URLSearchParams(location.search).get("id") || "";
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Customer message", "Page title"), Description: _TF("Write to the customer of one shipment.", "Page description") });
    document.body.append(page.E);

    const subject = new Sw.TextBox({ Label: _TF("Subject", "Field label of the message subject"), MaxLength: 120 });
    const body = new Sw.TextBox({ Label: _TF("Message", "Field label of the message text"), Multiline: true, Rows: 8 });
    const status = Sw.el("p.sw-selectable", { attrs: { role: "status" } });
    const draft = Sw.Draft.Attach(body, { Context: "ship.message." + id });

    const send = new Sw.Button({
        Text: _TF("Send message", "Button that e-mails the message to the customer"),
        Title: _TF("E-mails the message to the customer of this shipment", "Tool tip"),
        Kind: "primary",
        OnClick: async () => {
            if (!body.Get().trim()) {
                body.SetError(_TF("Write the message first.", "Field message"));
                body.Focus();
                return false;
            }
            try {
                await Sw.Net.Send("../Api/ship/SendMessage", { Id: id, Subject: subject.Get(), Text: body.Get() });
            } catch (e) {
                status.textContent = Sw.Net.Message(_TF("The message was not sent", "Error line under the message"), e, { Do: _TF("Your text is kept; try again in a moment.", "Advice after a failed send") });
                return false;
            }
            await draft.Clear();
            body.Set("");
            subject.Set("");
            status.textContent = _TF("The message was sent.", "Status line after sending");
        },
    });

    page.Add(Sw.Card({ Title: _TF("Message", "Card title"), Body: [subject, body, send, status] }));
});
