/*
 * Carrier account page: the account name and API key the warehouse uses to print return labels. The key is masked with a Show toggle,
 * never drafted and never stored in the browser; the form validates inline and keeps the entries when the save fails.
 */
(function () {
    "use strict";

    Sw.Page.Main(() => {
        const page = Sw.PageLayout({
            Type: "form",
            Title: _TF("Carrier account", "Title of the page with the parcel carrier account of the warehouse"),
            Description: _TF("The account the warehouse uses to print return labels.", "One line description under the title of the carrier account page"),
        });

        const account = new Sw.TextBox({
            Label: _TF("Account name", "Label of the field with the user name of the parcel carrier account"),
            AutoComplete: "username",
            SpellCheck: false,
        });
        const key = new Sw.TextBox({
            Label: _TF("API key", "Label of the field with the secret API key of the parcel carrier account"),
            Hint: _TF("Find it in the carrier's portal under Developer settings.", "Hint under the API key field of the carrier account"),
            Type: "password",
            Reveal: true,
            AutoComplete: "off",
            SpellCheck: false,
            Mono: true,
        });
        const save = new Sw.Button({
            Text: _TF("Save", "Button that saves the carrier account"),
            Title: _TF("Save the carrier account used for return labels", "Tool tip of the Save button of the carrier account page"),
            Kind: "primary",
        });

        Sw.Form({
            Fields: [
                { Name: "Account", Control: account, Required: _TF("Enter the account name.", "Error under the account name when it is empty") },
                { Name: "ApiKey", Control: key, Required: _TF("Enter the API key.", "Error under the API key when it is empty") },
            ],
            Submit: save,
            Title: _TF("the carrier account", "Inserted in the question when leaving the page: Your changes to <this> are lost."),
            OnSubmit: values => Sw.Net.Send("../Api/carriers/SaveAccount", values),
        });

        page.Add(Sw.Card({
            Title: _TF("Account", "Title of the card with the carrier account fields"),
            Body: [account, key, save],
        }));

        if (!Sw.Platform.IsTouch)
            account.Focus();
    });
})();
