# sneak7 key (41 planted mistakes; written before the run, without reading the checker or its corpus)

Families: C = claimed by the round 3 gate (security, formatting, non-click pointer, silent failures, untranslated); N = new families.

| Id | File:line | Mistake | Rule | Fam |
|---|---|---|---|---|
| H1 | shipments.html:7 | web font from fonts.googleapis.com | PRF-3, LAY-6 | N |
| H2 | shipments.html:18 | img without width/height/aspect-ratio (css only display:block) | LAY-1 | N |
| H3 | shipments.html:19 | icon-only button without a name | ACC-5, TIP-2 | N |
| H4 | shipments.html:20 | input with placeholder only, no label | FRM-1 | N |
| H5 | shipments.html:21 | aria-description with words in HTML (not translated) | TXT-13 | C(untr) |
| H6 | shipments.html:22 | `<a>` without href (not focusable, not a link) | INP-21, LST-6 | N |
| C1 | shipments.css:15 | 16x16 px button | INP-24 | N |
| C2 | shipments.css:27 | `:focus { outline: none }` without replacement | FOC-3 | N |
| C3 | shipments.css:31 | hover not under `@media (hover: hover)` | INP-11 | N |
| C4 | shipments.css:36 | `min-width: 1280px` wrapper: horizontal page scroll | LAY-8 | N |
| C5 | shipments.css:45 | faded to opacity 0, not hidden | MOT-7 | N |
| C6 | shipments.css:53 | endless blink (theme duration, still infinite) | MOT-6 | C(css) |
| C7 | shipments.css:59 | nowrap + hidden + max-width, no ellipsis | TXT-17, LAY-8 | C(css) |
| L1 | shiplib.js:11 | api key in sessionStorage (key name neutral, argument `apiKey`) | SEC-1 | C(sec) |
| L2 | shiplib.js:21 | the key logged by a trace helper (`access`) | SEC-1 | C(sec) |
| L3 | shiplib.js:28 | duration "3h 20m" by hand | TXT-20 (+TXT-1) | C(fmt) |
| L4 | shiplib.js:33 | date d/m/y by hand | TXT-20 | C(fmt) |
| L5 | shiplib.js:38 (+ shipments.js:138) | user data in a template literal -> innerHTML | SEC-2 | C(sec) |
| L6 | shiplib.js:45 | raw fetch, `.catch(() => null)`, non-ok answers parsed: failure = null | FBK-5 | C(silent) |
| P1 | shipments.js:21 | select change navigates | ACC-11 | N |
| P2 | shipments.js:28 | `setAttribute("aria-label", "Refresh shipments")` literal | TXT-1 | C(untr) |
| P3 | shipments.js:35 | "Are you sure?" with no text, no Danger, destructive | SAF-2 (SAF-1/3) | N |
| P4 | shipments.js:47 | `Promise.allSettled` then "n notices were sent again" (failures dropped) | FBK-5 | C(silent) |
| P5 | shipments.js:61 | catch that only clears the status line | FBK-5 | C(silent) |
| P6 | shipments.js:128 (shown at :75) | thrown English error text shown by Sw.Fail / the button | TXT-1 | C(untr) |
| P7 | shipments.js:87 | password TextBox not masked (no Type password) | SEC-1 | C(sec) |
| P8 | shipments.js:98 | Save disabled until valid, no reason | FRM-7, INP-12 | N |
| P9 | shipments.js:102 | customer message (long text) without a draft | SAF-11 | N |
| P10 | shipments.js:112-113 | Alt+Left / Alt+Right page shortcuts | INP-23 | N |
| P11 | shipments.js:117 | hover (mouseenter) opens a menu | INP-8 | C(ptr) |
| P12 | shipments.js:122 | initial focus on the danger button | FOC-2 | N |
| P13 | shipments.js:139 | `toISOString().slice(0, 10)`: UTC day shown as a local date | TXT-20 | C(fmt) |
| P14 | shipments.js:140 | plural by appending "s" | TXT-6 | C(fmt) |
| P15 | shipments.js:146 | selection toggled on pointerdown | INP-1, INP-7 | C(ptr) |
| P16 | shipments.js:147 | open on pointerup (no click) | INP-1 | C(ptr) |
| P17 | shipments.js:154-155 | a click on the cell copies | LST-4, INP-31 | N |
| P18 | shipments.js:156 | `Sw.Info("Reference copied")` literal | TXT-1 | C(untr) |
| P19 | shipments.js:161 | `scrollIntoView({ behavior: "smooth" })` ignores reduced motion | MOT-3 | N |
| P20 | shipments.js:137 | focusable row (tabindex 0) with no Enter / Space path | INP-20, INP-21 | N |
| P21 | shipments.js:82/135 | table rows without header cells | ACC-3 | N |
| P22 | shipments.js:133 | list load without empty / error state | FBK-6 | N |

Counts: 41; claimed families 20 (sec 4, fmt 4, ptr 3, silent 3, untr 4, css 2), new families 21.
Not a mistake (watch for false positives): "DHL" / "UPS" option texts (product names), `Sw.Activity.Run` export, `_T("{0}: {1}")`, Send's `.then` returned.
