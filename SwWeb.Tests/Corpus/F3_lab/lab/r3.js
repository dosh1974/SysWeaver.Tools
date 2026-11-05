/** R3 retest page: honest Cancel and failing auto saves (built from ui/README "Dirty, busy and close attempts", "Unsaved edits"). */
Sw.Page.Main(async () => {
    const delay = (ms, signal) => new Promise((res, rej) => {
        const t = setTimeout(res, ms);
        if (signal) signal.addEventListener("abort", () => { clearTimeout(t); const e = new Error("aborted"); e.name = "AbortError"; rej(e); });
    });
    const R = window.R = { log: [] };
    const page = Sw.PageLayout({ Type: "form", Title: "Retest R3", MenuTitle: false });
    // ---- auto saving controls (no Save button in their card on purpose: they save on change)
    const tg = new Sw.Toggle({ Label: "Weekly digest", OnChange: async v => { await delay(600); R.log.push("tg " + v); throw new Error("Save failed (500)"); } });
    const sel = new Sw.Select({ Label: "Language", Value: "en", Items: [{ Value: "en", Text: "English" }, { Value: "sv", Text: "Swedish" }, { Value: "de", Text: "German" }],
        OnChange: v => Promise.reject("Not allowed by policy") });
    const nb = new Sw.NumberBox({ Label: "Rows per page", Value: 25, Min: 1, Max: 500, OnChange: async v => { await delay(300); return false; } });
    const tgNet = new Sw.Toggle({ Label: "Send reports", OnChange: v => Sw.Net.Send(Sw.Url.Root + "Api/labFake/SetReports", { On: v }) });
    let server = false;
    const tgFast = new Sw.Toggle({ Label: "Rapid", OnChange: async v => { const ms = v ? 900 : 200; await delay(ms); server = v; R.log.push("rapid saved " + v + " after " + ms); } });
    R.c = { tg, sel, nb, tgNet, tgFast }; R.server = () => server;
    page.Add(Sw.Card({ Title: "Settings", Body: [tg.E, sel.E, nb.E, tgNet.E, tgFast.E] }));
    // ---- dialogs
    R.dlg = (kind, o) => {
        o = o || {};
        const name = new Sw.TextBox({ Label: "Customer name", Value: "Initech" });
        const save = {
            ignore: async () => { await delay(5000); R.log.push("ignore saved"); return true; },
            signal: async (close, value, { Signal }) => { await delay(5000, Signal); R.log.push("signal saved"); return true; },
            send: (close, value, { Signal }) => Sw.Net.Send(Sw.Url.Root + "Api/labFake/SaveCustomer", { Name: name.Get() }, { Signal }).then(r => { R.log.push("send answered " + JSON.stringify(r)); return true; }),
            lie: (close, value, { Signal }) => Sw.Net.Send(Sw.Url.Root + "Api/labFake/SaveCustomer", { Name: name.Get() }, { Signal }).then(r => { R.log.push("lie answered"); return true; }),
            fail: async () => { await delay(3000); throw new Error("Customer locked by another user"); },
        }[kind];
        const btn = { Text: "Save", Kind: "primary", Value: 1, OnClick: save };
        if (kind === "lie") btn.Cancellable = true;
        const h = Sw.Dialog.Open({ Title: "Edit customer " + kind, Id: "r3." + kind, Content: Sw.el("div.sw-stack", null, name.E), Buttons: [{ Text: "Cancel", Value: null }, btn] });
        h.OnClose && 0;
        R.h = h; R.name = name;
        return h;
    };
    R.foot = () => [...document.querySelectorAll(".sw-dialog button")].map(b => b.innerText.trim() + (b.getAttribute("aria-disabled") === "true" ? "[dis]" : "")).filter(Boolean);
    R.dialogs = () => [...document.querySelectorAll(".sw-dialog")].filter(d => d.isConnected && getComputedStyle(d).display !== "none").map(d => (d.querySelector("h1,h2,.sw-dialog-title") || {}).innerText);
    R.click = re => { const b = [...document.querySelectorAll(".sw-dialog button")].reverse().find(b => re.test(b.innerText)); if (!b) return "no " + re; b.click(); return b.innerText; };
    R.ready = true;
});
