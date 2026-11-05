/**
 * @file good/g1.js - billing details form (correct use of Sw.Form).
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Billing details", "Page title"), Description: _TF("Where invoices go and which plan you pay for.", "Page description") });
    document.body.append(page.E);

    const name = new Sw.TextBox({ Label: _TF("Company name", "Field label on the billing page"), AutoComplete: "organization" });
    const mail = new Sw.TextBox({ Label: _TF("Billing e-mail", "Field label on the billing page"), Type: "email", AutoComplete: "email", Hint: _TF("Invoices are sent to this address.", "Hint under the billing e-mail field") });
    const plan = new Sw.Select({
        Label: _TF("Plan", "Field label on the billing page"),
        Items: [
            { Value: "", Text: _TF("Choose a plan", "Placeholder item of the plan list") },
            { Value: "team", Text: _TF("Team", "Plan name") },
            { Value: "business", Text: _TF("Business", "Plan name") },
        ],
    });
    const save = new Sw.Button({ Text: _TF("Save", "Button that saves the billing details"), Title: _TF("Save the billing details of your company", "Tool tip of the Save button"), Kind: "primary" });

    const form = Sw.Form({
        Fields: [
            { Name: "Name", Control: name, Required: true },
            { Name: "Mail", Control: mail, Required: _TF("Enter a billing e-mail.", "Error under the billing e-mail field"), Validate: v => Sw.Format.IsValidEmail(v) ? null : _TF("Enter an e-mail like name@example.com.", "Error under the billing e-mail field") },
            { Name: "Plan", Control: plan, Required: _TF("Choose a plan.", "Error under the plan field") },
        ],
        Submit: save,
        Title: _TF("the billing details", "Used in: Leave without saving? Your changes to the billing details are lost."),
        OnSubmit: v => Sw.Net.Send("../Api/billing/Save", v),
    });

    page.Add(Sw.Card({ Title: _TF("Company", "Card title on the billing page"), Body: [name, mail, plan, save] }));

    const cur = await Sw.Net.Get("../Api/billing/GetDetails");
    if (cur) {
        name.Set(cur.Name || "");
        mail.Set(cur.Mail || "");
        plan.Set(cur.Plan || "");
    }
    form.Validate(false);
});
