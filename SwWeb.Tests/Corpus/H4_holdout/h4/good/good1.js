// good1.js - payment provider settings: the API key is masked, sent in the request body and copied without showing it
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({
        Type: "form",
        Title: _TF("Payment provider", "Title of the payment provider settings page of the billing app"),
        Description: _TF("The key the billing app uses to read payments.", "One line description under the title of the payment provider settings page"),
    });
    const key = new Sw.TextBox({
        Label: _TF("API key", "Label of the field that holds the API key of the payment provider"),
        Type: "password",
        Reveal: true,
        AutoComplete: "off",
        SpellCheck: false,
        Hint: _TF("You find it under Developers in the dashboard of the provider.", "Hint under the API key field: where the user finds the key"),
    });
    const copy = Sw.CopyButton({
        Get: () => key.Get(),
        Secret: true,
        What: _TF("API key", "Name of the copied value, read as 'API key copied'"),
        Label: _TF("Copy the API key", "Accessible name of the copy button next to the API key field"),
    });
    const save = new Sw.Button({
        Text: _TF("Save key", "Button that stores the API key of the payment provider on the server"),
        Kind: "primary",
        Title: _TF("Store the key on the server; the page never shows it again", "Tool tip of the Save key button"),
    });
    Sw.Form({
        Fields: [{ Name: "Key", Control: key, Required: _TF("Enter the API key.", "Error under the API key field when it is empty") }],
        Submit: save,
        OnSubmit: values => Sw.Net.Send(Sw.Url.Root + "Api/invoices/SetProviderKey", { Key: values.Key }),
    });
    page.Add(Sw.Card({
        Title: _TF("Access", "Card title on the payment provider settings page"),
        Body: [key, Sw.el("div.sw-row", null, copy, save)],
    }));
});
