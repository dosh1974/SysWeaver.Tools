/** R4 misuse lab: 13 NEW wrong-but-plausible usages of the round 3 APIs (Sw.Cancel, Sw.Sleep, Read / AbortSafe, Live, Keys Scope, Draft.Clear, EditWhileBusy ...). */
Sw.Page.Main(async () => {
    const delay = ms => new Promise(r => setTimeout(r, ms));
    const M = window.M = { log: [] };
    const page = Sw.PageLayout({ Type: "form", Title: "Misuse 4", MenuTitle: false, Actions: [new Sw.Button({ Text: "Save all", Kind: "primary", OnClick: async () => true })] });
    const area = Sw.el("div.sw-stack");
    page.Add(Sw.Card({ Title: "Lab", Body: area }));
    const fake = Sw.Url.Root + "Api/labFake/";
    // P1 change the generated id of a TextBox input (to select it in tests / CSS): the <label for> no longer points at it
    M.p1 = () => { const tb = new Sw.TextBox({ Label: "Password" }); tb.Input.id = "p1pw"; area.append(tb.E); const h = Sw.Draft.Attach(tb, { Context: "lab.m4.p1" });
        return { labels: tb.Input.labels.length, active: h.Active }; };
    // P2 Sw.Sleep in a Run loop, but the AbortError is swallowed (try / catch around each step: "make it robust")
    M.p2 = () => { M.p2n = 0; return M.p2j = Sw.Activity.Run({ Key: "lab.m4.p2", Title: "Re-indexing" }, async (signal, job) => {
        for (let i = 1; i <= 8; ++i) { try { await Sw.Sleep(400, signal); } catch (e) { } M.p2n = i; job.Update({ Progress: i / 8 }); } return "ok"; }); };
    // P3 { Read: true } on a write in a dialog footer (to get a real Cancel)
    M.p3 = () => Sw.Dialog.Open({ Title: "Transfer funds", Content: "100 EUR to ACME", Buttons: [{ Text: "Cancel", Value: null },
        { Text: "Transfer", Kind: "primary", Value: 1, OnClick: (c, v, { Signal }) => Sw.Net.Send(fake + "Transfer", { Amount: 100 }, { Signal, Read: true }) }] });
    // P4 draft cleared before the save is known to have worked
    M.p4 = () => { const tb = new Sw.TextBox({ Label: "Reply", Multiline: true }); const d = Sw.Draft.Attach(tb, { Context: "lab.m4.p4" });
        const b = new Sw.Button({ Text: "Send reply", Kind: "primary", OnClick: () => { d.Clear(); return Sw.Net.Send(fake + "SendReply", { Text: tb.Get() }); } });
        area.append(tb.E, b.E); M.p4tb = tb; M.p4b = b; return 1; };
    // P5 two different works started with one Key (copy / paste of the Run snippet, Key "report")
    M.p5 = async () => { M.p5log = []; const a = Sw.Activity.Run({ Key: "lab.m4.report", Title: "Weekly sales" }, async s => { await Sw.Sleep(1200, s); M.p5log.push("weekly"); });
        await delay(200); const b = Sw.Activity.Run({ Key: "lab.m4.report", Title: "Stock levels" }, async s => { await Sw.Sleep(1200, s); M.p5log.push("stock"); });
        return { same: a === b, bNew: b.IsNew, titles: Sw.Activity.List().filter(j => j.Key === "lab.m4.report").map(j => j.Title) }; };
    // P6 a scoped shortcut whose Scope element is never put in the document
    M.p6 = () => { M.p6ran = 0; const el = Sw.el("div"); Sw.Keys.Add("Mod+J", () => { M.p6ran++; }, { Title: "Jump", Scope: el }); return Sw.Keys.List().filter(k => /J/.test(k.Combo || k.Key || "")).length; };
    // P7 auto-save that never answers (a lost promise)
    M.p7 = () => { M.p7tg = new Sw.Toggle({ Label: "Never answers", OnChange: v => new Promise(() => { }) }); area.append(M.p7tg.E); return 1; };
    // P8 the handler announces its own success too
    M.p8 = () => { M.p8b = new Sw.Button({ Text: "Publish", OnClick: async () => { await delay(300); Sw.Page.Announce("Published"); Sw.Info("Published"); } }); area.append(M.p8b.E); return 1; };
    // P9 the page closes a busy dialog from code (a timer / a push "record changed")
    M.p9 = () => { const inp = Sw.el("input", { attrs: { "aria-label": "Name", value: "Initech" } });
        M.p9saved = 0; M.p9p = Sw.Dialog.Open({ Title: "Edit customer", Content: inp, Buttons: [{ Text: "Cancel", Value: null }, { Text: "Save", Kind: "primary", Value: 1, OnClick: async () => { await delay(2500); M.p9saved = 1; } }] });
        M.p9inp = inp; return 1; };
    // P10 catch, Sw.Fail, then rethrow (log it AND show it)
    M.p10 = () => { M.p10b = new Sw.Button({ Text: "Import", OnClick: async () => { try { await delay(200); throw new Error("File is not a CSV file"); } catch (e) { Sw.Fail(e); throw e; } } }); area.append(M.p10b.E); return 1; };
    // P11 the new Sw.Net.Live used for the real save
    M.p11 = () => { const tb = new Sw.TextBox({ Label: "City" }); const card = Sw.Card({ Title: "Address", Body: [tb.E], Actions: [M.p11b = new Sw.Button({ Text: "Save address", Kind: "primary", OnClick: () => Sw.Net.Live(fake + "SaveAddress", { City: tb.Get() }) })] });
        area.append(card.E); M.p11tb = tb; return 1; };
    // P12 EditWhileBusy + the work closes the dialog itself
    M.p12 = () => { const inp = Sw.el("input", { attrs: { "aria-label": "Title", value: "Q3 plan" } });
        M.p12p = Sw.Dialog.Open({ Title: "Rename plan", EditWhileBusy: true, Content: inp, Buttons: [{ Text: "Cancel", Value: null }, { Text: "Rename", Kind: "primary", Value: 1, OnClick: async close => { const sent = inp.value; await delay(1500); M.p12sent = sent; close(1); } }] });
        M.p12inp = inp; return 1; };
    // P13 progress in percent instead of 0..1
    M.p13 = () => { const j = Sw.Activity.Start({ Key: "lab.m4.p13", Title: "Uploading photos" }); j.Update({ Progress: 40, Text: "40 of 100" }); M.p13j = j; return { p: j.Progress }; };
    M.ready = true;
});
