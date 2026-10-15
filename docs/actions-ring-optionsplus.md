# Logi Options+ Actions Ring - feature inventory (for Slender parity)

Research date 2026-10-15. Status tags: CONFIRMED (source cited) or UNCERTAIN.

## Sources
- S1 support.logi.com "Actions Ring" (en-nz/17844647906967), undated; mentions support dates up to May 2026.
- S2 support.logi.com "What is the Actions Ring feature in Logi Options+?" (17859365804439), undated.
- S3 Logitech Sync Hub "Getting Started - MX Master 4" (hub.sync.logitech.com), undated, 2025+.
- S4 support.logi.com "Haptic Feedback - MX Master 4" (34335429816727), undated.
- S5 Release note Options+ 1.91.722586, 2025-06-10 (devicebase.net mirror): "Actions Ring FOLDERS".
- S6 logitech.com/en-us/software/actions-ring and /discover/a/master-your-mx-master-4 (2026 footer, undated).
- S7 xenospectrum MX Master 4 review (launch 2025-09-30).
- L1 local: C:/ProgramData/LogiOptionsPlus/depots/881241/88b73d28-.../ (Actions Ring "radial-menu-virtual-device" depot: manifest.json, core_metadata.json, defaults_slot.json, card_presets_virtual_device.json, available_card_tags_slot.json, strings/en-US.yaml)
- L2 local: .../881241/2334eaeb-... (MX Master 4 depot: haptic_config.json, default_configurations.json, core_metadata.json, recommendation_slots.json)
- L3 local: C:/Program Files/LogiOptionsPlus/data/strings/en-US.yaml and data/card_presets/card_presets_win.json
- L4 fixture: Slender.Tests/Fixtures/optionsplus-settings.json and optionsplus-macros.json
- Not inspected: the ring overlay UI itself (runs in Logi Plugin Service, C:/Program Files/Logi/LogiPluginService, compiled) and app.asar. Rendering details (theme, colours, animation, radii) are therefore UNCERTAIN.

## 1. Opening the ring
- Action "Show Actions Ring" (card id card_global_presets_show_radial_menu, macro type SYSTEM, system.action "SHOW_RADIAL_MENU", taskId 160) can be mapped to any customisable button of a compatible mouse/keyboard, or searched in the actions picker. CONFIRMED (S1, L1, L4).
- MX Master 4: the Haptic Sense Panel (slot mx-master-4-2b042_c416) triggers it; the action is also offered for buttons c83/c86/c195/c196. CONFIRMED (L2 recommendation_slots, L4; S3).
- Two selection gestures: (1) press and release the panel, ring stays open, hover a bubble, left-click; (2) press and hold, hover a bubble, release on it. CONFIRMED (S3).
- MX Creative Console: bottom-right Dialpad button (changeable); Craft keyboard Crown press. CONFIRMED (S1, L3).
- Other devices: DPI button can be set to Show Actions Ring; MX Master 3S, MX Keys S/Mini, MX Anywhere 3S, MX Mechanical, MX Keypad listed as compatible. CONFIRMED (S6, L3).
- Slender extension (m28, not in Options+): `ActionSpec.TapHold(Tap, Hold)` on any button or key (not the gesture button or thumb wheel), e.g. Sense Panel: quick press runs one action, holding 350 ms (a haptic tick) starts Hold. With Hold = Actions Ring the ring opens in hold-only mode at the threshold: releasing on a slot runs it, releasing anywhere else cancels. A ring "tap" (stay open) never happens through Press / Hold; it applies only to a plain Actions Ring binding. Not allowed inside a ring slot. Settings: More tab, "Press / Hold".
- Smart Actions can bind a trigger to a ring slot ("Select slot"/"Confirm slot"; string "Actions Ring, {SLOT_NAME}{NESTED_SLOT} is used", so nested folder slots are addressable). CONFIRMED (L1 strings).
- Ring appears at the cursor ("at the tip of your cursor"). CONFIRMED (L1 onboarding strings). Whether it is clamped to screen edges: UNCERTAIN.
- Dedicated fixed keyboard shortcut: none found. A keyboard key can be assigned Show Actions Ring (recommendation list). UNCERTAIN beyond that.
- Requires Logi Plugin Service; Options+ UI offers "Enable Logi Plugin Service". Win10+, macOS 13/14+, Options+ >=1.83 (Creative Console), >=2.04 (mice/keyboards). CONFIRMED (S1, L3).

