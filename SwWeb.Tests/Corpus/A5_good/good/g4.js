"use strict";
// Report builder: long work in the activity list with progress and an honest Cancel, a server export, a refused start shown once
Sw.Page.Main(async () => {
    const months = new Sw.Select({ Label: _TF("Period", "Label of the report period"), Value: 3,
        Items: [{ Value: 1, Text: _TF("Last month", "Report period") }, { Value: 3, Text: _TF("Last 3 months", "Report period") }, { Value: 12, Text: _TF("Last 12 months", "Report period") }] });

    const build = new Sw.Button({
        Text: _TF("Build report", "Button: build the sales report"), Kind: "primary",
        Title: _TF("Collect the sales of the period into one report; it runs in the background", "Tool tip of Build report"),
        OnClick: () => {
            const period = months.Get();
            return Sw.Activity.Run({ Key: "report:" + period, Title: _T("Building the sales report ({0} months)", period, "Activity title") }, async (signal, job) => {
                const parts = await Sw.Net.Read("../Api/reports/GetParts", { Months: period }, { Signal: signal });
                const rows = [];
                for (let i = 0; i < parts.length; ++i) {
                    Sw.Cancel.Check(signal);
                    rows.push(...await Sw.Net.Read("../Api/reports/GetPart", { Part: parts[i] }, { Signal: signal }));
                    job.Update({ Progress: (i + 1) / parts.length, Text: _T("{0} of {1} parts", i + 1, parts.length, "Activity progress") });
                    await Sw.Sleep(200, signal);
                }
                return rows.length;
            });
        },
    });

    const exp = new Sw.Button({
        Text: _TF("Export to Excel", "Button: export the report as a file"),
        Title: _TF("The server writes the report to an Excel file; you can leave the page meanwhile", "Tool tip of Export"),
        OnClick: () => Sw.Activity.RunServer(Sw.Url.Root + "Api/reports/StartExport", { Months: months.Get() },
            { Title: _TF("Sales report export", "Activity title of the export") }),
    });

    const page = Sw.PageLayout({ Type: "form", Title: _TF("Report builder", "Page title"),
        Description: _TF("Sales reports built from the order history", "Page description"), Actions: [build, exp] });
    document.body.append(page.E);
    page.Add(Sw.Card({ Title: _TF("Report", "Card title"), Body: [months] }));
});
