/*
 * Warehouse returns: the "Print label again" item of a return's row menu. A menu item has no busy state of its own, so the handler
 * shows the result itself: a toast when the label went to the printer (it can't be seen on the page), the reason when it failed.
 */
(function () {
    "use strict";

    /**
     * Sends the return label of one return to the printer at the packing desk.
     * @param {{ Id: number, Rma: string }} r The return.
     * @returns {Promise<void>} Settles when the result was shown (it never rejects).
     */
    async function reprint(r) {
        try {
            await Sw.Net.Send("../Api/returns/PrintLabel", { Id: r.Id });
            Sw.Toast(_T("The label of {0} was sent to the printer at the packing desk.", r.Rma, "Toast after a return label was printed again. {0}: the return number, ex: RMA-10442"), { Kind: "ok" });
        } catch (e) {
            if (Sw.Net.ShouldReport(e))
                Sw.Fail(Sw.Net.FailText(_TF("Print label again", "Name of the action in the failure message when a return label could not be printed"), e));
        }
    }

    /**
     * The row menu items of a return.
     * @param {{ Id: number, Rma: string }} r The return.
     * @returns {object[]} Items for Sw.Menu.Show / Sw.Button({ Menu }).
     */
    function RowMenu(r) {
        return [
            {
                Text: _TF("Print label again", "Menu item of a return: prints its return label again at the packing desk"),
                Icon: "download",
                OnClick: () => reprint(r),
            },
        ];
    }

    window.ReturnsLabels = { RowMenu };
})();
