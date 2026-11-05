/** Critic 4 round 2: 13 NEW misuse patterns; each M.n() sets up the pattern and returns what it measured. */
Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: "Misuse 2", Description: "New bad patterns, round 2." });
    const M = window.M = { warns: [], page };
    const ow = console.warn;
    console.warn = (...a) => { M.warns.push(a.join(" ").slice(0, 300)); ow.apply(console, a); };
    const wait = ms => new Promise(r => setTimeout(r, ms));
    const host = Sw.el("div");
    page.Add(host);
    const dirty = () => Sw.Page.DirtyTexts().map(d => d.Text + (d.Draft ? " [draft]" : ""));

    // N1: an "applies at once" Toggle whose OnChange is async (auto save), in a card
    M.n1 = async () => {
        const saves = [];
        const t = new Sw.Toggle({ Label: "Email me when a report fails", OnChange: async v => { await wait(300); saves.push(v); } });
        page.Add(Sw.Card({ Title: "Notifications", Body: t.E }));
        t.E.querySelector("[role=switch],input,button").click();
        await wait(100);
        const during = dirty();
        await wait(600);
        return { saves, during, after: dirty() };
    };
    // N1b: same, but the auto save fails
    M.n1b = async () => {
        const t = new Sw.Toggle({ Label: "Weekly digest", OnChange: async v => { await wait(200); throw new Error("Save failed (500)"); } });
        page.Add(Sw.Card({ Title: "Digest", Body: t.E }));
        t.E.querySelector("[role=switch],input,button").click();
        await wait(700);
        return { val: t.Get(), after: dirty(), toasts: [...document.querySelectorAll(".sw-toast")].map(x => x.innerText.slice(0, 120)) };
    };
    // N2: a filter TextBox (plain type) in a card that filters as you type (OnInput only)
    M.n2 = async () => {
        const tb = new Sw.TextBox({ Label: "Filter the reports", OnInput: () => {} });
        page.Add(Sw.Card({ Title: "Reports", Body: tb.E }));
        tb.Input.focus();
        tb.Input.value = "failed"; tb.Input.dispatchEvent(new Event("input", { bubbles: true }));
        tb.Input.dispatchEvent(new Event("change", { bubbles: true }));
        await wait(200);
        return { after: dirty() };
    };
    // N3: own SetDirty (as the R1 docs said) + the controls' own dirty in the same card
    M.n3 = async () => {
        const tb = new Sw.TextBox({ Label: "Subject", OnInput: () => Sw.Page.SetDirty("subj", "Your changes to the subject are lost.") });
        page.Add(Sw.Card({ Title: "Mail", Body: tb.E }));
        tb.Input.focus(); tb.Input.value = "Hi"; tb.Input.dispatchEvent(new Event("input", { bubbles: true }));
        await wait(200);
        return { after: dirty() };
    };
    // N4: a draft on a secret-looking text field (private key in a textarea, a token in a text input)
    M.n4 = async () => {
        const ta = Sw.el("textarea", { "aria-label": "Private key" });
        const inp = Sw.el("input", { type: "text", "aria-label": "API token", autocomplete: "off" });
        host.append(ta, inp);
        const d1 = Sw.Draft.Attach(ta, { Context: "lab.key" });
        const d2 = Sw.Draft.Attach(inp, { Context: "lab.token" });
        ta.value = "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC7"; ta.dispatchEvent(new Event("input", { bubbles: true }));
        inp.value = ["sk", "live", "51HxyzABCDEF0123456789abcdefGHIJ"].join("_"); inp.dispatchEvent(new Event("input", { bubbles: true }));
        await wait(1200);
        return { key: (await Sw.Draft.Get("lab.key"))?.Text?.slice(0, 30) || null, token: (await Sw.Draft.Get("lab.token"))?.Text || null, ls: Object.keys(localStorage).filter(k => /lab\.(key|token)/.test(k)).length };
    };
    // N5: two fields with the same draft Context
    M.n5 = async () => {
        const a = Sw.el("textarea", { "aria-label": "Reply A" }), b = Sw.el("textarea", { "aria-label": "Reply B" });
        host.append(a, b);
        Sw.Draft.Attach(a, { Context: "lab.same" }); Sw.Draft.Attach(b, { Context: "lab.same" });
        a.value = "Text for A"; a.dispatchEvent(new Event("input", { bubbles: true }));
        await wait(700);
        b.value = "Text for B"; b.dispatchEvent(new Event("input", { bubbles: true }));
        await wait(700);
        return { stored: (await Sw.Draft.Get("lab.same"))?.Text };
    };
    // N6: TextBox Draft option AND a manual Sw.Draft.Attach on its Input
    M.n6 = async () => {
        await Sw.Draft.Set("lab.double", "Earlier text");
        const tb = new Sw.TextBox({ Label: "Comment", Multiline: true, Draft: { Context: "lab.double" } });
        page.Add(Sw.Card({ Title: "Double", Body: tb.E }));
        const d = Sw.Draft.Attach(tb.Input, { Context: "lab.double" });
        await wait(800);
        return { val: tb.Get(), notices: tb.E.querySelectorAll(".sw-draft-notice").length, allNotices: document.querySelectorAll(".sw-draft-notice").length };
    };
    // N7: markup in author strings that may hold user data (reason, dirty text, dialog title, toast, confirm)
    M.n7 = async () => {
        window.__xss = 0;
        const x = "<img src=x onerror=\"window.__xss++\"><b>bold</b>";
        const b = new Sw.Button({ Text: "Act", Disabled: "Locked by " + x });
        host.append(b.E);
        b.E.click();
        Sw.Page.SetDirty("xss", "Your changes to " + x + " are lost.");
        const h = Sw.Dialog.Open({ Title: "Edit " + x, Content: "Body " + x, Buttons: [{ Text: "Close", Value: null }] });
        Sw.Toast("Saved " + x);
        await wait(600);
        const res = { xss: window.__xss, imgs: document.querySelectorAll("img[src='x']").length, bold: document.querySelectorAll("b").length };
        h.Close(null);
        Sw.Page.SetDirty("xss", null);
        return res;
    };
    // N8: async OnClick that throws synchronously / rejects with a string / returns false
    M.n8 = async () => {
        const out = {};
        const mk = (name, fn) => { const b = new Sw.Button({ Text: name, OnClick: fn }); host.append(b.E); return b; };
        const b1 = mk("Sync throw", () => { throw new Error("boom sync"); });
        const b2 = mk("Reject string", async () => { await wait(100); throw "plain string"; });
        const b3 = mk("Return false", async () => { await wait(100); return false; });
        for (const [k, b] of [["sync", b1], ["str", b2], ["false", b3]]) {
            b.E.click();
            await wait(900);
            out[k] = { busy: b.E.getAttribute("aria-busy"), toasts: [...document.querySelectorAll(".sw-toast")].map(t => t.innerText.replace(/\s+/g, " ").slice(0, 100)), live: [...document.querySelectorAll(".sw-page-live")].map(e => e.textContent) };
        }
        return out;
    };
    // N9: re-entrancy: Close twice, Close from OnClose, open a new dialog from OnClose
    M.n9 = async () => {
        const log = [];
        const h = Sw.Dialog.Open({ Title: "Re-entrant", Content: "x", Buttons: [{ Text: "Close", Value: null }] });
        const of = Sw.Dialog.Of(h.Body || h.E || document.querySelector(".sw-dialog"));
        of && of.OnClose(r => { log.push("onclose " + JSON.stringify(r)); h.Close("again"); });
        h.Close(1); h.Close(2);
        let v;
        try { v = await h; } catch (e) { v = "threw " + e; }
        await wait(500);
        const h2 = Sw.Dialog.Open({ Title: "Opens next", Content: "y", Buttons: [{ Text: "Close", Value: null }] });
        const of2 = Sw.Dialog.Of(document.querySelector(".sw-dialog"));
        of2 && of2.OnClose(() => { M.next = Sw.Dialog.Open({ Title: "Second", Content: "z", Buttons: [{ Text: "Close", Value: null }] }); });
        h2.Close(null);
        await wait(600);
        const st = { log, v, open: [...document.querySelectorAll(".sw-dialog h2")].map(e => e.textContent), inert: document.querySelectorAll("[inert]").length, focusInDialog: !!document.activeElement.closest(".sw-dialog") };
        M.next && M.next.Close(null);
        await wait(400);
        st.afterInert = document.querySelectorAll("[inert]").length;
        return st;
    };
    // N10: activity job lifecycle misuse
    M.n10 = async () => {
        const out = {};
        const j = Sw.Activity.Start({ Title: "Job A" });
        j.Done({ Text: "A done" }); j.Done({ Text: "A done twice" }); j.Update({ Progress: 0.5, Text: "after done" }); j.Fail(new Error("fail after done"));
        out.a = Sw.Activity.List ? Sw.Activity.List().filter(x => /Job A/.test(x.Title)).map(x => x.State + ":" + (x.Text || "")) : "no List";
        const k = Sw.Activity.Start({});
        out.noTitle = k && (k.Title || "(none)");
        const f = Sw.Activity.Start({ Title: "Job F" });
        f.Fail("Token sk_live_51HxyzABCDEF0123456789 rejected");
        out.failStr = Sw.Activity.List ? Sw.Activity.List().filter(x => /Job F/.test(x.Title)).map(x => x.State + ":" + (x.Error || x.Text || "")) : null;
        return out;
    };
    // N11: duplicate / reserved shortcuts
    M.n11 = async () => {
        const hits = [];
        Sw.Keys.Add("Mod+S", () => hits.push("first"), { Title: "Save A", InInputs: true });
        Sw.Keys.Add("Mod+S", () => hits.push("second"), { Title: "Save B", InInputs: true });
        let r;
        try { r = Sw.Keys.Add("Mod+W", () => hits.push("w"), { Title: "Close tab?" }); } catch (e) { r = "threw " + e.message; }
        M.hits = hits;
        return { list: (Sw.Keys.List ? Sw.Keys.List() : []).map(k => (k.Combo || k.combo || "") + ":" + (k.Title || "")).filter(x => /S:|W:/.test(x)), r: typeof r };
    };
    // N13: a helper footer button (Copy log) in a busy dialog
    M.n13 = async () => {
        const clicks = [];
        const h = Sw.Dialog.Open({ Title: "Upload", Content: "Uploading", Buttons: [{ Text: "Copy log", OnClick: () => { clicks.push("copy"); return undefined; }, Close: false }, { Text: "Cancel", Value: null }] });
        h.SetBusy(new Promise(() => {}), { Text: "Uploading report.xlsx" });
        await wait(400);
        const d = [...document.querySelectorAll(".sw-dialog")].pop();
        const cb = [...d.querySelectorAll("button")].find(b => b.textContent.trim() === "Copy log");
        const st = { ad: cb && cb.getAttribute("aria-disabled"), title: cb && cb.title };
        cb && cb.click();
        await wait(300);
        st.clicks = clicks.slice();
        h.Close(null);
        return st;
    };
    M.ready = true;
});
