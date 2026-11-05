"use strict";
// Playlist: reorder by drag, by Alt+Up / Alt+Down and by a row menu; a live "now playing" line that is never a button's work
Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "read", Title: _TF("Playlist", "Page title"),
        Description: _TF("The order the lobby screens play the videos in", "Page description") });
    document.body.append(page.E);
    const now = Sw.el("p.sw-selectable", { attrs: { "aria-live": "polite" } });
    const list = Sw.el("ul.sw-rows", { attrs: { "aria-label": _TF("Videos in play order", "Accessible name of the playlist") } });
    page.Add(Sw.Card({ Title: _TF("Now playing", "Card title"), Body: [now] }), Sw.Card({ Title: _TF("Order", "Card title"), Body: [list] }));

    let items = await Sw.Net.Read("../Api/playlist/GetItems") || [];

    function move(from, to) {
        if (to < 0 || to >= items.length || from === to)
            return;
        const [it] = items.splice(from, 1);
        items.splice(to, 0, it);
        render(it.Id);
        return Sw.Net.Send("../Api/playlist/SetOrder", { Ids: items.map(x => x.Id) });
    }

    function render(focusId) {
        list.textContent = "";
        if (!items.length) {
            list.append(Sw.StateView({ Kind: "empty", Small: true, Title: _TF("The playlist is empty", "Empty state") }).E);
            return;
        }
        items.forEach((it, i) => {
            const menu = new Sw.Button({ Icon: "more", Small: true, Kind: "ghost",
                AriaLabel: _T("Actions of {0}", it.Name, "Row menu button of a video"), Title: _TF("Move this video", "Tool tip of the row menu"),
                Menu: [
                    { Text: _TF("Move up", "Row menu"), Disabled: i === 0, OnClick: () => move(i, i - 1) },
                    { Text: _TF("Move down", "Row menu"), Disabled: i === items.length - 1, OnClick: () => move(i, i + 1) },
                ] });
            const li = Sw.el("li", { tabindex: 0, dataset: { swItem: it.Id } }, Sw.el("span.sw-rows-text", { text: it.Name }), menu);
            li.addEventListener("keydown", ev => {
                if (ev.altKey && (ev.key === "ArrowUp" || ev.key === "ArrowDown")) {
                    ev.preventDefault();
                    move(i, ev.key === "ArrowUp" ? i - 1 : i + 1);
                }
            });
            Sw.Drag(li, { OnEnd: (ev, s, cancelled) => { if (cancelled) return; const to = Math.max(0, Math.min(items.length - 1, i + Math.round(s.DY / li.offsetHeight))); move(i, to); } });
            list.append(li);
            if (it.Id === focusId)
                Sw.Focus.Show(li);
        });
    }
    render();

    const hint = Sw.Help.Learned("playlist.move", { Text: _TF("Drag a video, press Alt+Up / Alt+Down, or use its menu to change the order", "Usage hint above the playlist"), Target: list });
    list.before(hint.E);

    const poll = async () => {
        try {
            const p = await Sw.Net.Live("../Api/playlist/NowPlaying", null);
            now.textContent = p ? _T("Now playing: {0}", p.Name, "Live line") : _TF("Nothing is playing", "Live line");
        } catch (e) {
            if (Sw.Net.ShouldReport(e))
                now.textContent = Sw.Net.Message(_TF("The live line could not be updated", "What failed"), e);
        }
        setTimeout(poll, 5000);
    };
    poll();
});
