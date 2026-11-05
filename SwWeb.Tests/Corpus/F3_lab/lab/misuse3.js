/** R3 misuse lab: 13 NEW wrong-but-plausible usages of the round 2 APIs (each M.nX() sets up one, the steps file drives it). */
Sw.Page.Main(async () => {
    const delay = (ms, signal) => new Promise((res, rej) => {
        const t = setTimeout(res, ms);
        if (signal) signal.addEventListener("abort", () => { clearTimeout(t); const e = new Error("aborted"); e.name = "AbortError"; rej(e); });
    });
    const M = window.M = { log: [] };
    const page = Sw.PageLayout({ Type: "form", Title: "Misuse 3", MenuTitle: false, Actions: [new Sw.Button({ Text: "Save all", Kind: "primary", OnClick: async () => true })] });
    const area = Sw.el("div.sw-stack");
    page.Add(Sw.Card({ Title: "Lab", Body: area }));
    M.area = area;
    const fake = Sw.Url.Root + "Api/labFake/";
    // N1 a draft on a contenteditable rich text editor
    M.n1 = () => { const ed = Sw.el("div", { attrs: { contenteditable: "true", role: "textbox", "aria-label": "Comment", id: "n1" }, style: { minHeight: "60px", border: "1px solid" } }); area.append(ed); M.n1h = Sw.Draft.Attach(ed, { Context: "lab.m3.comment" }); return { active: M.n1h.Active }; };
    // N2 Report called twice for one outcome (copy / paste of the README snippet into two branches)
    M.n2 = async () => { const b = new Sw.Button({ Text: "Sync" }); area.append(b.E); const sc = Sw.Work.Scope(signal => Promise.reject(new Error("Sync server unreachable")));
        const said = Sw.Work.Watch(); const res = await Sw.Work.Busy(b.E, Sw.Work.Promise(sc), { Name: "Sync" }); const out = Sw.Work.Outcome(sc, res);
        Sw.Work.Report(out, { Name: "Sync", Said: said.Stop(), Spun: res.Spun }); Sw.Work.Report(out, { Name: "Sync", Spun: res.Spun }); return out; };
    // N3 Focus.Keep on a container that the rebuild replaces (parent.innerHTML / replaceWith)
    M.n3 = async () => { const host = Sw.el("div#n3host"); area.append(host);
        const build = () => { const ul = Sw.el("ul#n3list", null, ["a", "b", "c"].map(x => Sw.el("li", null, new Sw.Button({ Text: "Item " + x, Small: true })))); host.replaceChildren(ul); return ul; };
        let ul = build(); ul.querySelectorAll("button")[1].focus();
        const k = Sw.Focus.Keep(ul); ul = build(); k.Restore();
        await delay(100); const a = document.activeElement; return { active: a.tagName + " " + (a.innerText || a.id || "").trim() }; };
    // N4 Activity.Run whose work ignores the signal (the README calls Run "the safe form: Cancel always works")
    M.n4 = () => { M.n4saved = false; const j = Sw.Activity.Run({ Key: "lab.m3.run", Title: "Publishing catalogue", Kind: "server" }, async (signal, job) => { for (let i = 1; i <= 6; ++i) { await delay(500); job.Update({ Progress: i / 6 }); } M.n4saved = true; M.log.push("n4 published"); return "ok"; }); M.n4j = j; return { state: j.State }; };
    // N5 OnClick returns a job of Sw.Activity.Start (not Run / Track) and does the work in the background
    M.n5 = () => { const b = new Sw.Button({ Text: "Rebuild index", OnClick: () => { const j = Sw.Activity.Start({ Key: "lab.m3.start", Title: "Rebuilding the index" }); (async () => { await delay(2500); j.Fail(new Error("Index locked")); })(); return j; } }); area.append(b.E); M.n5b = b; return 1; };
    // N6 footer OnClick that calls close() itself and then fails
    M.n6 = () => { M.n6h = Sw.Dialog.Open({ Title: "Rename report", Content: "x", Buttons: [{ Text: "Cancel", Value: null }, { Text: "Rename", Kind: "primary", Value: 1, OnClick: async close => { close(1); await delay(800); throw new Error("Name already used"); } }] }); return 1; };
    // N7 the same shortcut spelled twice ("Ctrl+S" and "Mod+S")
    M.n7 = () => { M.n7r = []; Sw.Keys.Add("Ctrl+S", () => M.n7r.push("A"), { Title: "Save A", InInputs: true }); Sw.Keys.Add("Mod+S", () => M.n7r.push("B"), { Title: "Save B", InInputs: true }); return Sw.Keys.List().filter(k => /Save/.test(k.Title || "")).map(k => k.Title + " " + k.Combo); };
    // N8 TextBox({ Draft }) inside a Sw.Form card (the form and the draft both track it)
    M.n8 = () => { const tb = new Sw.TextBox({ Label: "Message", Multiline: true, Draft: "lab.m3.msg" }); const sub = new Sw.Button({ Text: "Send", Kind: "primary" });
        const card = Sw.Card({ Title: "Contact", Body: [tb.E, sub.E] }); area.append(card.E || card); M.n8f = Sw.Form({ Fields: [{ Name: "Msg", Control: tb, Label: "Message", Required: true }], Submit: sub, OnSubmit: v => true }); M.n8tb = tb; return 1; };
    // N10 StartServer on a job url that does not exist / answers 404
    M.n10 = async () => { try { const j = await Sw.Activity.StartServer(fake + "StartNoSuchJob", { X: 1 }, { DoneText: "ok" }); return { job: j && (j.State || typeof j), title: j && j.Title }; } catch (e) { return { threw: String(e && e.message || e).slice(0, 120) }; } };
    // N11 the real save marked Detached (copied from a "background refresh" example)
    M.n11 = () => { const tb = new Sw.TextBox({ Label: "Display name", Value: "Ann" }); const b = new Sw.Button({ Text: "Save name", OnClick: () => { Sw.Net.Send(fake + "SaveName", { Name: tb.Get() }, { Detached: true }).then(() => M.log.push("n11 saved")); } });
        const c = Sw.Card({ Title: "Profile", Body: [tb.E, b.E] }); area.append(c.E || c); M.n11tb = tb; M.n11b = b; return 1; };
    // N12 the request starts in a setTimeout (debounced save) and fails
    M.n12 = () => { const b = new Sw.Button({ Text: "Save later", OnClick: () => { setTimeout(() => Sw.Net.Send(fake + "SaveLater", { A: 1 }), 300); } }); area.append(b.E); M.n12b = b; return 1; };
    // N13 Cancellable: true on a footer write (the author wants the Cancel button back)
    M.n13 = () => { M.n13h = Sw.Dialog.Open({ Title: "Transfer funds", Content: "Move 100 to savings", Buttons: [{ Text: "Cancel", Value: null }, { Text: "Transfer", Kind: "primary", Value: 1, Cancellable: true, OnClick: (c, v, { Signal }) => Sw.Net.Send(fake + "Transfer", { Amount: 100 }, { Signal }) }] }); return 1; };
    M.n10b = () => { const b = new Sw.Button({ Text: "Start export job", OnClick: () => Sw.Activity.StartServer(fake + "StartNoSuchJob", { X: 1 }) }); area.append(b.E); M.n10bb = b; return 1; };
    // N9 = rapid auto save toggles (r3.html)
    M.ready = true;
});
