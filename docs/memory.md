# Memory audit (wt-mem, 2026-10-15)

Goal: find every realistic way to make Slender use less memory, measure it, and rank it. Nothing here is merged; the
experiment code on `wt-mem` exists only to take the measurements.

## Terms

- **Commit** = private bytes (`PrivateMemorySize64`; same number as `PagedMemorySize64` on Windows). Memory only this
  process can use, whether it is in RAM or in the page file. This is the honest cost.
- **Private WS** = the part of commit that is in RAM now. Task Manager's "Memory" column shows this.
- **WS** = working set: private WS plus shared pages (DLL code, mapped files). Shared pages are counted in every process
  that maps them, so WS overstates what a process really costs.
- All numbers are MB, median of 2–3 runs, sampled after the process has sat idle (30 s unless stated).

## Ranked options

Savings are for the always-running tray unless the row says Settings. "Measured" means a before/after run in this
worktree; "estimate" says how it was worked out.

| # | Option | Saving | Effort | Risk | Recommendation |
|---|---|---|---|---|---|
| 1 | **Stop LogiPluginService from starting** (Options+ leftover; see below) | **~650 MB WS, ~1.2 GB commit** (measured on the running process) | Low | Low (reversible) | **Do it at switch-over.** Biggest item by far. |
| 2 | **Cap the tray's gen0 budget**: runtimeconfig `System.GC.Gen0MaxBudget` = 1 MB | **~11–13 MB commit, ~10 MB private WS** under a load that reproduces the real tray (23.7 → 12.3 commit at 5 min). 0 at idle. | Trivial (one csproj item) | Low: ~20 extra gen0 GCs per 5 min, each well under 1 ms | **Do it.** `System.GC.Gen0Size` and `GCgen0size` in runtimeconfig are silently ignored; only `Gen0MaxBudget` works there. |
| 3 | **Replace WinForms with a raw Win32 tray** (Shell_NotifyIcon + TrackPopupMenu) | **WS −16 MB, commit −3 MB, private WS −2 MB** (prototype, measured: 49.8 → 33.5 WS, 11.5 → 8.4 commit) | Medium (~300 lines; menu, balloon, UI-thread marshalling) | Low–medium | **Do it**, mainly because it is the prerequisite for #4. |
| 4 | **Publish the tray NativeAOT** (needs #3, source-generated JSON, and an ExcelBridge rewrite) | **Estimate: WS ~33 → ~15–20 MB, commit ~8 → ~4–6 MB** on top of #3 | Medium–high (see blockers) | Medium (Excel COM rewrite) | **Do it after #2 and #3**, once a C++ linker is available. Could not be measured here: no MSVC linker on this PC. |
| 5 | **Settings: framework-dependent .NET** (`SelfContained=false`; tray already uses the shared runtime) | **~13.5 MB physical RAM while Settings is open** (13.7 MB of .NET runtime pages shared with the tray; process's own commit unchanged) | Trivial | Low: needs the .NET 10 Desktop runtime the tray already needs | **Do it.** Install size: 217 MB self-contained vs 40 MB with both runtimes framework-dependent (.NET alone not measured). |
| 6 | **Settings: framework-dependent Windows App SDK** (`WindowsAppSDKSelfContained=false`) | 0 for Settings' own memory (measured); shares WinUI pages only if another app on the PC uses WinAppSDK 1.8 at the same time. Startup +50 ms. | Low (runtime 1.8 must be installed; it is) | Low–medium (an extra dependency to install) | Optional. Mainly an install-size win (217 → 40 MB with #5). |
| 7 | **Settings: trimming** | WS −21 MB, commit −3 MB, private WS ±0 (measured); startup +200 ms unless combined with ReadyToRun | Medium (source-generated JSON for config/status/UI state/layout; Engine's ExcelBridge warnings) | Medium (trim breakage only shows at run time) | Later, together with #8. |
| 8 | **Settings: NativeAOT** (WinAppSDK 1.8 supports it) | Not measurable here (no linker). Expect the managed/JIT share to go (a few MB of commit, ~10–20 MB WS); the WinUI/D3D/driver share (most of the 82 MB private WS) stays. | Medium–high | Medium | Low priority: Settings is short-lived and exits on close. |
| 9 | **Popover: tray menu only (no WinUI popover)** | Avoids **60–80 MB** each time the popover would be open (WinUI floor measured at 62 MB private WS / 82 MB commit) | — | — | **Recommended.** See "Popover decision". |
| 10 | Tiered compilation off (`TieredCompilation=false`) | commit −0.6 MB, WS ±0 (measured) | Trivial | Low (slower first calls; not noticeable in a tray) | Optional; not worth it alone. Moot with NativeAOT. |
| 11 | Fewer threads (merge engine/keyboard workers, hook + foreground watcher) | ~0.08 MB commit per thread (measured: 4 idle threads = +0.3 MB) | Medium | Medium (hook latency must stay isolated) | **Don't.** Not worth the risk. |
| 12 | Periodic `GC.Collect(Aggressive)` when idle | −0.8 MB commit (measured, one-shot). Mostly covered by #2. | Trivial | Low | Skip if #2 is done. |
| 13 | Periodic `EmptyWorkingSet` / `SetProcessWorkingSetSize(-1,-1)` | **Cosmetic.** WS 53 → 10 MB, then back to 26 MB within 5 min; commit unchanged (measured). | Trivial | Low, but costs page faults (CPU, disk) when pages come back | **Don't.** It only moves pages to the standby list; the number in Task Manager drops, the real cost does not. |
| 14 | Concurrent GC off, Server GC off, `GCConserveMemory`, `GCHeapHardLimit`, segments GC (`clrgc.dll`), `GCHeapAffinitizeMask` | All within ±0.7 MB (noise) at idle and under load (measured) | — | — | **Don't bother.** Server GC is already off for WinExe; affinitize only applies to Server GC. Concurrent GC's background thread is created lazily, so turning it off saves nothing measurable. |
| 15 | `UseSystemResourceKeys` | ≈0 without trimming (it only skips loading exception message resources) | — | — | Don't bother (measured; see raw numbers). |
| 16 | ReadyToRun for the tray's own assemblies | ≈0 memory (see raw numbers); slightly faster start | Trivial | Low | Optional. |
| 17 | Trimming the WinForms tray | Not possible: `NETSDK1175 Windows Forms is not supported or recommended with trimming enabled` (confirmed) | — | — | Only reachable through #3. |
| 18 | W^X off (`DOTNET_EnableWriteXorExecute=0`) | **Not a saving.** WS ±0; commit **+7 MB** (measured), because the JIT's loader heaps move from shared to private accounting | — | Lower security | Don't. Useful only to show that ~7 MB of the tray's real cost is hidden from "private bytes" today. |

Settings image decode sizes are already right (every `BitmapImage` sets `DecodePixelWidth/Height` to the displayed size),
there is no render cache, and closing the window ends the process (verified: every measured run exited cleanly on
`WM_CLOSE` and wrote its window state). Fonts are three static TTFs of 59–122 KB; nothing to gain there.

## Recommended plan

1. **Switch-over (no code):** stop the Options+ updater service and LogiPluginService from starting (reversible steps
   below). Frees ~650 MB WS / ~1.2 GB commit on this PC today.
2. **Tray, one line:** `<RuntimeHostConfigurationOption Include="System.GC.Gen0MaxBudget" Value="1048576" />`. Expect the real tray to drop
   from ~23 MB to ~11–13 MB commit.
3. **Settings, one line:** `SelfContained=false` (keep WinAppSDK self-contained unless the install size matters).
4. **Popover:** keep the tray menu; do not build a WinUI popover. If a richer surface is wanted later, put it in the
   Win32 tray process (native menu or a small owner-drawn Win32 window), never in a second WinUI process.
5. **Tray rewrite (bigger):** replace WinForms with the Win32 tray (prototype in `tools/mem/Win32Tray`), move JSON to
   source generation, rewrite `ExcelBridge` without late-bound COM, then publish NativeAOT. Target: ~15–20 MB WS,
   ~4–6 MB commit for the whole tray.

Not recommended: working-set trimming, thread merging, GC mode flags, W^X off.

## The real tray (read-only)

`Slender.Tray.exe` from the main checkout (built 10:44 from `bf6ca46`, framework-dependent Release, devices connected),
measured with `Get-Process` and a VMMap snapshot (`vmmap64.exe -p <pid> out.csv`, read-only):

| Uptime | WS | Private WS | Commit | Threads | CPU used |
|---|---|---|---|---|---|
| 49 min | 74.4 | — | 23.8 | 17 | — |
| 57 min (VMMap) | 78.4 | 17.7 | 22.6 | 15 | 7.8 s |
| ~1 h 40 min | 67.0 | — | 19.7 | — | — |

Where the 22.6 MB commit goes (VMMap, KB): GC heap 11,032 (gen0 4,095, gen1 708, gen2 6,148), native heap 6,084,
image pages written by the loader 3,384, private data 1,212, thread stacks 604 (≈36 KB per thread), page tables 116.
On top of that, ~18 MB of JIT loader heaps (10.1 MB of it in RAM) are mapped twice for W^X and therefore counted as
"shareable", even though no other process uses them.

The measurement tray (same code, no devices) idles at 11.5 MB commit with a 0.55 MB live GC heap. Adding a synthetic
64 KB/s of short-lived allocations (`SLENDER_ALLOC=64`) reproduces the real tray: 26.1 MB commit at 5 minutes, with the
GC holding 16.9 MB committed for 0.6 MB of live objects. The default gen0 budget on this CPU (Core Ultra 7 255H, 24 MB L3)
is roughly 10 MB, so the tray grows its gen0 to that size before it ever collects. That is what option #2 fixes.

`Process.GetProcessesByName` (the Options+ check) was suspected and ruled out: ~3 KB managed allocation and ~6 ms CPU per
call on this PC (613 processes, 9,495 threads).

## Shipping the gen0 cap

The tray is started from the Run key, so an environment variable is not an option; it must go in runtimeconfig.json.
Tested on copies of the build with only runtimeconfig.json edited, under the 64 KB/s load, 2 runs, 5 minutes:

| Setting | Commit at 5 min | Private WS | GC committed / live (KB) | gen0 GCs in 5 min | Works? |
|---|---|---|---|---|---|
| none (default) | 23.7 | 19.1 | 13,012 / 657 | 1 | — |
| env `DOTNET_GCgen0size=100000` (reference) | 12.6 | 8.5 | 2,196 / 779 | 20 | yes, but env only |
| runtimeconfig `System.GC.Gen0Size: 1048576` | 23.6 | 19.2 | 13,012 / 659 | 1 | **no, ignored** |
| runtimeconfig `GCgen0size: "0x100000"` | 23.2 | 19.1 | 13,012 / 658 | 1 | **no, ignored** |
| runtimeconfig `System.GC.Gen0MaxBudget: 1048576` | **12.3** | **8.5** | 2,260 / 790 | 20 | **yes** |
| runtimeconfig `System.GC.HeapHardLimit: 33554432` | 13.6 | 10.1 | 3,860 / 664 | 8 | yes, but an OOM risk on any spike |

At idle (no load) `Gen0MaxBudget` changes nothing (11.8 vs 11.7 commit), so it has no downside when the tray is quiet.

Ship it as (tray csproj; the same item works for a NativeAOT build, where it is compiled in):

```xml
<ItemGroup>
  <!-- Cap gen0 at 1 MB: the default budget follows the L3 size (~10 MB here) and the tray holds it committed. -->
  <RuntimeHostConfigurationOption Include="System.GC.Gen0MaxBudget" Value="1048576" />
</ItemGroup>
```

Check after deploying: the real tray should settle around 11–13 MB commit instead of ~23 MB.

## Popover decision

| Option | Extra memory while open | After closing | Measured how |
|---|---|---|---|
| (a) WinUI popover in its own process (or Settings in a compact mode) | **62 MB private WS, 82 MB commit, 146 MB WS** for the smallest possible WinUI 3 window (title, two lines, four toggles, a button). Mica on or off makes no difference. Settings forced to a small size still costs its full ~82 MB private WS (it clamps to its 1040×640 minimum and builds the whole UI). | Freed (process exits) | `tools/mem/WinUiMini`, 2 runs |
| (b) Lightweight popover inside the tray, WinForms | **+1.1 MB commit, +2.8 MB WS** while shown | **Not freed**: +1.6 MB commit, +4.5 MB WS stay after `Close()`/`Dispose()` (code, GDI+ fonts, control classes stay loaded) | `SLENDER_POPOVER=show/hide`, 3 runs |
| (b') Same inside a Win32 tray | Estimate ≤0.5 MB (a native window with a few child controls; no managed UI framework) | Mostly freed | Estimate from the Win32 prototype's 3.7 MB private WS floor including icon and menu |
| (c) No popover, tray menu only | 0 (native menu built on demand) | 0 | — |

**Recommendation: (c)** now, with **(b')** as the upgrade path if the design needs more than a menu. Option (a) adds
60–80 MB every time it opens and a ~0.8 s cold start for a surface the user glances at; the locked "unfold into the
settings window" design would mean either keeping that WinUI process alive (a permanent 60–80 MB) or paying the start-up
on every click. Unfolding can still be honoured by having the tray menu's "Settings…" open the full window.

## Settings app (WinUI 3)

| Variant | WS | Private WS | Commit | Startup to first populated frame (median of 4) | Install size |
|---|---|---|---|---|---|
| Self-contained .NET + WinAppSDK (current) | 192.5 | 80.5–81.5 | 106.6–107.9 | 755 ms | 217 MB |
| WinAppSDK framework-dependent | 192.9 | 81.5 | 106.5 | 806 ms | 117 MB |
| Both framework-dependent | 192.4 | 80.9 | 105.9 | 831 ms | 40 MB |
| Trimmed (self-contained) | 171.5 | 81.9 | 103.8 | 953 ms | 125 MB |
| ReadyToRun (self-contained) | 191.1 | 82.1 | 114.1 | 713 ms | 261 MB |

(60 s samples; 30 s samples agree within 2 MB.) Where Settings' 112.7 MB commit goes (VMMap at 30 s): native heap 57 MB
(XAML, D2D/DWrite, WIC), private data 34.5 MB (GPU driver and DirectComposition surfaces), image pages 16.3 MB (of which the
Intel GPU driver `igc64.dll` and `igd10umt64xe.dll` are ~11 MB), managed heap 3.8 MB (+14.8 MB of W^X loader heap counted
as shareable), stacks 1 MB. Managed code is a small slice; the WinUI/DirectX stack is the bulk, which is why trimming and
NativeAOT can only shave the edges.

Framework-dependent sharing (VMMap, tray running): self-contained Settings shares 0 KB of .NET runtime pages with other
processes; framework-dependent Settings shares 13,684 KB of them (total shared image WS 56.7 → 70.2 MB).

Trimming needs code changes before it is safe: reflection-based `System.Text.Json` is disabled when trimming
(`JsonConfigStore`, `StatusJson`, `UiState`, `AssetCache` all use it; the first trimmed run failed to save its window
state with "Reflection-based serialization has been disabled"), and `ExcelBridge` (in the shared Engine) produces IL2075/
IL2050 warnings that `TreatWarningsAsErrors` turns into build errors. The measured trimmed build used
`JsonSerializerIsReflectionEnabledByDefault=true` and `TreatWarningsAsErrors=false` only to get numbers.

NativeAOT (`PublishAot=true`) stops at `Platform linker not found ... Desktop Development for C++ workload`.

## NativeAOT blockers (tray and Settings)

Found by publishing the Win32 prototype with `PublishAot=true` (the compiler analyses the code before it needs the linker):

- `JsonConfigStore.cs(71,89)`, `StatusJson.cs(20,27)`: IL2026/IL3050, reflection JSON. Fix: a `JsonSerializerContext`
  with `[JsonSerializable]` for the DTOs, pass its `JsonTypeInfo`.
- `ExcelBridge.cs(52,107,114,117)`: IL2075, `Type.InvokeMember` on an Excel COM object (late-bound IDispatch). Built-in COM
  interop is not available under NativeAOT or trimming (`BuiltInComInterop.IsSupported=false`). Fix: call
  `IDispatch::GetIDsOfNames`/`Invoke` through a `[GeneratedComInterface]` IDispatch definition (or `ComWrappers`), or rely
  on the existing `WM_HSCROLL` fallback only.
- `ExcelBridge.cs(122)`: IL2050, `AccessibleObjectFromWindow(..., out object)` uses COM marshalling. Fix: take the raw
  `nint` and wrap it with `ComWrappers`.
- WinForms: not supported by NativeAOT or trimming; only the Win32 tray (#3) is AOT-able.

NativeAOT estimate method: VMMap of the Win32 prototype without the engine (bare CoreCLR + icon: 25.8 MB WS, 6.0 MB commit)
shows what NativeAOT removes: `System.Private.CoreLib` image (4.9 MB WS), `coreclr.dll` (2.0), `clrjit.dll` (1.7), JIT
loader heaps (2.4 MB shareable + 0.8 MB private), host DLLs. It adds the compiled code actually used (~2–3 MB image).
The Windows DLLs (ntdll, user32, shell32, combase, windows.storage: ~6 MB WS, shared) remain. Result: floor ~12–15 MB WS,
~3–4 MB commit; the engine adds ~1–2 MB commit. The GC is the same, so #2 still applies.

## Threads in the tray

The real tray runs 15–17 threads: UI, `Slender.MouseHook`, `Slender.ForegroundWatcher`, `Slender.ExcelSta`,
`Slender.Engine` and `Slender.Keyboard` workers, finalizer, tiered-compilation worker (transient), thread-pool workers
(the HID reads block a pool thread for up to 250 ms per device), and a few OS pool threads. Each costs ~36 KB of committed
stack in practice (VMMap: 604 KB for ~17 threads) plus a TEB; measured +0.3 MB commit for 4 extra idle threads. Merging is
possible (the engine and keyboard workers could share one queue; the foreground watcher's WinEvent hook could live on the
mouse-hook thread's message loop; the Excel STA worker could start on first use), but the whole saving is ~0.2–0.3 MB and
the LL hook must keep its own thread so a slow consumer can never delay mouse input. Not worth it.

One CPU (not memory) note: the HID read loop wakes every 250 ms per device and runs each read on a thread-pool thread
(`Task.Run` around a blocking `WaitAny`). The real tray used 7.8 s CPU in 57 min. Overlapped I/O with a completion
callback (or `ThreadPool.BindHandle`) would remove both the polling and the blocked pool threads.

## Logitech processes (describe only; nothing was changed)

### LogiPluginService

- What it is: `C:\Program Files\Logi\LogiPluginService\LogiPluginService.exe`, MSI product "Logi Plugin Service"
  6.4.2.3414 (`{704B6BE1-0C4F-4DCF-AF1C-345E5A7CB182}`), a Loupedeck-based .NET plugin host (WPF, WinForms, SkiaSharp,
  OpenTK, WebView2) with a child `LogiPluginServiceExt.exe`. It hosts Options+ plugins: LogiSmartActions, Actions Ring app
  actions, HapticMouseHover, BetterClick, Microsoft Teams, PowerPoint, Word, Spotify, and the old `LogiExcelAddIn` data.
- Now: **650 MB WS, 1,211 MB commit, 55 threads** (+19.8 MB WS for the Ext child), started 08:15 today. Options+ was quit at
  ~10:43, and since then its SmartActions plugin has been retrying the Options+ agent connection
  (`LogiLocalConnection Options+ client : Client Connect() Error: The operation has timed out`, attempt 190 at 12:03,
  10 attempts every 3.5 min). Without the Options+ agent it does nothing useful.
- What breaks if it does not run: Options+ Smart Actions, the Actions Ring's app-specific actions, haptic hover feedback,
  and the Teams/Office/Spotify plugin actions, **all of which only work while Options+ runs anyway**. Slender does not use
  it. The Options+ Excel VSTO add-in talks to the Options+ agent, not to this service.
- How it starts: there is no Run key, Startup-folder entry, scheduled task or service for it. Its parent process (PID 14444)
  no longer exists; it is started by the Options+ agent at logon (UNCONFIRMED: inferred from the missing autostart entries
  and the parent having exited when Options+ was quit).
- Reversible ways to stop it starting:
  1. Stop Options+ starting (next section); then nothing launches it. Undo: re-enable Options+.
  2. End it for this session: `Stop-Process -Name LogiPluginService, LogiPluginServiceExt`. Undo: start Options+ (or run the
     exe).
  3. Uninstall "Logi Plugin Service" (`msiexec /x {704B6BE1-0C4F-4DCF-AF1C-345E5A7CB182}`). Undo: reinstalling Options+
     reinstalls it. Heavier; only if (1) does not stop it.
  4. Trim-OptionsPlus with `smartactions` and `actions-ring` off might stop Options+ from launching it at all
     (UNCONFIRMED; the switch-over runbook already asks to check the process list afterwards).

### OptionsPlusUpdaterService

- Service `OptionsPlusUpdaterService`, display name **"Logi Options+"**, `logioptionsplus_updater.exe --run-as-service`,
  LocalSystem, **Automatic**, no triggers, restart-on-failure (it "terminated unexpectedly" twice at 10:43 today when Options+
  was quit and was restarted). Currently Stopped. It is the Options+ updater and, by all signs, the launcher that starts the
  agent in the user session (UNCONFIRMED).
- Once Slender replaces Options+: set it to Manual (keeps manual updates possible) or Disabled. Elevated:
  `Set-Service OptionsPlusUpdaterService -StartupType Manual` (or `Disabled`); undo:
  `Set-Service OptionsPlusUpdaterService -StartupType Automatic; Start-Service OptionsPlusUpdaterService`.
  Also remove/disable the HKLM Run entry "Logi Download Assistant" (Task Manager > Startup apps) if the download assistant is
  not wanted; undo by re-enabling it there.
- Effect on memory: the service itself is ~42 MB WS while running (baseline.md); stopping it mainly matters because it
  starts the agent (262 MB) and, through it, LogiPluginService (650 MB).

## Method

- Worktree `slender-wt-mem` (branch `wt-mem`, base `820c394`), .NET SDK 10.0.402, runtime 10.0.13, Windows 11
  10.0.26300, Core Ultra 7 255H (16 logical, 24 MB L3), 31.5 GB RAM.
- **Measurement mode** (`SLENDER_DATA_DIR` set): every Slender file (config, status, logs, assets, window state) goes to a
  private folder; the tray skips the single-instance mutex, uses a HID factory that finds no devices, gives the engine a
  no-op hook, installs a real but handler-less `WH_MOUSE_LL` hook (so the hook thread is counted and every event passes
  through), and uses an in-memory Run key. Settings uses private mutex/event names, shows its window without activation,
  off all monitors and out of the taskbar/Alt+Tab (`IsShownInSwitchers=false`), and writes the time from process start to
  the first frame after the window is populated. Without `SLENDER_DATA_DIR` both apps behave exactly as before.
- Load switches (tray): `SLENDER_CHURN=2` copies the connected-device read pattern of `WindowsHidTransport` (two loops of
  `Task.Run` + `WaitAny` with a 250 ms timeout); `SLENDER_ALLOC=64` adds 64 KB/s of short-lived 256-byte objects with a
  small long-lived ring; `SLENDER_GCLOG=1` logs `GC.GetGCMemoryInfo()` every 10 s. Experiments: `SLENDER_TRIM`,
  `SLENDER_POPOVER`, `SLENDER_THREADS`, `SLENDER_MEASURE_LLHOOK=0`.
- Runtime knobs were tried as `DOTNET_*` environment variables (the same settings the csproj properties write to
  runtimeconfig.json) and, for the gen0 cap, also as runtimeconfig.json edits on copies of the build
  (`tools/mem/New-RuntimeConfigVariant.ps1`).
- Scripts: `tools/mem/Measure-Process.ps1` (start N copies with env, sample WS / private WS (perf counter
  `WorkingSetPrivate`) / commit / threads / handles at fixed times, stop by process id), `Measure-Startup.ps1`,
  `Snapshot-Vmmap.ps1` + `VmmapTop.ps1` (VMMap summary and top images). Raw CSVs: `tools/mem/results-*.csv`.
- Tools: VMMap 3.4 (`https://live.sysinternals.com/vmmap64.exe`, single binary, Authenticode **Valid**, signer
  CN=Microsoft Corporation), downloaded to the session scratchpad, used only for read-only snapshots. `dotnet-counters`
  is not installed and was not installed; nothing attached to the real tray except VMMap's snapshot.
- Not measured: NativeAOT (no MSVC linker; publishing stops at "Platform linker not found"), the real tray's allocation
  rate (would need `dotnet-counters`/EventPipe attached to it), Settings on-screen vs off-screen (off-screen chosen so runs
  never covered the user's work), LogiPluginService behaviour after a reboot with Options+ disabled.

## Raw numbers

Medians (MB). Every run is in `tools/mem/results-*.csv` (label, run, sample time, WS, private WS, commit, peak WS,
threads, handles).

Tray, idle, no devices (`results-tray.csv`, `results-tray-ui.csv`, `results-tray-build.csv`, 3 runs, 60 s):

| Variant | WS | Private WS | Commit | Threads |
|---|---|---|---|---|
| base | 49.9–50.9 | 7.8–8.5 | 11.6–12.0 | 20–21 |
| `gcConcurrent=0` | 50.7 | 8.4 | 11.7 | 21 |
| `GCgen0size=1MB` (env) | 50.6 | 8.2 | 11.9 | 21 |
| `GCHeapHardLimit=64MB` | 50.3 | 8.3 | 11.6 | 21 |
| `GCConserveMemory=9` | 50.4 | 8.3 | 11.8 | 21 |
| segments GC (`clrgc.dll`) | 50.1 | 7.8 | 12.1 | 21 |
| `TieredCompilation=0` | 49.8 | 7.4 | 11.2 | 20 |
| ReadyToRun publish | 50.0 | 7.8 | 11.6 | 20 |
| `UseSystemResourceKeys=true` | 49.9 | 7.9 | 11.7 | 21 |
| `Gen0MaxBudget=1MB` (runtimeconfig) | 50.2 | 8.1 | 11.8 | 21 |
| no LL hook thread | 50.1 | 7.8 | 11.6 | 21 |
| +4 idle threads | 50.5 | 8.2 | 12.1 | 25 |
| WinForms popover shown | 53.1 | 8.8 | 12.9 | 22 |
| WinForms popover shown then disposed | 54.8 | 8.6 | 13.4 | 23 |

Tray with two simulated HID read loops (`results-tray-churn.csv`, 2 runs; 30 s / 2 min / 5 min commit):

| Variant | WS at 5 min | Private WS at 5 min | Commit 30 s / 2 min / 5 min |
|---|---|---|---|
| base | 53.0 | 8.4 | 12.6 / 11.9 / 12.3 |
| `gcConcurrent=0` | 53.0 | 8.3 | 11.8 / 11.8 / 12.1 |
| `GCgen0size=1MB` | 53.0 | 8.5 | 11.8 / 12.1 / 12.1 |
| `GCConserveMemory=9` | 53.3 | 8.8 | 11.7 / 12.0 / 12.6 |
| W^X off | 52.5 | 16.1 | 19.4 / 19.7 / 20.8 |
| `TieredCompilation=0` | 50.8 | 7.9 | 11.5 / 11.5 / 11.3 |
| `TieredPGO=0` | 52.3 | 8.3 | 12.6 / 11.9 / 12.2 |
| segments GC | 52.8 | 8.1 | 12.3 / 12.6 / 12.9 |
| aggressive GC once at 15 s | 52.3 | 7.6 | 11.2 / 10.7 / 11.5 |
| working-set trim once at 15 s | **10.3 → 12.9 → 26.3** (30 s / 2 min / 5 min) | 2.3 → 3.0 → 5.9 | 11.6 / 11.7 / 12.7 |
| GC + working-set trim at 15 s | 10.5 → 13.3 → 24.9 | 2.4 → 3.2 → 5.1 | 10.5 / 11.0 / 11.8 |

Tray with read loops plus 64 KB/s allocations (`results-tray-alloc.csv`, `results-tray-runtimeconfig.csv`, 2 runs):

| Variant | Commit 60 s | Commit 5 min | Private WS 5 min | WS 5 min |
|---|---|---|---|---|
| base (first batch) | 15.8 | 26.1 | 21.1 | 66.3 |
| base (second batch) | 16.3 | 23.7 | 19.1 | 63.7 |
| `GCgen0size=1MB` (env) | 12.8 / 13.6 | 13.1 / 12.6 | 8.9 / 8.5 | 54.0 / 53.2 |
| `GCgen0size=2MB` (env) | 13.7 | 14.7 | 10.0 | 55.1 |
| `GCConserveMemory=9` | 15.7 | 24.9 | 19.4 | 64.6 |
| `gcConcurrent=0` | 16.1 | 24.1 | 20.0 | 65.1 |
| segments GC | 16.2 | 24.3 | 19.3 | 64.6 |
| segments GC + `GCgen0size=1MB` | 12.6 | 13.9 | 8.7 | 54.0 |
| runtimeconfig `Gen0MaxBudget=1MB` | 13.3 | 12.3 | 8.5 | 53.2 |
| runtimeconfig `HeapHardLimit=32MB` | 14.2 | 13.6 | 10.1 | 54.9 |
| runtimeconfig `Gen0Size` / `GCgen0size` | 16.2 / 16.8 | 23.6 / 23.2 | 19.2 / 19.1 | 63.8 / 63.7 |

Tray without WinForms (`results-win32tray.csv`, 3 runs, 60 s):

| Variant | WS | Private WS | Commit | Threads |
|---|---|---|---|---|
| WinForms tray (measurement mode) | 49.8 | 7.5 | 11.5 | 21 |
| Win32 tray + same engine | 33.5 | 5.5 | 8.4 | 19 |
| Win32 tray + engine, `TieredCompilation=0` | 33.3 | 5.2 | 8.3 | 18 |
| Win32 tray, icon and menu only | 24.5 | 3.7 | 6.1 | 4 |

WinUI popover candidates (`results-popover.csv`, 2 runs, 60 s):

| Variant | WS | Private WS | Commit | Threads |
|---|---|---|---|---|
| Minimal WinUI 3 window, self-contained | 147.0 | 62.3 | 82.3 | 18 |
| Same, framework-dependent | 146.3 | 61.9 | 82.1 | 18 |
| Same, no Mica | 145.7 | 61.9 | 82.1 | 17 |
| Settings asked for 360×420 (clamped to 1040×640) | 201.9 | 85.8 | 110.7 | 28 |

Settings (`results-settings.csv`): see the Settings table above.
