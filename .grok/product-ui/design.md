---
product: Slender
locked: 2026-10-15
from: round 4 (round-3 variant 3 + variant 4 bottom bar and button picker)
---

# Design

- Signature: the real device render (from the local Options+ cache) with copper hotspots at Options+'s own slot coordinates and leader lines to labels.
- Risk spent: product-shot scale. The render is the hero, everything else stays quiet.
- Type pair: Archivo Expanded 800 for the app heading only; Segoe UI Variable for everything else; JetBrains Mono key caps.
- App identity: real icons extracted from each app's executable (or the package logo for Store apps). Never letter tiles.
- Labels: "<Button>" small muted, then the action. Unchanged = muted "<action> · same as All apps". Selected = mark-soft fill with copper edge.
- Picker: anchored flyout below the labels, tabs Keys (recorder) / Mouse / Excel / Ring / Default for the mouse; Keys / App / Media / Default for the keyboard. Esc cancels, Enter saves.
- Assets: device renders and app icons are copied at runtime into %LOCALAPPDATA%\Slender\assets. Never committed.

## Actions Ring (locked 2026-10-15, round 1: directions 1-3 kept as user-switchable styles)

- Styles: Bezel (8 wedges round a hub that names the slot, knurled rim, copper rim arc on the chosen wedge), Compass (no disc, 8 round icon buttons, copper needle from the cursor, only the chosen slot labelled), Keypad (3x3 keycaps, cursor in the middle key, name + mono shortcut on every cap, chosen cap pressed 2 px). Prototype: docs/ring-prototypes-round1.html.
- User options (Settings > Actions Ring): style, size S/M/L (0.8/1/1.2), accent colour (replaces copper in the ring only), opacity 40-100 %, labels always/selected only, position at cursor or monitor centre.
- Type: Sora for slot names, JetBrains Mono for shortcuts, Segoe Fluent Icons for glyphs; app/site icons from the file when the slot launches something.
- Folders: same style, same place, hub/header shows the folder name; centre goes back.
