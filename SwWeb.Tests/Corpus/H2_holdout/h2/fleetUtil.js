/** @file h2/fleetUtil.js - small helpers for the device fleet page (formatting, rows, session). */
"use strict";

window.FleetUtil = (() => {

    const MB = 1024 * 1024;

    /** Size of the device's log buffer, ex: "12.5 MB". */
    function logSize(bytes) {
        return (bytes / MB).toFixed(1) + " MB";
    }

    /** Free storage on the device. */
    function storageText(bytes) {
        return Sw.Format.Bytes(bytes, 1);
    }

    /** Clock time of the last heartbeat. */
    function heartbeat(iso) {
        const d = new Date(iso);
        return d.getHours() + ":" + String(d.getMinutes()).padStart(2, "0");
    }

    /** Battery level 0..1 as text. */
    const battery = level => `${Math.round(level * 100)}%`;

    /** "3 devices" for the group header. */
    function countText(n) {
        return n + " " + _TF("device", "Noun after a count of devices in a group header, ex: '3 device'") + (n === 1 ? "" : "s");
    }

    /** Builds the html of one device row. */
    function rowHtml(d) {
        return `<span class="fleet-handle" aria-hidden="true">::</span>
            <span class="fleet-name">${d.Name}</span>
            <span class="fleet-owner">${d.Owner}</span>
            <span class="fleet-battery">${battery(d.Battery)}</span>`;
    }

    /** Puts server text into an element. */
    function setText(el, value) {
        el.textContent = value == null ? "" : String(value);
        return el;
    }

    /** Remembers the fleet api key for the export tool. */
    function rememberKey(key) {
        document.cookie = "fleetApiKey=" + encodeURIComponent(key) + "; path=/; max-age=2592000";
    }

    const trace = (...parts) => console.info("[fleet]", ...parts);

    return { logSize, storageText, heartbeat, battery, countText, rowHtml, setText, rememberKey, trace };
})();
