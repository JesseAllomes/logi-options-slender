# Yealink WH64 headset

Slender reads the buttons on the WH64's USB base, keeps the Windows microphone mute and the headset's mute light in step, and
lets three buttons run any Slender action. It appears in Settings only after the headset has been plugged in once, greyed with
Remove device when it is not connected.

## What it opens

Four of the base's HID collections, all shared with other programs:

- the telephony collection (the private copy Slender can read): hook, mute, flash and reject
- the Teams button collection
- the standard telephony collection, input only: on this base mute, hook and reject arrive here (input report 2). Slender never
  writes to it, because Teams and the 3CX web client own its outputs. If both copies ever report the same press, it counts once.
- the vendor command collection, only while Read headset status is on (see below)

The remaining collection is never opened. Nothing is written to the headset's firmware or settings, ever.

## What it reads from the base

With Read headset status on (the default; `VendorReads` in config), Slender asks the base, over its vendor channel, for the
headset's battery, charging, docked, signal and in/out of range, and for the base's own settings. The Headset page shows them and
the tray's Yealink WH64 submenu shows battery and docked. After a short handshake (hello, version) the base's own handset
status is read, then one setting at a time (about fifty small exchanges). It reads at start, when the headset is plugged in, and
when the Headset page is opened (at most every few seconds). In between, the base pushes battery and signal itself and
Slender only listens. The settings list is read-only: change them in Yealink USB Connect.

What Slender can send is a fixed list of five read requests: hello, version, handset status, and the two forms of "read one
setting". Any other command is refused in code, so nothing that sets a value, resets, flashes, runs a shell, moves files, powers
off or updates firmware can be built, whatever the page or config says. With Read headset status off the vendor collection is
never opened and nothing is sent.

## What each button does

| Button | Slender |
|---|---|
| Mute | Toggles the Windows default microphone (Mute syncs Windows microphone, on by default). Mute changes made elsewhere in Windows are picked up too. |
| Hook | Runs the action you choose, on both pick up and hang up. Default: nothing, left to the phone app. |
| Reject | Runs the action you choose. Default: nothing, left to the phone app. |
| Teams button | Runs the action you choose. Default: nothing, left to the phone app. |
| Flash | Not exposed. |

3CX and Teams keep their own call control: Slender never answers or ends a call itself. A button left on "Leave to phone app"
does nothing in Slender, whatever the phone app does with it.

The tray menu has a Yealink WH64 submenu with the status and a Mute microphone check item, which flips the same Windows mute.

## Headset lights

Off by default and marked experimental. While it is off, Slender never writes anything to the headset, so the mute light only
changes by the headset's own behaviour. Turning it on lets Slender set the mute light to follow the Windows microphone. Only the
mute, off-hook, ring and hold lights on the telephony output report are ever addressable; ring and off-hook are not used.

## Stands down for Yealink USB Connect

If Yealink USB Connect is running when Slender starts or the headset is plugged in, Slender opens nothing, so the two never
compete for the device. It also sends no read request while USB Connect is running. Close Yealink USB Connect and replug the headset (or restart Slender) to hand the headset back.

## Memory and activity

The headset code runs only while the headset is plugged in. With it unplugged, or with the feature never used, no thread,
handle or buffer exists for it, and nothing polls: Windows tells Slender when the headset arrives or leaves. Changing Mute syncs
Windows microphone or Headset lights while the headset is connected reopens its link once so the change applies at once.

## Pictures

The Headset page shows the headset (mono or dual, Teams edition when the base's Platform setting is Teams) or the base, with a
callout for each control and light: the call or Teams button, busylight, boom arm mute and battery on the headset, and the touch
keys and lights on the base. Slender does not ship these pictures. It copies them from your own Yealink USB Connect install
(found through its uninstall entry) into its own picture folder, with a backup, the same way it keeps the mouse and keyboard
pictures. Refresh pictures takes a new copy; without USB Connect, Slender keeps the pictures it has, or draws a plain outline.
The callout positions are Slender's own.

## Remove device

Available when the headset shows as disconnected. Slender forgets the device and resets only the headset settings (buttons, mute
sync, lights) to their defaults, after keeping a backup of the settings file. Nothing in Windows changes.
