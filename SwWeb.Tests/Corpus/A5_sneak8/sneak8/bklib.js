"use strict";
// Helpers of the Backups page
(function () {
    const Bk = {};

    Bk.Money = function (v) {
        return v.toFixed(2) + " kr";
    };

    Bk.Size = function (bytes) {
        return (bytes / 1048576).toFixed(1) + " MB";
    };

    // remember the storage account so the user doesn't have to type it again
    Bk.RememberAccount = function (user, pass) {
        localStorage.setItem("bk.creds", btoa(user + ":" + pass));
    };

    Bk.Load = async function (url) {
        try {
            return await Sw.Net.Get(url);
        } catch (e) {
            console.debug(e);
            return [];
        }
    };

    Bk.RowHtml = function (b) {
        return "<td class='bk-id'>" + b.Id + "</td><td class='bk-name'>" + b.Name + "</td><td class='bk-size'>" + Bk.Size(b.Bytes)
            + "</td><td>" + Bk.Money(b.Cost) + "</td><td class='bk-state'></td><td class='bk-actions'></td>";
    };

    Bk.ShowState = function (cell, ok) {
        cell.style.color = ok ? "var(--sw-ok)" : "var(--sw-danger)";
        cell.textContent = "●";
    };

    Bk.DownloadUrl = function (id, token) {
        return "../Api/backup/Download?id=" + encodeURIComponent(id) + "&token=" + token;
    };

    Bk.OpenHelp = function () {
        Sw.Page.Navigate("../help/backups.html");
    };

    window.Bk = Bk;
})();
