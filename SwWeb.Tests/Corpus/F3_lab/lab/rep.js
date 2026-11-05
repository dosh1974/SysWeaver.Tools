/** @file R3 docs-only page "Report center": Sw.Work.Outcome / Report, Sw.Focus.Keep, Sw.Activity (Run + StartServer), Sw.Draft. Written from ui/README and core/README only. */
Sw.Page.Main(async () => {
    const P = window.P = { log: [] };
    const q = new URLSearchParams(location.search);
    const page = Sw.PageLayout({ Type: "list", Title: _T("Report center", "Page title"), MenuTitle: false });
    // 1) Refresh: own async handler with the one result path (ui/README "One result path")
    const status = Sw.el("p.sw-text-muted", { text: _T("Not loaded yet", "Status") });
    const refresh = Sw.el("button.sw-button", { text: _T("Refresh statistics", "Button"), type: "button", id: "refresh" });
    refresh.addEventListener("click", async ev => {
        if (Sw.ModifierClick(ev)) return;
        const sc = Sw.Work.Scope(signal => Sw.Net.Send(Sw.Url.Root + (q.get("bad") ? "Api/labFake/StatsTable" : "Api/debug/StatsTable"), null, { Signal: signal }));
        const said = Sw.Work.Watch();
        const res = await Sw.Work.Busy(refresh, Sw.Work.Promise(sc), { Name: _T("Refresh statistics", "Name") });
        const out = Sw.Work.Outcome(sc, res);
        P.out = out; P.res = res;
        if (out.Ok) status.textContent = _T("Loaded {0} rows", (res && res.Value && res.Value.Rows ? res.Value.Rows.length : "?"), "Status");
        Sw.Work.Report(out, { Name: _T("Refresh statistics", "Name"), Said: said.Stop(), Spun: res.Spun });
    });
    page.Add(Sw.Card({ Title: _T("Statistics", "Card"), Body: [status, refresh] }));
    // 2) Saved reports: a list rebuilt after Archive (Sw.Focus.Keep(container, work))
    let reports = [{ Id: 1, Name: "Weekly sales" }, { Id: 2, Name: "Open invoices" }, { Id: 3, Name: "Stock levels" }];
    const list = Sw.el("ul.lab-reports", { attrs: { "aria-label": _T("Saved reports", "List") } });
    function render() {
        list.replaceChildren(...reports.map(r => Sw.el("li", { dataset: { swItem: String(r.Id) } }, Sw.el("span", { text: r.Name }),
            new Sw.Button({ Text: _T("Run", "Button"), Small: true, AriaLabel: _T("Run {0}", r.Name, "Button"), OnClick: () => runReport(r) }),
            new Sw.Button({ Text: _T("Archive", "Button"), Small: true, AriaLabel: _T("Archive {0}", r.Name, "Button"),
                OnClick: () => Sw.Focus.Keep(list, async () => { await Sw.Util.Delay(600); reports = reports.filter(x => x !== r); render(); }) }))));
    }
    render();
    page.Add(Sw.Card({ Title: _T("Saved reports", "Card"), Body: list }));
    // 3) Long work: Sw.Activity.Run (thenable: the button stays busy, Cancel aborts the signal)
    function runReport(r) {
        return Sw.Activity.Run({ Key: "lab.rep." + r.Id, Title: _T("Running {0}", r.Name, "Job"), Kind: "local" }, async (signal, job) => {
            for (let i = 1; i <= 5; ++i) { await Sw.Util.DelayAbort(500, signal); job.Update({ Progress: i / 5 }); }
            P.log.push("ran " + r.Name);
            return { Text: _T("{0} is ready", r.Name, "Job") };
        });
    }
    P.runReport = runReport;
    // 4) Server job: export the statistics table (core/README "Server jobs")
    const exp = new Sw.Button({ Text: _T("Export statistics (server job)", "Button"), Icon: "download",
        OnClick: () => Sw.Activity.StartServer(Sw.Url.Root + "Api/StartExportTableApi",
            { Api: Sw.Url.Root + "Api/debug/StatsTable", Req: { Cc: 0, Row: 0 }, ExportAs: q.get("as") || "csv", Options: { Filename: "lab-stats" }, Key: "lab.rep.export", Title: _T("Exporting statistics", "Job") },
            { DoneText: _T("The statistics file is ready", "Job") }).then(j => (P.job = j, j)) });
    page.Add(Sw.Card({ Title: _T("Export", "Card"), Body: exp }));
    P.exp = exp;
    // 5) Draft: a comment for the report run
    const note = new Sw.TextBox({ Label: _T("Comment for the next run", "Label"), Multiline: true, Rows: 3 });
    const send = new Sw.Button({ Text: _T("Save comment", "Button"), Kind: "primary", OnClick: async () => { await Sw.Util.Delay(300); P.log.push("comment " + note.Get()); await P.draft.Clear(); } });
    page.Add(Sw.Card({ Title: _T("Comment", "Card"), Body: [note.E, send.E] }));
    P.draft = Sw.Draft.Attach(note, { Context: "lab.rep.comment" });
    P.note = note; P.ready = true;
});
