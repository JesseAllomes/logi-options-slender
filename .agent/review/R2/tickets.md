## r21
TICKET r21  (severity medium)
FILES:     src/Slender.Tray/Ring/RingSlotText.cs lines 100-115 (IconFile and its doc); tests/Slender.Tests/Tray/RingIconFileTests.cs (add InlineData rows only)
DEFECT:    IconFile only rejects a leading "\\"; the forward-slash UNC form ("//host/share/a.exe", from Icon "file://host/share/a.exe") and mapped network drives (Z:\...) pass Path.IsPathFullyQualified, so opening the ring probes a remote share over SMB (NTLM hash leak, UI-thread stall).
CHANGE:    1. Add `private static bool IsLocal(string path)`: false when path starts with "\\" or "//" (or "\/" or "/\"), false when !Path.IsPathFullyQualified(path); otherwise take `Path.GetPathRoot(path)` and return `new DriveInfo(root).DriveType is not (DriveType.Network or DriveType.NoRootDirectory)` inside try/catch (ArgumentException, IOException) returning false on exception.
           2. Line 113: `if (path is null || !IsLocal(path)) return null;`
           3. Doc: "Only local drives are used: UNC paths (either slash) and mapped network drives are never probed."
           4. Add InlineData rows: (@"file://host/share/a.exe", null, null); (null, "//10.0.0.9/s/a.exe", null); (null, @"\/host/share/a.exe", null).
TEST:      the new rows; the "//" rows fail before CHANGE.
DO NOT:    touch other files or change other rows.
DONE WHEN: the theory passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r21: <reason>"

## r23
TICKET r23  (severity medium)
FILES:     tests/Slender.Tests/Tray/RingIconFileTests.cs (append only)
DEFECT:    RingIconCache.Get's missing-file branch (src/Slender.Tray/Ring/RingIcons.cs:198) has no test.
CHANGE:    1. Append [Fact] Missing_icon_file_is_probed_once_and_cached_as_none: `var cache = new RingIconCache(null!);` (the branch returns before the canvas is used); path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe"); Assert.Equal(0, cache.Get(path, 32)) twice. Do not call Dispose if it would dereference the null canvas (check RingIconCache.Dispose; skip disposing when it touches _canvas).
TEST:      the new fact; passes with current code.
DO NOT:    touch src files or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r23: <reason>"

## r25
TICKET r25  (severity low)
FILES:     tests/Slender.Tests/Tray/RingIconFileTests.cs (append only)
DEFECT:    FileIcons.Load's .png dispatch to PngImages.Load (src/Slender.Tray/Ring/RingIcons.cs:11), the path used for imported action images, has no test.
CHANGE:    1. Append [Fact] Png_icon_loads_as_square_premultiplied_pixels: write a valid 2x2 opaque red PNG to a temp file (embed its bytes as a base64 literal; generate it correctly, including CRCs, e.g. by computing it once with System.IO.Hashing-free code or a known-good literal), call FileIcons.Load(path, 16), assert the result is not null and has length 16 * 16 * 4; delete the temp file in finally.
TEST:      the new fact; passes with current code (if it fails, report the failure as BLOCKED with details rather than editing src).
DO NOT:    touch src files or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r25: <reason>"

## r26
TICKET r26  (severity low)
FILES:     tests/Slender.Tests/Ring/RingLayoutTests.cs (append only)
DEFECT:    RingLayout.ClampCentre's ring-larger-than-work-area branch (src/Slender.Engine/Ring/RingLayout.cs:115) is untested.
CHANGE:    1. Append [Fact] Ring_larger_than_the_work_area_is_centred_on_it: Bezel Large metrics (Extent 180 DIP) at dpiScale 2.0 = 360 px extent; work area 0,0,600,500 (both axes smaller than 720); Place(AtCursor, 10, 10, ...) -> (300, 250).
TEST:      the new fact; passes with current code.
DO NOT:    touch src files or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r26: <reason>"

## r27
TICKET r27  (severity low)
FILES:     tests/Slender.Tests/Tray/ActionTextTests.cs (new)
DEFECT:    The new ActionText.Describe arms (src/Slender.Tray/UI/ActionText.cs ~lines 15-18: Launch, Disabled, Brightness, Text) have no test.
CHANGE:    1. New test class with a [Fact] per arm asserting the exact strings the code returns today (read ActionText.cs first): Launch("C:\Tools\x.exe") -> "Open: C:\Tools\x.exe" (or whatever the code returns), Disabled, Brightness(+10) and Brightness(-10), Text("Kind regards").
TEST:      the new facts; pass with current code.
DO NOT:    touch src files.
DONE WHEN: the facts exist and the suite passes.
IF BLOCKED: stop, leave nothing behind, return "BLOCKED r27: <reason>"

## r29
TICKET r29  (severity low)
FILES:     src/Slender.Tray/Ring/RingRenderer.cs lines 139-151 (Compass selected label); src/Slender.Tray/Ring/RingCanvas.cs (HaloText only, if trimming is missing)
DEFECT:    The Compass selected-slot label on the left/right slots starts about 132k from the centre with maxWidth 150k, but the window ends at Extent (220k), so long names or shortcuts ("Scroll to adjust") are clipped at the window edge.
CHANGE:    1. In the selected-label block compute the room to the window edge for side labels: `var room = align == 0 ? 150 * k : m.Extent - Math.Abs(x + dx - cx) - 4 * k;` and pass `Math.Min(150 * k, room)` as maxWidth to both HaloText calls (lines 149-150).
           2. Read RingCanvas.HaloText: if its DirectWrite layout does not already trim to maxWidth with an ellipsis (DWRITE_TRIMMING_GRANULARITY_CHARACTER + an ellipsis trimming sign, and word wrapping off), add that so text longer than maxWidth ends in "…" instead of overflowing. If it already trims, change nothing there.
TEST:      none practical (rendering); suite must pass. Say in the report whether HaloText already trimmed.
DO NOT:    change sizes, Extent, other styles, or other files.
DONE WHEN: builds with -warnaserror, suite passes, a NativeAOT publish of src/Slender.Tray (output inside the repo, then deleted) has no new warnings.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r29: <reason>"

## r30
TICKET r30  (severity low)
FILES:     src/Slender.Tray/Ring/RingSlotText.cs lines 117-119 (Fit); tests/Slender.Tests/Tray/RingIconFileTests.cs (append only)
DEFECT:    Fit cuts at a UTF-16 index; when the cut lands between the halves of a surrogate pair (emoji in a slot name) it leaves a lone high surrogate before the ellipsis.
CHANGE:    1. Rewrite Fit as a block body: if (text.Length <= maxChars || maxChars < 2) return text; int cut = maxChars - 1; if (char.IsHighSurrogate(text[cut - 1])) cut--; return string.Concat(text.AsSpan(0, cut).TrimEnd(), "…");
           2. Append [Fact] Fit_never_splits_a_surrogate_pair: Fit("123456789\U0001F600abc", 11) contains no lone surrogate (every high surrogate is followed by a low one) and ends with "…"; Fit("short", 11) == "short".
TEST:      the new fact; fails before CHANGE.
DO NOT:    touch other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r30: <reason>"

