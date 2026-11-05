/*
 * Warehouse returns: archive one return at once and offer Undo (UX guide SAF-5). The archive keeps the return, so it can always be
 * brought back: no question first, Undo in the toast and next to the row for 60 s.
 */
(function () {
    "use strict";

    const api = "../Api/returns/";
    const undoMs = 60000;

    /**
     * Shows the archived line in place of the row, with Undo for 60 s and a toast with the same Undo.
     * @param {HTMLElement} row The row of the return (put back by Undo).
     * @param {{ Id: number, Rma: string }} r The return.
     */
    function showArchived(row, r) {
        const line = Sw.el("div.ret-archived.sw-selectable");
        let toast = null;
        const undo = new Sw.Button({
            Text: _TF("Undo", "Button after a return was archived: brings it back to the open returns"),
            AriaLabel: _T("Undo archiving {0}", r.Rma, "Accessible name of the Undo button after archiving. {0}: the return number, ex: RMA-10442"),
            Title: _T("Bring {0} back to the open returns", r.Rma, "Tool tip of the Undo button after archiving. {0}: the return number, ex: RMA-10442"),
            Kind: "ghost",
            Small: true,
            Done: false,
            OnClick: async () => {
                await Sw.Net.Send(api + "Restore", { Id: r.Id });
                clearTimeout(timer);
                if (toast)
                    toast.Close();
                line.replaceWith(row);
                Sw.Focus.Restore(row);
            },
        });
        line.append(_T("{0} was archived.", r.Rma, "Line in the list where an archived return was. {0}: the return number, ex: RMA-10442"), " ", undo.E);
        row.replaceWith(line);
        undo.Focus();

        toast = Sw.Toast(_T("{0} was archived.", r.Rma, "Toast after a return was archived. {0}: the return number, ex: RMA-10442"), {
            Kind: "ok",
            Action: { Text: _TF("Undo", "Button in the toast after a return was archived: brings it back"), OnClick: () => undo.Click() },
        });

        const timer = setTimeout(() => {
            const keep = Sw.Focus.Keep(line.parentElement || document.body);
            undo.Destroy();
            keep.Restore();
        }, undoMs);
    }

    /**
     * The Archive button of one return row.
     * @param {HTMLElement} row The row of the return.
     * @param {{ Id: number, Rma: string }} r The return.
     * @returns {Sw.Button} The button.
     */
    function ArchiveButton(row, r) {
        return new Sw.Button({
            Text: _TF("Archive", "Button on a return row: moves the return to the archive, where it can be restored"),
            AriaLabel: _T("Archive {0}", r.Rma, "Accessible name of the Archive button of one return. {0}: the return number, ex: RMA-10442"),
            Title: _TF("Move the return to the archive; Undo brings it back", "Tool tip of the Archive button of a return"),
            Small: true,
            Done: false,
            OnClick: async () => {
                await Sw.Net.Send(api + "Archive", { Id: r.Id });
                showArchived(row, r);
            },
        });
    }

    window.ReturnsArchive = { ArchiveButton };
})();
