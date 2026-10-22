# Yealink WH64 headset

Slender reads the buttons on the WH64's USB base, keeps the Windows microphone mute and the headset's mute light in step, and
lets three buttons run any Slender action. It appears in Settings only after the headset has been plugged in once, greyed with
Remove device when it is not connected.

## What it opens

Only two of the base's HID collections, both shared with other programs:

- the telephony collection (the private copy Slender can read): hook, mute, flash and reject
- the Teams button collection

The collection Teams and the 3CX web client use for call control, and the vendor command collection, are never opened. Nothing
is written to the headset's firmware or settings, ever; no feature or command report is sent.

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
compete for the device. Close Yealink USB Connect and replug the headset (or restart Slender) to hand the headset back.

## Memory and activity

The headset code runs only while the headset is plugged in. With it unplugged, or with the feature never used, no thread,
handle or buffer exists for it, and nothing polls: Windows tells Slender when the headset arrives or leaves. Changing Mute syncs
Windows microphone or Headset lights while the headset is connected reopens its link once so the change applies at once.

## Remove device

Available when the headset shows as disconnected. Slender forgets the device and resets only the headset settings (buttons, mute
sync, lights) to their defaults, after keeping a backup of the settings file. Nothing in Windows changes.
