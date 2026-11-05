/*
 * Returns overview page: the numbers of the last 30 days. Counts, amounts, durations and times go through Sw.Format, every text through
 * the translation functions; the tiles keep their place while loading and the area has an empty and an error state.
 */
(function () {
    "use strict";

    Sw.Page.Main(() => {
        const page = Sw.PageLayout({
            Type: "dash",
            Title: _TF("Returns overview", "Title of the page with the return numbers of the last 30 days"),
            Description: _TF("Returns of the last 30 days.", "One line description under the title of the returns overview page"),
        });

        const count = Sw.Kpi({ Label: _TF("Returns", "Label of the tile with the number of returns in the last 30 days"), Span: 4 });
        const refunded = Sw.Kpi({ Label: _TF("Refunded", "Label of the tile with the total amount refunded in the last 30 days"), Span: 4 });
        const checkTime = Sw.Kpi({ Label: _TF("Average check time", "Label of the tile with the average time from arrival to the end of the check"), Span: 4 });
        const facts = Sw.Facts({
            Items: [
                { Key: "updated", Label: _TF("Updated", "Label of the time the numbers were computed"), Value: "" },
                { Key: "share", Label: _TF("Share restocked", "Label of the share of returned items put back in stock"), Value: "" },
            ],
        });
        const grid = Sw.Grid({ Content: [count, refunded, checkTime] });
        const state = Sw.StateView({ Kind: "loading", Small: true, Lines: 3 });
        state.E.hidden = true;
        page.Add(facts, grid, state.E);

        function showState(o) {
            grid.E.hidden = true;
            state.Set(o);
            state.E.hidden = false;
        }

        async function load() {
            try {
                const s = await Sw.Net.Get("../Api/returns/Stats");
                if (!s.Count) {
                    showState({
                        Kind: "empty",
                        Small: true,
                        Title: _TF("No returns in the last 30 days", "Title of the empty state of the returns overview"),
                        Text: _TF("The numbers appear here after the first return was registered in the shop.", "Text of the empty state of the returns overview"),
                    });
                    return;
                }
                count.Set({ Value: Sw.Format.Number(s.Count) });
                refunded.Set({ Value: Sw.Format.Amount(s.Currency, s.Refunded) });
                checkTime.Set({ Value: Sw.Format.DurationMs(s.AverageCheckMs) });
                const [zone, updated] = Sw.Format.TimeStamp(s.Updated);
                facts.Set("updated", updated);
                facts.Get("updated").SetTitle(zone);
                facts.Set("share", _T("{0} %", Sw.Format.Number(s.RestockedShare * 100, 1), "Share of returned items put back in stock. {0}: a percentage, ex: 42.5"));
                state.E.hidden = true;
                grid.E.hidden = false;
            } catch (e) {
                if (!Sw.Net.ShouldReport(e))
                    return;
                showState({
                    Kind: "error",
                    Small: true,
                    Title: _TF("The overview could not be loaded", "Title of the error state of the returns overview"),
                    Text: _TF("The server did not answer as expected. Try again; if it keeps failing, give the details to support.", "Text of the error state of the returns overview"),
                    Details: String(e && e.message ? e.message : e),
                    Actions: [new Sw.Button({
                        Text: _TF("Try again", "Button in the error state of the returns overview"),
                        Title: _TF("Load the numbers of the last 30 days again", "Tool tip of Try again in the error state of the returns overview"),
                        OnClick: () => load(),
                    })],
                });
            }
        }

        Sw.Events.On("net.online", () => load());
        return load();
    });
})();
