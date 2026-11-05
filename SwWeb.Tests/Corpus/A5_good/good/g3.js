"use strict";
// Tenants: delete a tenant (type to confirm, prepared token), suspend with Undo, a refused action keeps its reason
Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "dash", Title: _TF("Tenants", "Page title"),
        Description: _TF("Customers that share this server", "Page description of the tenants") });
    document.body.append(page.E);
    const list = Sw.ListCard({ Title: _TF("Tenants", "Card title"), Count: true, Span: 12,
        Empty: { Icon: "users", Title: _TF("No tenants", "Empty state title"), Text: _TF("Tenants you add show up here.", "Empty state text") } });
    const tools = Sw.el("div.sw-row");
    page.Add(Sw.Grid({ Content: [list] }), tools);

    let tenants = [];
    let current = null;

    const suspend = new Sw.Button({
        Text: _TF("Suspend", "Button: suspend the selected tenant"),
        Title: _TF("Suspend the selected tenant: its users can't sign in until it is resumed", "Tool tip of Suspend"),
        Disabled: _TF("Select a tenant first", "Reason Suspend is disabled"),
        OnClick: async () => {
            const t = current;
            await Sw.Net.Send("../Api/tenants/Suspend", { Id: t.Id });
            Sw.Toast(_T("{0} was suspended", t.Name, "Toast after suspending a tenant"), {
                Kind: "ok",
                Action: { Text: _TF("Undo", "Toast action: resume the tenant again"), OnClick: () => Sw.Net.Send("../Api/tenants/Resume", { Id: t.Id }) },
            });
        },
    });

    const del = new Sw.Button({
        Text: _TF("Delete…", "Button: delete the selected tenant"), Kind: "danger",
        Title: _TF("Delete the selected tenant and all its data", "Tool tip of Delete"),
        Disabled: _TF("Select a tenant first", "Reason Delete is disabled"),
        OnClick: () => {
            const t = current;
            Sw.Danger.Run({
                Action: _TF("Delete", "Verb of the delete confirmation"),
                Target: t.Name,
                Consequence: _T("{0} users and all their files are deleted. This can't be undone.", t.Users, "Consequence of deleting a tenant"),
                Confirm: t.Name,
                Button: del,
                Prepare: async signal => {
                    const p = await Sw.Net.Send("../Api/tenants/PrepareDelete", { Id: t.Id }, { Signal: signal, ReadOnly: true });
                    if (!p || !p.Token)
                        throw new Error(_TF("The server gave no delete token.", "Error: no delete token"));
                    return { Token: p.Token };
                },
                Run: (signal, p) => Sw.Net.Send("../Api/tenants/Delete", { Token: p.Token }),
                Done: _T("{0} was deleted", t.Name, "Result of deleting a tenant"),
            });
        },
    });
    tools.append(suspend.E, del.E);

    function pick(t) {
        current = t;
        suspend.SetDisabled(!t, _TF("Select a tenant first", "Reason Suspend is disabled"));
        del.SetDisabled(!t, _TF("Select a tenant first", "Reason Delete is disabled"));
    }

    tenants = await Sw.Net.Read("../Api/tenants/GetTenants") || [];
    list.SetItems(tenants.map(t => ({ Key: t.Id, Text: t.Name, Meta: _T("{0} users", t.Users, "Number of users of a tenant"),
        Status: t.Suspended ? { Text: _TF("Suspended", "Tenant state"), Kind: "warning" } : { Text: _TF("Active", "Tenant state"), Kind: "ok" },
        OnClick: () => pick(t) })));
});
