/**
 * @file good/h1.js - long work done right: a server export job, and a local re-check that really stops on Cancel.
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Reports", "Page title of the shipping reports"), Description: _TF("Exports and checks of the shipments.", "Page description") });
    document.body.append(page.E);

    const exportAll = new Sw.Button({
        Text: _TF("Export all shipments", "Button that exports every shipment as a CSV file on the server"),
        Icon: "download",
        Title: _TF("The server makes a CSV file of every shipment; you can leave the page meanwhile", "Tool tip of the export button"),
        OnClick: () => Sw.Activity.RunServer(Sw.Url.Root + "Api/ship/StartExportAll", { Format: "csv" }),
    });

    const recheck = new Sw.Button({
        Text: _TF("Re-check addresses", "Button that checks every shipment address again"),
        Title: _TF("Asks the address service about every open shipment, one at a time (can be cancelled)", "Tool tip"),
        OnClick: () => Sw.Activity.Run({ Key: "ship.recheck", Title: _TF("Re-checking the addresses", "Activity title"), Kind: "local" }, async (signal, job) => {
            const ids = await Sw.Net.Get("../Api/ship/GetOpenIds", { Signal: signal }) || [];
            let n = 0;
            for (const id of ids) {
                Sw.Cancel.Check(signal);
                await Sw.Net.Send("../Api/ship/CheckAddress", { Id: id }, { Signal: signal });
                ++n;
                job.Update({ Progress: n / ids.length, Text: _T("{0} of {1} checked", Sw.Format.Number(n), Sw.Format.Number(ids.length), "Progress text. {0}: addresses checked, ex: 12; {1}: all addresses, ex: 80") });
                await Sw.Sleep(200, signal);
            }
            return { Text: _T("{0} addresses checked", Sw.Format.Number(n), "Result of the re-check. {0}: number, ex: 80") };
        }),
    });

    page.Add(Sw.Card({ Title: _TF("Exports and checks", "Card title"), Body: [exportAll, recheck] }));
});
