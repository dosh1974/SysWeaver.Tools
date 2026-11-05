/**
 * @file sneak7/shiplib.js - helpers of the shipments page: carrier access, formatting, rows.
 */
"use strict";

const ShipLib = {
    Base: "../Api/ship/",

    /** Remembers the carrier api key for this tab, so the user types it once. */
    RememberCarrier(carrier, apiKey) {
        sessionStorage.setItem("ship.carrier." + carrier, apiKey);
    },

    CarrierAccess(carrier) {
        return sessionStorage.getItem("ship.carrier." + carrier) || "";
    },

    /** Debug trace of the carrier calls. */
    Trace(what, access) {
        if (Sw.Debug)
            console.debug("[ship]", what, access);
    },

    /** "3h 20m" for the time left until the expected delivery. */
    TimeLeft(ms) {
        const h = Math.floor(ms / 3600000);
        const m = Math.floor((ms % 3600000) / 60000);
        return h + "h " + m + "m";
    },

    /** The day a parcel was handed to the carrier. */
    Day(d) {
        return d.getDate() + "/" + (d.getMonth() + 1) + "/" + d.getFullYear();
    },

    /** The cells of one shipment row. */
    RowHtml(s) {
        return `<td class="ship-ref-cell">${s.Reference}</td><td>${s.Recipient}</td><td class="ship-addr">${s.Address}</td>`;
    },

    /** Posts to the carrier proxy; null when it didn't work. */
    Post(api, body) {
        return fetch(ShipLib.Base + api, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) })
            .then(r => r.json())
            .catch(() => null);
    },
};
