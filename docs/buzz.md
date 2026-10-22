# Buzz the mouse from other programs

Slender's tray listens on a local named pipe, `\\.\pipe\Slender.Buzz`. It is current-user only and never a network port. A
client writes one line: an alert name, or nothing. The tray plays that alert on the mouse's haptic motor.

- A name that matches one of your **Named alerts** (Settings, Haptics) plays that alert's buzz.
- An empty or unknown name plays the **Alert (no name)** buzz.
- Lines over 64 characters are ignored. At most about five buzzes per second play; the rest are dropped.
- Nothing plays while haptics are switched off (tray menu or Settings).

Alert names are letters, digits, `-` and `_`, up to 32 characters, and are not case sensitive.

## The command

```
Slender.Buzz.exe [name]
```

The installer puts it next to Slender: `%LOCALAPPDATA%\SlenderApp\current\Slender.Buzz.exe` (`tools/Slender.Buzz` builds it from
source). Call it by that full path, or add the folder to your PATH. It connects with a 1 second
timeout and writes the line. Exit code 0 means sent; 2 means Slender is not running (it prints a message and nothing else
happens, so a hook can ignore the failure).

## Claude Code hooks

In `settings.json` (user or project), buzz when Claude finishes or asks for you:

```json
{
  "hooks": {
    "Stop": [
      { "hooks": [ { "type": "command", "command": "Slender.Buzz.exe claude" } ] }
    ],
    "Notification": [
      { "hooks": [ { "type": "command", "command": "Slender.Buzz.exe claude" } ] }
    ]
  }
}
```

Add an alert called `claude` in Settings, Haptics, Named alerts to give it its own pattern. Use a different name per event
(for example `claude-done` for Stop and `claude-ask` for Notification) to tell them apart by feel.

## A build script

```bat
dotnet build && Slender.Buzz.exe build-ok || Slender.Buzz.exe build-failed
```

```powershell
dotnet test; if ($LASTEXITCODE -eq 0) { Slender.Buzz.exe tests-ok } else { Slender.Buzz.exe tests-failed }
```

## Excel VBA

```vba
Sub BuzzMouse(Optional ByVal alertName As String = "")
    On Error Resume Next
    Shell """C:\Tools\Slender.Buzz.exe"" " & alertName, vbHide
End Sub

Sub LongMacro()
    ' ... slow work ...
    BuzzMouse "excel"
End Sub
```

Use the full path to the exe you installed it at. `Shell` returns at once; the exe exits within a second.

## Protocol

One UTF-8 line ending in a newline. Settings' Test buttons use a second form, `!test pattern pulses spacingMs strength|-`,
which plays that exact cue whatever the on/off switches say. The 3CX ringer (`docs/3cx-ringer.md`) writes `!ring start|stop hid|notification` and
`!ring result answer|reject found|missing`. Other lines starting with `!` are ignored.
