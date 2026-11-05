"use strict";
// Archive files: a list / detail page with empty, loading and error states, formatted sizes and times, copy buttons
Sw.Page.Main(async () => {
    const refresh = new Sw.Button({ Icon: "reload", Title: _TF("Reload the list of archive files", "Tool tip of the reload button"),
        OnClick: () => load() });
    const page = Sw.PageLayout({ Type: "fill", Title: _TF("Archive files", "Page title"),
        Description: _TF("Files moved to the archive in the last 90 days", "Page description of the archive files"), Actions: [refresh] });
    document.body.append(page.E);

    const detail = Sw.el("div.sw-selectable");
    const state = Sw.StateView({ Kind: "loading", Title: _TF("Loading the archive files", "Loading state of the archive list") });
    const list = Sw.SelectList({
        Label: _TF("Archive files", "Accessible name of the archive list"),
        Items: [],
        Empty: { Icon: "archive", Title: _TF("No archive files", "Empty state title"),
            Text: _TF("Files moved to the archive show up here.", "Empty state text of the archive list") },
        OnSelect: (key, items, how) => { show(items[0]); if (key && how === "click") ld.ShowDetail(true, true); },
        OnOpen: () => ld.ShowDetail(true, true),
    });
    const ld = Sw.ListDetail({ List: list, Detail: detail, Label: _TF("Archive files", "Accessible name of the list and detail") });
    page.Add(state, ld);

    function show(f) {
        detail.textContent = "";
        if (!f) {
            detail.append(Sw.StateView({ Kind: "empty", Small: true, Title: _TF("Select a file to see its details", "Detail state when nothing is selected") }).E);
            return;
        }
        const [tip, when] = Sw.Format.TimeStamp(f.Moved);
        detail.append(Sw.Facts({ Card: false, Items: [
            { Label: _TF("Name", "Fact label: file name"), Value: f.Name, Copy: true },
            { Label: _TF("Size", "Fact label: file size"), Value: Sw.Format.Bytes(f.Bytes) },
            { Label: _TF("Moved", "Fact label: when the file was archived"), Value: when, Title: tip },
            { Label: _TF("Checksum", "Fact label: SHA-256 of the file"), Value: f.Sha256, Mono: true, Copy: true },
        ] }).E);
        detail.append(Sw.CopyButton({ Value: f.Path, What: _TF("Path", "What was copied: the file path"),
            Label: _T("Copy the path of {0}", f.Name, "Copy button of a file path") }).E);
    }

    async function load() {
        state.Set({ Kind: "loading", Title: _TF("Loading the archive files", "Loading state of the archive list") });
        state.E.hidden = false;
        try {
            const files = await Sw.Net.Read("../Api/archive/GetFiles") || [];
            list.SetItems(files.map(f => ({ Key: f.Id, Text: f.Name, Sub: Sw.Format.Bytes(f.Bytes), Meta: Sw.Format.TimeStamp(f.Moved)[1], Item: f })));
            state.E.hidden = true;
            show(null);
        } catch (e) {
            if (!Sw.Net.ShouldReport(e)) {
                state.Set({ Kind: "error", Title: _TF("Offline", "Error state: no connection"), Text: _TF("The list loads when the connection is back.", "Error state text: offline") });
                Sw.Events.On("net.online", load);
                return;
            }
            state.Set({ Kind: "error", Title: _TF("The archive files could not be loaded", "Error state title"),
                Text: Sw.Net.Message(_TF("Loading the archive files failed", "What failed"), e, { Do: _TF("Reload to try again.", "Advice after a failed load") }),
                Actions: [{ Text: _TF("Reload", "Button in the error state"), OnClick: () => load() }] });
        }
    }

    await load();
});
