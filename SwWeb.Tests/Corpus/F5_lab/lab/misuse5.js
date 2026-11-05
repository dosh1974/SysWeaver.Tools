/** R5 misuse lab: 13 NEW wrong-but-plausible usages of the round 4 APIs (ReadOnly / AbortSafe, Sw.Cancel.Controller, Message string,
 *  Locked, user-vs-code edits, Sw.Overlay, Labels, Sw.Work.Current, job.Remove, option checks, RunServer Quiet). */
Sw.Page.Main(async () => {
    const delay = ms => new Promise(r => setTimeout(r, ms));
    const M = window.M = { log: [] };
    const page = Sw.PageLayout({ Type: "form", Title: "Misuse 5", MenuTitle: false });
    const area = Sw.el("div.sw-stack");
    page.Add(Sw.Card({ Title: "Lab", Body: area }));
    const fake = Sw.Url.Root + "Api/labFake/";
    // Q1 a write marked AbortSafe "so Cancel works" (the server does NOT undo it), in Run, user cancels
    M.q1 = () => Sw.Activity.Run({ Key: "lab.m5.q1", Title: "Import 500 contacts" }, signal => Sw.Net.Send(fake + "ImportContacts", { N: 500 }, { Signal: signal, AbortSafe: true }));
    // Q2 own plain AbortController + own Cancel button for a write (an upload)
    M.q2 = () => { const ac = new AbortController(); M.q2ac = ac; const p = Sw.Net.Send(fake + "UploadPhoto", { Name: "a.jpg" }, { Signal: ac.signal }).then(() => "ok", e => "err " + e.name); M.q2p = p; return 1; };
    // Q3 stale code: Sw.Net.Message(...).Text (it is a string now)
    M.q3 = () => { const st = Sw.el("p"); area.append(st); try { throw new Error("Customer locked by another user"); } catch (e) { st.textContent = Sw.Net.Message("Save customer", e).Text; } return st.textContent; };
    // Q4 page locks a field during its own save and the save throws: never unlocked
    M.q4 = () => { const tb = new Sw.TextBox({ Label: "Company name", Value: "Initech" }); M.q4tb = tb;
        const b = new Sw.Button({ Text: "Save company", Kind: "primary", OnClick: async () => { tb.Locked(true); await Sw.Net.Send(fake + "SaveCompany", { Name: tb.Get() }); tb.Locked(false); } });
        area.append(Sw.Card({ Title: "Company", Body: [tb.E], Actions: [b] }).E); M.q4b = b; return 1; };
    // Q5 a user button that copies values by code ("Same as billing"): the copied shipping address is not an edit
    M.q5 = () => { const bill = new Sw.TextBox({ Label: "Billing address", Value: "Main St 1" }); const ship = new Sw.TextBox({ Label: "Shipping address" });
        const same = new Sw.Button({ Text: "Same as billing", OnClick: () => { ship.Set(bill.Get()); } });
        const save = new Sw.Button({ Text: "Save addresses", Kind: "primary", OnClick: () => Sw.Net.Send(fake + "SaveAddr", { Bill: bill.Get(), Ship: ship.Get() }) });
        area.append(Sw.Card({ Title: "Addresses", Body: [bill.E, ship.E, same.E], Actions: [save] }).E); M.q5same = same; M.q5ship = ship; return 1; };
    // Q6 own fixed "bulk actions" bar at the bottom, not registered
    M.q6 = () => { for (let i = 0; i < 6; i++) area.append(Sw.el("p", { text: "Filler " + i + " lorem ipsum dolor sit amet." })); const last = new Sw.Button({ Text: "Approve all", Kind: "primary", OnClick: async () => 1 }); area.append(last.E); M.q6last = last;
        const bar = Sw.el("div", { attrs: { style: "position:fixed;left:0;right:0;bottom:0;height:72px;background:var(--sw-surface,#fff);border-top:1px solid #ccc;z-index:50;display:flex;align-items:center;padding:0 16px" } }, ["3 selected"]); document.body.append(bar); M.q6bar = bar; return 1; };
    // Q7 a dialog whose footer label is changed by code (no Labels declared)
    M.q7 = () => { let b; const h = Sw.Dialog.Open({ Title: "Send invoice", Content: "Send invoice 2026-0042 to ACME?", Buttons: [{ Text: "Cancel", Value: null }, { Text: "Send", Kind: "primary", Value: 1 }] }); M.q7h = h;
        setTimeout(() => { const btn = [...document.querySelectorAll(".sw-dialog button")].find(x => /^Send$/.test(x.innerText.trim())); if (btn) { const l = btn.querySelector(".sw-button-text") || btn; l.textContent = "Send now and notify the customer by e-mail"; } }, 600); return 1; };
    // Q8 the work awaits Sw.Work.Current of its own button (wants "the result of the save")
    M.q8 = () => { const b = new Sw.Button({ Text: "Publish", Kind: "primary", OnClick: async () => { await Sw.Net.Send(fake + "Publish", {}); const ok = await Sw.Work.Current(b.E); M.q8ok = ok; } }); area.append(b.E); M.q8b = b; return 1; };
    // Q9 job.Remove() right after Run "to keep the list clean"
    M.q9 = () => { M.q9n = 0; const j = Sw.Activity.Run({ Key: "lab.m5.q9", Title: "Recalculate totals" }, async s => { for (let i = 0; i < 4; i++) { await Sw.Sleep(300, s); M.q9n++; } return "ok"; }); M.q9j = j; j.Remove && j.Remove(); return Sw.Activity.List().filter(x => x.Key === "lab.m5.q9").length; };
    // Q10 search as you type, latest wins with Sw.Cancel.Controller, Send not marked ReadOnly
    M.q10 = () => { M.q10shown = []; let ctl = null; const out = Sw.el("p.q10out"); area.append(out);
        M.q10type = async q => { if (ctl) ctl.abort(); ctl = Sw.Cancel.Controller(); const sig = ctl.signal;
            try { const r = await Sw.Net.Send(fake + (q.length < 2 ? "SlowSearch" : "FastSearch"), { Q: q }, { Signal: sig }); out.textContent = "Results for " + q + ": " + JSON.stringify(r); M.q10shown.push(q); } catch (e) { M.q10shown.push("abort " + q + " " + e.name); } };
        return 1; };
    // Q11 option typos on dialog / card / page APIs
    M.q11 = () => { Sw.Card({ Titel: "Typo card", Body: [] }); const h = Sw.Dialog.Open({ Tittle: "Typo dialog", Content: "x", Buttons: [{ Text: "Close", Value: null }, { Text: "Find", Kind: "primary", Cancelable: true, OnClick: (c, v, { Signal }) => Sw.Sleep(300, Signal) }] });
        setTimeout(() => { const d = [...document.querySelectorAll(".sw-dialog")].pop(); const x = d && [...d.querySelectorAll("button")].find(b => /Close/.test(b.innerText)); x && x.click(); }, 500); return 1; };
    // Q12 Sw.Net.Message with the arguments swapped
    M.q12 = () => { const e = new Error("Name already used"); return Sw.Net.Message(e, "Rename"); };
    // Q13 RunServer with Quiet: true (to "avoid double toasts") and no own handling of a refused start
    M.q13 = () => { const b = new Sw.Button({ Text: "Export report", OnClick: () => Sw.Activity.RunServer(fake + "StartReport", {}, { Title: "Report", Quiet: true }) }); area.append(b.E); M.q13b = b; return 1; };
});
