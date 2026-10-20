# Slender Excel add-in (opt-in)

Replaces Logitech's Excel add-in. Off by default. Settings > General > Excel add-in turns it on and off.

## What it is

An Excel-DNA `.xll` (64-bit, .NET 10) built by `src/Slender.ExcelAddin`. Chosen over VSTO (.NET Framework, ClickOnce trust, needs Office tools), a COM add-in (CLSID registration, shim for .NET) and Office.js (needs a web page host, so a network listener). An .xll is one file that Excel opens from an HKCU `OPEN` value: per user, no admin, no COM registration. It needs the .NET 10 runtime, which Slender's installer already provides.

## Safety

- No network port. The add-in opens one named pipe, `Slender.Excel.<excel pid>`, current user only (`PipeOptions.CurrentUserOnly`); Slender's client also demands the same.
- Idle: no ribbon, timers or worksheet functions. It only acts when a request arrives; without Slender there is never a client.
- Every step is time-boxed: Slender waits 250 ms per call; the add-in waits 180 ms for Excel's main thread and answers `busy` if Excel is in edit mode or a dialog (the request is then cancelled so it cannot run late and double up). After a failure Slender leaves that Excel alone for 3 s.
- Macro names are restricted to letters, digits, `_ . ! '`.

## What it does

Next/previous visible sheet (hidden skipped), zoom steps, next table style, next chart type, run a named macro (`ActionSpec.ExcelMacro`, config `{"type":"excelMacro","name":"Module1.Tidy"}`), and the thumb wheel: cells selected scrolls columns, a chart or shape selected steps sheets.

When the foreground is Excel and the add-in answers, it is used; otherwise Slender falls back to today's out-of-process COM and key behaviour (table style, chart type and macros have no fallback).

## Turning it on

Close Excel, flip the switch. Slender copies `ExcelAddin\Slender.ExcelAddin64-packed.xll` to `%LOCALAPPDATA%\Slender\ExcelAddin` and writes `HKCU\Software\Microsoft\Office\16.0\Excel\Options\OPEN[n]` = `/R "<path>"`. Excel loads it at next start; it may ask to enable the unsigned add-in. Turning it off removes that value (later OPEN values move down so Excel still reads them) and deletes the copy. To remove by hand: delete the OPEN value that names `Slender.ExcelAddin64-packed.xll`.

After a Slender update the copy Excel opens is refreshed when it differs from the shipped one (size, then hash only if sizes match but times differ). It happens at tray start and when Settings opens the General page, only while Excel is closed; the new file is copied to a temporary name and moved over the old one, and the registry is not touched. If Excel has it loaded the refresh waits for the next try.

To run a macro from a control: picker > Excel tab > type the macro name (e.g. `Book1.xlsm!MyMacro`). Names are checked as you type; it needs the add-in on.

Not verified in a real Excel yet: the first run needs a manual check on a machine the user chooses.
