# KEY sneak8 (written BEFORE the SwWeb run; the checker source and its corpus were not opened)

Page "Backups": backups.html, backups.css, bklib.js (helper module, global `Bk`), backups.js (page).
Column "fam": C = a family the gate claims (rule mentions lint / ux-* in guide 1.0.6 support rows or round 3/4 rows);
N = a family I never planted before and not claimed; R = re-planted R4 miss in a new spelling (not claimed).

| Id | File | Rule | fam | Mistake |
|---|---|---|---|---|
| H1 | html | INP-25 | C | viewport `maximum-scale=1, user-scalable=0` |
| H2 | html | ACC-3 | N | heading levels skip: h1 → h4 |
| H3 | html | FRM-6 | N | "Notification e-mail" input `type="text"` |
| H4 | html | INP-21 | C | inline `onclick` on a `<span class="bk-link">` |
| H5 | html | FOC-4 | C | `tabindex="2"` on the filter input |
| H6 | html | PRF-3 | C | CDN script (jsdelivr dayjs) |
| C1 | css | INP-30 | C | `.bk-size { user-select: none }` (content value) |
| C2 | css | TXT-15 | N | `.bk-new::after { content: "New" }` |
| C3 | css | MOT-4 | N | `transition: height …` (animating height) |
| C4 | css | INP-24 | C | `.bk-del` 20x20 px, cursor pointer |
| C5 | css | MOT-7 | C | `.bk-panel.closing { opacity: 0 }` not hidden |
| C6 | css | INP-11 | C | `.bk-card:is(:hover, :focus-within)` outside `@media (hover: hover)` |
| C7 | css | MOT-3 | C | `html { scroll-behavior: smooth }` |
| C8 | css | INP-8 / LST-2 | N | row actions `visibility: hidden` unless `tr:hover` (touch / keyboard can't reach them) |
| L1 | bklib | TXT-20 | C | `v.toFixed(2) + " kr"` money |
| L2 | bklib | TXT-20 | C | `(b / 1048576).toFixed(1) + " MB"` size |
| L3 | bklib | SEC-1 | C | `localStorage.setItem("bk.creds", btoa(user + ":" + pass))` |
| L4 | bklib | FBK-5 | C | `catch (e) { console.debug(e); return []; }` (log only, empty list) |
| L5 | bklib | SEC-2 | C | row html by string concatenation of server data → `innerHTML` in the page |
| L6 | bklib | ACC-2 | N | state shown by `style.color` only (no text / icon) |
| L7 | bklib | SEC-1 | C | download url with `&token=` + the token |
| P1 | page | POP-11 | N | `Sw.Dialog.Alert` on page load |
| P2 | page | INP-6 | C | `dblclick` as the only way to open a backup |
| P3 | page | INP-23 | C | single key "d" on document deletes (no text field check) |
| P4 | page | INP-4 | C | `contextmenu` + `preventDefault` on the list |
| P5 | page | SAF-4 | N | "Delete all backups" confirmed with Danger but no type to confirm |
| P6 | page | SAF-6 | N | Archive (reversible) says "This can't be undone." |
| P7 | page | TXT-30 | N | `Sw.Fail(_TF("Error", …))` instead of what / why / do |
| P8 | page | INP-12 | C | `btn.setAttribute("disabled", "")` on a native button, no reason |
| P9 | page | ACC-4 | N | status line text replaced on a plain div, no live region |
| P10 | page | ACC-7 | N | `setTimeout` 120 s signs out and clears the form, no warning |
| P11 | page | SEC-1 | C | `Sw.TextBox({ Label: "Encryption passphrase" })` without `Type: "password"` |
| P12 | page | TXT-2 | C | `_TF("Restore " + name, …)` non literal |
| P13 | page | TXT-6 | C | `_TF("Restored", …) + " " + n + " " + _TF("files", …)` |
| P14 | page | TXT-1 | C | `Sw.Info(\`Backup ${name} started\`)` template literal, untranslated |
| P15 | page | TXT-1 | C | `return Promise.reject("Nothing selected")` from an OnClick (shown to users) |
| P16 | page | FBK-5 | C | auto save in a timer `.catch(() => {})` |
| P17 | page | FBK-6 | C | list rendered by hand with no empty / error state |
| P18 | page | FOC-2 | C | `delAll.Focus()` on load (danger button) |
| P19 | page | TIP-1 | C | icon-only `Sw.Button({ Icon: "reload" })` without Title |
| P20 | page | SAF-1/2 | C | `Sw.Dialog.Confirm(…, _TF("Erase", …))` destructive without `Danger` |
| P21 | page | SAF-1 | C | raw `window.confirm(...)` |
| P22 | page | INP-1 | C | `touchstart` acts (select + open) |
| P23 | page | LST-4 | R | click on the id cell copies (`navigator.clipboard.writeText`) |
| P24 | page | ACC-11 | R | `Sw.Select` OnChange → `location.href = …` |
| P25 | page | SEC-1 | C | `console.info("passphrase", pass)` |
| P26 | page | INP-20 | C | drag-only reorder (`draggable`, `drop`), no keyboard / menu path |
| P27 | page | TIP-7 | N | "Double-click to open" helper in every row |
| P28 | page | FRM-2 | N | name required (SetError "Name is required") but not marked Required |

Total 42: C 28, N 12, R 2.
