/** R3 developer warning probe: one mistake per framework area; ?off=fn sets Sw.DevMode = () => false first. */
if (new URLSearchParams(location.search).get("off") === "fn") Sw.DevMode = () => false;
Sw.Page.Main(async () => {
    const W = window.W = { done: [] };
    const page = Sw.PageLayout({ Type: "form", Title: "Warnings probe", MenuTitle: false });
    const t = (n, f) => { try { f(); W.done.push(n); } catch (e) { W.done.push(n + " threw " + e.message); } };
    t("button no reason", () => { const b = new Sw.Button({ Text: "X", Disabled: true }); page.Add(b); });
    t("unknown icon", () => page.Add(Sw.Icon("no-such-icon-r3")));
    t("keys dup", () => { Sw.Keys.Add("Mod+K", () => 0, { Title: "A" }); Sw.Keys.Add("Mod+K", () => 0, { Title: "B" }); });
    t("keys reserved", () => Sw.Keys.Add("Mod+T", () => 0, { Title: "T" }));
    t("draft no context", () => { const tb = new Sw.TextBox({ Label: "Body" }); page.Add(tb); Sw.Draft.Attach(tb); });
    t("draft secret", () => { const tb = new Sw.TextBox({ Label: "API token" }); page.Add(tb); Sw.Draft.Attach(tb, { Context: "lab.w.tok" }); });
    t("progress NaN", () => { const j = Sw.Activity.Start({ Key: "lab.w.nan", Title: "W", Progress: NaN }); j.Remove(); });
    t("ready typo", () => Sw.Ready("Tabel", () => 0));
    t("dialog no title", () => { const h = Sw.Dialog.Open({ Content: "x", Buttons: [{ Text: "OK", Value: 1 }] }); setTimeout(() => h.Close(), 300); });
    t("dialog unknown key", () => { const h = Sw.Dialog.Open({ Title: "K", Content: "x", Defualt: 1, Buttons: [{ Text: "Close", Value: 1, Default: true }] }); setTimeout(() => h.Close(), 300); });
    t("dialog same id", () => { const a = Sw.Dialog.Open({ Title: "Same", Id: "lab.same", Content: "a" }); const b = Sw.Dialog.Open({ Title: "Same", Id: "lab.same", Content: "b" }); setTimeout(() => { a.Close(); b.Close(); }, 300); });
    t("learned dup", () => { Sw.Help.Learned("lab.w.dup", { Text: "a" }); Sw.Help.Learned("lab.w.dup", { Text: "b" }); });
    t("copy empty", () => { const c = Sw.CopyButton({}); page.Add(c); c.E ? c.E.click() : c.click && c.click(); });
    W.ready = true;
});
