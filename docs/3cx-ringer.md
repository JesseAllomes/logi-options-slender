# 3CX ringer

Rings the mouse while a call is ringing in the 3CX web client in Edge. It buzzes the ringer cue again and again until the call is answered (anywhere), ends, you click any mouse button, or 90 seconds pass.

It is off until you set it up, and while it is not ringing nothing of it runs: no hook, no timer, no polling. The native host is a one-shot program that Edge starts for a single message and that exits at once.

## How it works

1. A small Edge extension runs only on your 3CX address. It watches for two signs of a ringing call:
   - The headset ringer. The 3CX client tells a USB headset base to ring through WebHID; the extension sees that report and works out the Ringer bit from the device's own report description. This works whether or not the 3CX window has focus, and gives a real start and stop.
   - The call notification (fallback). The 3CX client shows a notification that stays until you act on it, only while its window is not focused. The extension notes when one opens and closes. Push notifications that come through the browser's service worker are not seen.
2. The extension sends `ring-start` or `ring-stop`, and which signal fired (`hid` or `notification`), to the Slender native host.
3. The host passes it to the running Slender tray over the local buzz pipe (`!ring start hid`, `!ring stop notification`, see `docs/buzz.md`) and exits.
4. The tray plays the ringer cue until the ring ends. Ordinary buzzes are dropped while it rings, so nothing cuts it. A second start while ringing changes nothing.

## Privacy

No call data leaves the page. The extension never reads or sends caller names, numbers, titles or any page content. The only things it sends are "started", "stopped" and the signal name. It has one permission (`nativeMessaging`), runs only on the one address you give, and has no storage and no network access. Slender keeps the time and signal of the last ring start in `status.json` so Settings can show it; nothing else is recorded.

## Set up

1. Open Settings, Haptics page, "3CX RINGER".
2. Type your 3CX web address as `https://your-pbx-address` (https only, no path) and press **Set up**. Slender opens the extension folder.
3. In Edge open `edge://extensions`, turn on Developer mode, choose **Load unpacked** and pick that folder.
4. Pick the ringer pattern, strength and cycles, and press **Test** to feel it. A saved pattern from My patterns also sets the pause between rings.
5. Reload the 3CX tab (or restart the PWA). The status line reads "Set up — waiting for the first call" until the first call, then "Last ring signal: ... (headset)".

Set up writes, for the current user only (no admin):

- `%LOCALAPPDATA%\Slender\edge-ringer\extension` with the extension (manifest limited to your address),
- `%LOCALAPPDATA%\Slender\edge-ringer\Slender.RingerHost.exe`, a copy of the host, so a Slender update never fights a running host (the tray refreshes the copy at start when the shipped one is newer),
- the host manifest `com.slender.ringer.json` beside it,
- the registry value `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.slender.ringer` (default value) pointing at that manifest. A value found there before is saved and put back by Remove.

The extension ID is fixed by the public key in the manifest, so the host manifest allows exactly that one extension.

## Remove

Press **Remove** in the same section. It restores or deletes the registry value and deletes the `edge-ringer` folder. Then remove "Slender 3CX ringer" on `edge://extensions`.

## Troubleshooting

- **Status stays "waiting for the first call".** Check the extension is on and loaded from the folder Slender opened, the 3CX tab was reloaded after loading it, and Slender is running (the host cannot reach the pipe otherwise). Edge must be the browser running the 3CX client. If you changed the address, press Set up again and reload the extension.
- **The headset rings but the mouse does not.** The extension saw no ringer report. Calls then rely on the notification signal, which only appears while the 3CX window is not focused. Tell the project which headset you use.
- **It never stops.** A click on any mouse button ends it; the safety limit is 90 seconds.
- **Nothing buzzes at all.** Haptic feedback must be on (top of the page) and the Ringer buzz switch must be on. Settings' Test works through the same tray.
- **Setup says it could not copy the host.** A call was being handled at that moment; try again.
- **Edge says the extension is not allowed.** Your work policy must allow unpacked extensions and native messaging.
