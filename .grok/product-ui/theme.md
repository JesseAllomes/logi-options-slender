---
product: Slender
mode: dark only (Windows app theme is dark on this PC); light is a separate future decode, never an invert
---

# Theme — graphite and copper

Material: Mica on the window, Acrylic on the tray popover and flyouts. Graphite like the MX Master 4 shell, copper like machined metal.

| Role | Default | Hover | Active | Disabled | on-* |
|---|---|---|---|---|---|
| paper (window ground) | #1B1F23 | — | — | — | ink |
| paper-2 (rail, flyouts) | #22282D | #262C32 | #2A3137 | — | ink |
| paper-3 (selected surface, chips) | #2A3137 | #303840 | #343C43 | — | ink |
| rule | #343C43 | — | — | — | — |
| ink | #ECEFF1 | — | — | #6B747B | — |
| muted (secondary text, "same as All apps") | #8F989F | — | — | — | — |
| mark (copper: hotspots, leader lines, selection, toggles) | #D9925F | #E3A272 | #C9824F | #5E4A3C | paper |
| mark-soft (selected label/row fill) | rgba(217,146,95,.16) | — | — | — | ink |
| live (connected, battery ok) | #6CCB9A | — | — | — | paper |
| warn (low battery, Options+ running) | #E3B04B | — | — | — | paper |
| danger (errors only) | #E06A5C | — | — | — | paper |
| focus (keyboard focus ring) | #F2B38A | — | — | — | — |

Contrast: ink on paper 14.6:1, muted on paper 6.0:1, paper on mark 6.1:1.

Type
- UI: Segoe UI Variable (WinUI type ramp). Body 13–14, caption 11.5–12.
- Display: Archivo, width 125, weight 800, only for the app name heading (34 px). Bundled OFL font.
- Mono: JetBrains Mono for key caps and control IDs. Bundled OFL font.
- Rail/popover headings: Sora 600. Bundled OFL font.

Space and shape: 4 px grid. Radius 6 (labels, rows), 8–10 (flyouts, rail items), 12 (popover). No card grid.
Motion: one signature only, the tray popover unfolding into the window (250 ms, ease-out, disabled with reduced motion). Hotspot select: copper halo, no animation.
Voice: plain verbs. "Same as All apps", "Add app", "Compare apps", "Quit Options+". Errors say what to do next.
