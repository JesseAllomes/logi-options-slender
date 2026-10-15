---
product: Slender
locked: 2026-10-15
---

# Layout

Window (WinUI 3, Mica, custom title bar, min 1040x640, remembers size and last device/app):

- Title bar: Slender glyph + name, caption buttons. Nothing else.
- Rail (left, 76 px): one tile per device (render thumbnail + battery text), selected tile has copper edge; separator; gear = Point & scroll / Haptics / General page.
- Stage (centre): app heading (real icon + Archivo name + "N buttons changed · others follow All apps"), device render at max height, hotspots, leader lines, labels left/right per Options+ label side, picker flyout under the labels.
- App strip (right, 78 px): All apps tile, app icons with names, "+ Add app" (running apps list or .exe picker). Ctrl+1..9.
- Bottom bar (spans stage): device quick settings. Mouse: Haptic feel (4 notches), SmartShift free-spin, Excel thumb wheel scrolls columns, Compare apps. Keyboard: Fn lock, Easy-Switch host chips, Compare apps.
- Compare apps: full grid (controls x apps) as a ContentDialog-sized overlay, cells editable inline with the same picker.
- InfoBars at the top of the stage: device not connected; Options+ running (with "Quit Options+"); low battery.

Tray popover (Acrylic, 330 px): device rows with battery, the same quick toggles, "You're in <app> · N buttons changed", "Open Slender" (unfolds into the window).

Keyboard: arrows move between hotspots, Enter opens the picker, Ctrl+Tab next app, F6 cycles rail / stage / apps / bottom bar.
Process: Slender.Settings.exe is separate from the tray, started on demand, exits on close. It reads/writes config.json; the tray watches config.json and publishes status.json.

Actions Ring overlay (tray, Direct2D layered window, no activation): opens at the cursor clamped on-screen (or monitor centre); geometry from src/Slender.Engine/Ring/RingLayout.cs, shared with the Settings preview/editor (Actions Ring page: appearance controls + live preview that doubles as the slot editor for the selected app).