## 2. Slots
- 8 primary bubbles, always present, "Slot 1".."Slot 8"; ids radial-menu-virtual-device-10000000_c408..c415. CONFIRMED (S1, S2, L1).
- Count fixed at 8; no evidence it varies by app. CONFIRMED-ish (S1/S2), variation UNCERTAIN.
- Config-screen marker positions (percent of a 1024x1024 image): c408 (43.8,6.7) top; c409 (63.4,15); c410 (71.5,34); c411 (63.5,54.1); c412 (44,62.2) bottom; c413 (24.2,54); c414 (17.3,34); c415 (24.2,14.9). So slots run clockwise from the top. CONFIRMED (L1 core_metadata). Real overlay geometry UNCERTAIN.
- Default global ring (Windows): c408 Cut (Ctrl+X), c409 Copy, c410 Paste, c411 Forward (mouse WIN_FORWARD), c412 Back (WIN_BACK), c413 Undo (Ctrl+Z), c414 Redo (Ctrl+Y), c415 Folder. CONFIRMED (L1 defaults_slot.json, L4 matches). Reviews mention other defaults (Explorer, screenshots, media, emoji, AI chatbots): UNCERTAIN (S7).
- Slot-allowed card tags (identical for all 8): MOUSE, BUTTON, KEY_OR_BUTTON, KEY_OR_BUTTON_ADVANCED_CLICK, MISC, BRIGHTNESS, FOLDER, ANALOG_CONTROL, KEYBOARD_SETTINGS. CONFIRMED (L1 available_card_tags_slot.json).
- Slot context menu: Copy Action, Copy Folder, Paste, Delete Action, Delete Folder, Customize. Confirm dialogs "Remove this action?" / "Remove this folder?". CONFIRMED (L1 strings).
- Customise flow: Options+ > Actions Ring icon > Layout > CUSTOMIZE RING; drag actions from the picker on the right onto bubbles; set name and icon. CONFIRMED (S1, S2).

## 3. Slot (action) types
Same card catalogue as buttons, filtered by tags above. CONFIRMED (L1, L3, L4).
- Keystroke (macro.type KEYSTROKE; keystroke {code (HID usage), modifiers [HID usages, e.g. 224=LCtrl], virtualKeyId, displayCharacter}, actionName e.g. "Ctrl + X"). CONFIRMED (L4).
- Mouse actions (type MOUSE, e.g. WIN_BACK, WIN_FORWARD), advanced click. CONFIRMED (L4, L3).
- System actions (type SYSTEM; about 41 presets: lock, sleep, shut down, task view, show desktop, screen capture, calculator, brightness, window min/max/close, language switch...). CONFIRMED (L3).
- Media (type MEDIA: play/pause, next/prev, volume up/down/mute). CONFIRMED (L3).
- Open application (APP / QUICK_LAUNCH), open file, open folder (OPEN_FILE_FOLDER, openFileFolder.isFolderMode; "BROWSE FOLDER", "Enter folder path"), open web page (OPEN_WEB_PAGE), text block (TEXT_BLOCK), screen capture, audio device. CONFIRMED in catalogue (L3); each being allowed in a ring slot is UNCERTAIN though S7 says files, folders and websites can be assigned.
- Adjustable (ANALOG_CONTROL) bubbles: "Adjust Volume" (macro MEDIA SET_CONCRETE_VOLUME, uiSettings.valueFormat "%.0f%%", action {actionId "volume", integrationId "system"}); brightness similar. CONFIRMED (L1, S1).
- Plugin actions from Logi Marketplace via Logi Plugin Service (Zoom, Spotify, Photoshop, Premiere, Illustrator, Lightroom, InDesign, Word, Excel, PowerPoint, Figma, Slack, Notion, Final Cut, DaVinci, Affinity). CONFIRMED (S1, S6, L3).
- Smart Actions (macros: INPUT_SEQUENCE and DELAY steps, see optionsplus-macros.json) via the Smart Actions plugin. CONFIRMED (S1, L1 strings).
- AI actions: Options+ has an "AI Prompt Builder" (L3) and S2's example folder is "AI Actions"; reviews mention ChatGPT/Perplexity/Gemini/Copilot shortcuts (S7). A distinct AI-prompt slot type: UNCERTAIN.
- Easy Switch (switch host) assignable. CONFIRMED (S6, S7, L2 strings).
- Each action has editable name and icon. CONFIRMED (S1, S2).

