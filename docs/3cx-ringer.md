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

## What starts the buzz

The 3CX tab has a "Buzz the mouse when a call rings" choice:

- **The headset base rings.** Slender listens for the Yealink WH64 base ringing. It works with any phone app and needs no setup. The base signals a ring with a quick burst of key presses on its media-key interface; Slender only counts that burst, and reads nothing else from it. Volume key presses are never mistaken for it. The interface is opened only while this choice is on.
- **3CX rings in Edge.** The Edge extension described here starts the buzz. It needs the setup below.
- **Either one** (the default). Whichever signals first starts the buzz, and the other signal while it rings changes nothing. The answer and reject buttons keep working with the extension in every case.

The last ring signal shown on the tab says "headset base" when the base started it.

## Answer or reject with a mouse button

In Settings, Haptics, "3CX RINGER", **Answer with** and **Reject with** each take Middle click, Forward, Back or Off (the default). Only those three, because Slender sees them through its mouse hook and can hold one back from its normal action. The mouse's other buttons are not offered.

- While a call is ringing (the mouse is buzzing), a press of the chosen button is taken: it does not reach its normal binding or the app under the pointer, and its release goes with it. The ring ends and the extension clicks the 3CX web client's Answer or Decline control for that call.
- When nothing is ringing the buttons do what they always do. A press of any other button still just ends the buzz.
- It needs the ringer to be buzzing: haptic feedback and the Ringer buzz switch on. If the Ringer buzz is off there is no ring to take the button from.
- If the extension finds no Answer or Decline control it tells Slender, which writes `3CX answer control not found on the page` (or reject) to its log. The 3CX web client can rename its controls; the finder tries aria-label, title, short button text, test id, id and class, and skips hidden or disabled buttons.

How the command gets from Slender to the extension: the native host is still one-shot and Slender cannot call it, so the extension polls it.

1. On a ring start with a ring button set, the tray opens a second local pipe, `Slender.RingCmd` (current user only, never a network port). Nothing is created for a ring when both pickers are Off.
2. The extension, in the 3CX tab only and only while that tab's ring signal is on, asks the host `poll` once a second. Each poll starts the host for a moment; it reads one line from the pipe and answers `none` (keep polling), `answer`, `reject`, or `off` (no pipe: Slender is not listening, so the extension stops polling for this ring).
3. A press puts `answer` or `reject` on the pipe. The next poll picks it up (within a second), the pipe closes, and the extension clicks the control, but only if the ring signal is still on in that tab. It reports found or not found back (`!ring result answer found`, see `docs/buzz.md`).
4. The pipe also closes when the ring stops with no command waiting, or after 100 s. The extension's timer stops at the same points (and at 95 s). No process, port, pipe or timer exists between rings.

A port kept open with `connectNative` was the alternative. It would give instant delivery, but it keeps a host process alive for the whole ring and the host would still have to poll the pipe itself, so a once-a-second poll is simpler and fails more safely.

The extension version is now 1.1.0. After updating Slender, press **Set up** again and reload the extension in `edge://extensions` so Edge picks up the new files.

## Privacy

No call data leaves the page. The extension never reads or sends caller names, numbers, titles or any page content. The only things it sends are "started", "stopped", the signal name, a poll, and whether the Answer/Decline click found its button. It reads only the labels of the page's buttons to find those two. It has one permission (`nativeMessaging`), runs only on the one address you give, and has no storage and no network access. Slender keeps the time and signal of the last ring start in `status.json` so Settings can show it; nothing else is recorded.

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
