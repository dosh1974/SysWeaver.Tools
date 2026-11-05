/*
 * Late returns page: the returns past their check deadline, with a loading, an empty and an error state (UX guide FBK-6).
 * While the connection is lost the shell says so once; the list loads again when it is back.
 */
(function () {
    "use strict";

    Sw.Page.Main(() => {
        const page = Sw.PageLayout({
            Type: "dash",
            Title: _TF("Late returns", "Title of the page with the returns past their check deadline"),
            Description: _TF("Returns that were not checked before their deadline.", "One line description under the title of the late returns page"),
        });

        const card = Sw.ListCard({
            Title: _TF("Past the deadline", "Title of the card with the list of late returns"),
            Count: true,
            Span: 12,
            Empty: {
                Title: _TF("No late returns", "Title of the empty state of the late returns list"),
                Text: _TF("Returns that pass their check deadline show up here.", "Text of the empty state of the late returns list"),
            },
        });
        const state = Sw.StateView({ Kind: "loading", Small: true, Lines: 5 });
        card.BodyE.prepend(state.E);
        card.ListE.hidden = true;
        page.Add(Sw.Grid({ Content: [card] }));

        function item(r) {
            const [zone, deadline] = Sw.Format.TimeStamp(r.Deadline);
            return {
                Key: r.Id,
                Text: r.Rma,
                Meta: _T("Deadline {0}", deadline, "Second text of a late return. {0}: the check deadline as date and time, ex: 2026-10-20 14:00"),
                Title: zone,
                Status: { Text: _TF("Late", "Status of a return past its check deadline"), Kind: "warning" },
            };
        }

        function showError(e) {
            card.ListE.hidden = true;
            state.E.hidden = false;
            state.Set({
                Kind: "error",
                Small: true,
                Title: _TF("The late returns could not be loaded", "Title of the error state of the late returns list"),
                Text: _TF("The server did not answer as expected. Try again; if it keeps failing, give the details to support.", "Text of the error state of the late returns list"),
                Details: String(e && e.message ? e.message : e),
                Actions: [new Sw.Button({
                    Text: _TF("Try again", "Button in the error state of the late returns list"),
                    Title: _TF("Load the late returns again", "Tool tip of Try again in the error state of the late returns list"),
                    OnClick: () => load(),
                })],
            });
        }

        async function load() {
            try {
                const res = await Sw.Net.Send("../Api/returns/Late", null);
                state.E.hidden = true;
                card.ListE.hidden = false;
                card.SetItems((res.Items || []).map(item));
            } catch (e) {
                if (Sw.Net.ShouldReport(e))
                    showError(e);
            }
        }

        Sw.Events.On("net.online", () => load());
        return load();
    });
})();
