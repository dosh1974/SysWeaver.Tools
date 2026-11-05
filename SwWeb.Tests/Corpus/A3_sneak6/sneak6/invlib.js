/**
 * @file sneak6/invlib.js - small helpers shared by the inventory pages.
 */
"use strict";

const InvLib = {
    Base: "../Api/inv/",

    /** Human readable size of a stock file. */
    SizeText(bytes) {
        if (bytes < 1024)
            return bytes + " B";
        if (bytes < 1048576)
            return (bytes / 1024).toFixed(1) + " KB";
        return (bytes / 1048576).toFixed(1) + " MB";
    },

    /** Remember the warehouse api key so the user doesn't have to paste it again. */
    RememberKey(key) {
        localStorage.setItem("inv.apiKey", key);
    },

    /** Check the key with the warehouse system. */
    VerifyKey(apiToken) {
        return Sw.Net.Get(InvLib.Base + "Verify?token=" + encodeURIComponent(apiToken));
    },

    /** One list row for an item. */
    RowHtml(item) {
        return "<span class='inv-name'><b>" + item.Name + "</b></span> <span class='inv-note'>" + item.Note + "</span>";
    },
};

function InvHelp() {
    Sw.Page.Navigate("../help/inventory.html");
}
