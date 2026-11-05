/**
 * @file naive/raw.js - a page written the naive way: raw DOM, raw buttons, confirm(), fetch, a hand-made table.
 */
"use strict";

Sw.Page.Main(async () => {
    const page = Sw.PageLayout({ Type: "form", Title: _TF("Naive raw page", "Title of a test page"), Description: _TF("Raw DOM written by a naive author.", "Description of a test page") });

    //  A form without labels, without dirty tracking, without drafts
    const form = document.createElement("div");
    form.className = "nv-form";
    const name = document.createElement("input");
    name.placeholder = _TF("Name", "Placeholder of the name field");
    const note = document.createElement("textarea");
    note.placeholder = _TF("A long note", "Placeholder of the note field");
    const save = document.createElement("button");
    save.className = "nv-btn";
    save.textContent = _TF("Save", "Button that saves the form");
    const del = document.createElement("button");
    del.className = "nv-btn";
    del.textContent = _TF("Delete all", "Button that deletes all rows");
    form.append(name, note, save, del);

    //  Save: fetch, native disabled while busy, alert when done
    save.addEventListener("click", async () => {
        save.disabled = true;
        await fetch("../Api/naive/Save", { method: "POST", body: JSON.stringify({ Name: name.value, Note: note.value }) });
        save.disabled = false;
        window.__saves = (window.__saves || 0) + 1;
        alert(_TF("Saved", "Message after save"));
    });

    //  Delete: native confirm, no danger styling
    del.addEventListener("click", async () => {
        if (!confirm(_TF("Are you sure?", "Question before delete")))
            return;
        await fetch("../Api/naive/DeleteAll");
        window.__deleted = (window.__deleted || 0) + 1;
    });

    //  A hand-made table: rows open on click (also on a ctrl / shift click)
    const t = document.createElement("table");
    t.className = "nv-table";
    const rows = await (await fetch("../Api/naive/Rows")).json();
    for (const r of rows) {
        const tr = document.createElement("tr");
        tr.className = "nv-row";
        for (const v of [r.Id, r.Name, r.Size]) {
            const td = document.createElement("td");
            td.className = "nv-cell";
            td.textContent = v;
            tr.append(td);
        }
        tr.addEventListener("click", () => { window.__opened = (window.__opened || 0) + 1; });
        t.append(tr);
    }
    page.Add(form, t);
});
