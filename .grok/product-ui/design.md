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

## Thumb wheel card (locked 2026-10-19, round 3: card 3 variant 2)

- Opens from the thumb wheel label on the stage, for the app in the strip. Header "Thumb wheel · <app>".
- Two modes, each a row: header (name, speed notches right) above a drawn knurled thumb wheel between a left chip and a right chip.
  - Turn: chips show the two directions of the thumb wheel binding (Scroll left / Scroll right by default); either opens the thumb wheel picker.
  - Holding top button: on/off toggle in the header; chips are Scroll left (Back) and Scroll right (Forward) of the second action, each with a muted caption; each opens the action picker.
- Speed: 7 notches like Haptic feel (25, 50, 75, 100, 150, 200, 400 %), per mode, per app; "same as All apps" until changed.
- Result preview under each wheel: tab strip whose highlight moves (tab / sheet pairs), a sheet of lettered columns sliding sideways (side scroll), otherwise the chip flashes and counts (×1, ×2…). Driven live by the real thumb wheel and top button while the card is open; nothing runs. The live mode gets a faint copper wash; hint line "Turn the thumb wheel to preview · nothing runs while this is open".
- Footer: "Quick press of the top button: <its job>"; inherited line with Customise / Use All apps.
- Prototype: docs/thumbcard-ring-motion-round3.html.

## Actions Ring motion (locked 2026-10-19, round 3)

- Settings > Actions Ring: Opening = Off / Pop / Clockwise bloom / Fly in (default Clockwise bloom); Animation speed slider Slowest, Slow, Normal, Quick, Quickest (default Normal).
- Quickest = base timings (Pop 240 ms open, Bloom 440 ms, Fly in 400 ms); each step slower adds 0.4x (Normal 1.8x, Slowest 2.6x). Close = 3/4 of open.
- Bloom: hub first, then wedges grow from the hub clockwise from 12 o'clock with a small overshoot, rim turns in; close runs anticlockwise. Fly in: wedges slide in along their own direction with a slight twist, rim spins a quarter turn; close throws them out. Pop: whole ring grows from the cursor with overshoot.
- The ring takes input from the first frame. Off or Windows animations off: 90 ms fade.