## 4. Folders
- Card attribute "FOLDER" (id card_global_presets_folder, name "Folder", tag PRESET_TAG_FOLDER, icons Folder.webp/Folder.svg under pipeline://88b73d28-24d8-47bd-84a2-c45cad5640d3/actions.bundle/). Default ring ships slot 8 (c415) as a Folder. CONFIRMED (L1, L4).
- Introduced Options+ 1.91 (2025-06-10) for MX Creative Console: "assign a FOLDER ACTION to a bubble ... sub-groups of actions". CONFIRMED (S5, L3).
- Capacity: nine supplementary sub-bubbles per folder; marketing cites up to 72 extra controls (8 x 9). CONFIRMED (S1, S2). Locally the config image defines nested context-menu positions for 8 slots, so 8 vs 9 is UNCERTAIN; keep configurable.
- Folder name and icon editable (examples "AI Actions", "Work Mode"). CONFIRMED (S1, S2).
- Levels: docs describe one level only. Folder inside folder not documented: UNCERTAIN, assume 1 level.
- Contents: actions from the same pool; "Slot Addition" string implies an add-slot affordance in nested editor. Exclusions UNCERTAIN.
- Editing safety: converting a folder to an action warns "Turn folder into an action? All the content actions will be deleted"; replacing: "Replace this folder?"; Copy Folder / Paste in context menu. CONFIRMED (L1, L3).
- Opening/navigation: selecting a folder bubble opens a secondary ring (S7: "another Actions Ring inside a ring"). How to go back (centre, Esc, right-click, release): UNCERTAIN, not documented.
- Stored form (fixture lines ~2708-2727): in profile "assignments" array: {card:{attribute:"FOLDER", icons:{icons:["Folder.png","Folder.svg"], uri:"pipeline://88b73d28-.../actions.bundle/"}, id:"card_global_presets_folder", name:"ASSIGNMENT_NAME_FOLDER", readOnly:true, tags:["PRESET_TAG_FOLDER"]}, cardId:"card_global_presets_folder", slotId:"radial-menu-virtual-device-10000000_c415", tags:["UI_PAGE_BUTTONS"]}. In the sanitised fixture it is the untouched default: no children, no custom name, no nestedCards. CONFIRMED. Where a populated folder keeps its children and custom name is UNCERTAIN. The generic card shape in the same file has "nestedCards" (map key -> card), "nestedCardsOrder" and "selectedNestedCard" (used by gesture/ONE_OF cards); best guess is FOLDER cards use nestedCards the same way. Unverified.
- Icons are referenced by name pair plus a pipeline:// URI of the owning depot, not embedded.

## 5. Per-app rings vs global
- Contextual: the ring shows the active app's profile if one exists, otherwise global (profile "PROFILE_NAME_DEFAULT", id 420fd454-0c36-499d-bde4-146823b16147). CONFIRMED (S1, L1 strings "Adapts to your apps").
- App profiles come from Marketplace plugins (Photoshop, Zoom, Figma...) or user-created; strings "Profile already added", "App is not installed"; plugin profiles listed first. Preset profile art for Illustrator, Lightroom, Premiere, Photoshop, Global. CONFIRMED (S1, L1).
- In the fixture only the global profile has c408..c415 assignments; per-app ring storage UNCERTAIN.
- "Switch Profile" action can jump to another app's profile or General. CONFIRMED (L3).
- Templates: plugin-supplied default actions per app; recommendation_slots.json suggests actions per button per app. CONFIRMED (L2).

## 6. Appearance
- Only documented option: Size Small / Medium (default) / Large (Options+ > Actions Ring > Settings). CONFIRMED (S1, L3 ACTIONS_RING_APPEARANCE_*).
- Theme, colour, opacity, position mode, animation, label display: no settings found. UNCERTAIN (probably none).
- Icons PNG/SVG/WebP per action; tooltip names in config. Overlay label behaviour UNCERTAIN.

## 7. Selection mechanics
- Hover highlights; left-click runs; hold mode releases on bubble. CONFIRMED (S3, L1).
- Adjustable actions: hover and scroll wheel / thumb wheel / Creative Console dial, or click-and-drag left/right to show an adjustment bar. CONFIRMED (S1, L1).
- Cancel behaviour, keyboard navigation, accessibility, multi-monitor/DPI: not documented. UNCERTAIN.
- In-place "edit ring" overlay: none found; editing is in the Options+ window (Customize Ring). Local string "CONFIGURE ACTIONS RING" only. UNCERTAIN beyond that.

## 8. Haptics (MX Master 4)
- Event source "actions_ring": hover_on_action -> waveform 4 (subtle_collision); slider_endpoint_reached -> 4; button_action_run -> 1 (damp_state_change). CONFIRMED (L2 haptic_config.json).
- Toggle actions_ring.isHapticsEnabled (default true) next to gestures and switch_screens; global isGlobalHapticsEnabled; hapticFeedbackLevel (default 60 in default_configurations, user fixture 45); four named intensities Subtle/Low/Medium/High; auto-off under 10% battery. CONFIRMED (L2, L3, S4, L4).
- Panel force sensitivity set in "Point, Scroll and Press" (forceSensing card). CONFIRMED (L3, L4).

## 9. Import/export, limits, misc
- Copy/paste actions and folders: CONFIRMED (L1). Ring import/export: none found, UNCERTAIN (cloud backup exists generally).
- Limits: 8 bubbles, 9 per folder. CONFIRMED (S1, S2).
- Installer flag enables/disables Actions Ring. CONFIRMED (L3).

## 10. Slender parity checklist (suggested)
1. Trigger bound to button/key; support tap-then-click and hold-release-on-bubble.
2. 8 fixed bubbles clockwise from top; default Cut/Copy/Paste/Forward/Back/Undo/Redo/Folder.
3. Action types: keystroke, mouse nav, system, media, open app/file/folder/URL, text, macro, volume slider, plugin.
4. Folder: one level, up to 9 children (configurable), name and icon, copy/paste/delete confirm, convert warns of data loss.
5. Per-app profile with fallback to global; Switch Profile action.
6. Size Small/Medium/Large.
7. Slider actions via scroll or drag.
8. Haptic hooks only if hardware supports it.
9. Pick sensible behaviour for undocumented: cancel (Esc/centre), folder back, keyboard nav, multi-monitor/DPI.
