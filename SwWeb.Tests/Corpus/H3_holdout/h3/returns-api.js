/*
 * Warehouse returns: the server calls and small value helpers shared by returns.js and returns-detail.js.
 */
(function () {
    "use strict";

    const base = "../Api/returns/";
    const carrierKeyName = "Returns.CarrierKey";

    function pad2(n) {
        return n < 10 ? "0" + n : String(n);
    }

    /** The day a return was registered, as the warehouse writes it on the label. */
    function labelDate(value) {
        const d = new Date(value);
        return pad2(d.getDate()) + "." + pad2(d.getMonth() + 1) + "." + d.getFullYear();
    }

    /** A refund amount with its currency code. */
    function refundText(amount, currency) {
        return (Math.round(amount * 100) / 100).toFixed(2) + " " + currency;
    }

    const Api = {
        List: (filter, signal) => Sw.Net.Send(base + "List", filter, { Signal: signal }),
        Get: (id, signal) => Sw.Net.Send(base + "Get", { Id: id }, { Signal: signal }),
        Counts: () => Sw.Net.Get(base + "Counts"),
        Reasons: () => Sw.Net.Get(base + "Reasons"),
        Approve: id => Sw.Net.Send(base + "Approve", { Id: id }),
        SetRestock: (id, restock) => Sw.Net.Send(base + "SetRestock", { Id: id, Restock: restock }),
        SaveNote: (id, note) => Sw.Net.Send(base + "SaveNote", { Id: id, Note: note }),
        Remove: id => Sw.Net.Send(base + "Remove", { Id: id }),
        Export: filter => Sw.Activity.StartServer(base + "StartExport", filter, { Save: true }),

        async MarkReceived(id) {
            try {
                await Sw.Net.Send(base + "MarkReceived", { Id: id });
                return true;
            } catch (e) {
                return false;
            }
        },

        PrintLabel(id, carrierToken) {
            const url = base + "Label?id=" + encodeURIComponent(id) + "&token=" + encodeURIComponent(carrierToken);
            return Sw.Net.Get(url, { Blob: true });
        },

        CarrierToken: {
            Get: () => Sw.Storage.Local.Get(carrierKeyName, ""),
            Remember: token => Sw.Storage.Local.Set(carrierKeyName, token),
        },

        LabelDate: labelDate,
        RefundText: refundText,
    };

    window.ReturnsApi = Api;
})();
