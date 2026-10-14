using Slender.Hidpp;
using Slender.Hidpp.Transport;

// Slender.Probe: read-only HID++ explorer for the MX Master 4. Prints raw hex for everything.
// Opt-in flags change device state and restore it afterwards: --haptic-test, --divert-test, --thumbwheel-test.

bool haptic = args.Contains("--haptic-test");
bool divert = args.Contains("--divert-test");
bool thumb = args.Contains("--thumbwheel-test");
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Slender.Probe [--haptic-test] [--divert-test] [--thumbwheel-test]\nDefault is read-only.");
    return 0;
}

static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b);

Console.WriteLine("== HID collections (VID 046D) ==");
var all = WindowsHidTransportFactory.Enumerate();
foreach (var c in all)
{
    bool mx = WindowsHidTransportFactory.MxMaster4ProductIds.Contains(c.ProductId);
    Console.WriteLine($"VID {c.VendorId:X4} PID {c.ProductId:X4} UP {c.UsagePage:X4} U {c.Usage:X4} in {c.InputLength} out {c.OutputLength} feat {c.FeatureLength}{(mx ? " [MX4 PID]" : "")}\n  {c.Path}");
}
if (all.Count == 0) Console.WriteLine("(none)");

var transport = new WindowsHidTransportFactory().TryOpen();
if (transport == null)
{
    Console.WriteLine("No MX Master 4 HID++ collection could be opened.");
    return 2;
}

using var channel = new HidppChannel(transport);
channel.ReportSent += (_, b) => Console.WriteLine($"  TX {Hex(b)}");
channel.ReportReceived += (_, m) => Console.WriteLine($"  RX {m.ToHex()}{(m.IsError ? $"  (error 0x{m.ErrorCode:X2})" : "")}");
channel.Start();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;
var callCt = ct; // reset to None inside finally blocks so cleanup still runs after Ctrl+C

async Task<HidppMessage?> Try(HidppDevice dev, string label, ushort feature, byte fn, params byte[] p)
{
    Console.WriteLine($"{label}");
    try { return await dev.CallAsync(feature, fn, p, callCt); }
    catch (HidppException ex) { Console.WriteLine($"  -> {ex.Message}"); return null; }
}

Console.WriteLine("\n== Device index ping ==");
HidppDevice? device = null;
foreach (byte idx in new byte[] { 0xFF, 0x01 })
{
    var d = new HidppDevice(channel, idx);
    Console.WriteLine($"ping idx 0x{idx:X2}");
    var r = await d.PingAsync(TimeSpan.FromMilliseconds(800), ct);
    Console.WriteLine($"  -> {r}");
    if (r == PingResult.Answered && device == null) device = d;
}
if (device == null)
{
    Console.WriteLine("No device index answered.");
    return 3;
}
Console.WriteLine($"Using device index 0x{device.DeviceIndex:X2}");

Console.WriteLine("\n== Features ==");
var known = new ushort[]
{
    FeatureId.FeatureSet, FeatureId.DeviceName, FeatureId.Battery, FeatureId.UnifiedBattery, FeatureId.Haptics,
    FeatureId.ForceSensing, FeatureId.ReprogControlsV4, FeatureId.SmartShift, FeatureId.SmartShiftEnhanced,
    FeatureId.HiResWheel, FeatureId.ThumbWheel, FeatureId.AdjustableDpi,
};
var fsIdx = await device.GetFeatureIndexAsync(FeatureId.FeatureSet, ct);
if (fsIdx != null)
{
    var count = await Try(device, "FeatureSet getCount", FeatureId.FeatureSet, 0);
    int n = count?.Params[0] ?? 0;
    for (int i = 1; i <= n; i++)
    {
        var r = await Try(device, $"FeatureSet getFeatureID({i})", FeatureId.FeatureSet, 1, (byte)i);
        if (r != null) Console.WriteLine($"  index {i} = feature {(r.Params[0] << 8) | r.Params[1]:X4} type 0x{r.Params[2]:X2}");
    }
}
else
{
    foreach (var f in known)
    {
        var idx = await device.GetFeatureIndexAsync(f, ct);
        Console.WriteLine($"  0x{f:X4} -> {(idx is { } v ? $"index {v}" : "absent")}");
    }
}

Console.WriteLine("\n== 0x1B04 reprogrammable controls ==");
var cnt = await Try(device, "getCount", FeatureId.ReprogControlsV4, 0);
for (int i = 0; i < (cnt?.Params[0] ?? 0); i++)
{
    var info = await Try(device, $"getCidInfo({i})", FeatureId.ReprogControlsV4, 1, (byte)i);
    if (info == null) continue;
    var p = info.Params;
    Console.WriteLine($"  CID {(p[0] << 8) | p[1]:X4} task {(p[2] << 8) | p[3]:X4} flags 0x{p[4]:X2} pos {p[5]} group {p[6]} gmask 0x{p[7]:X2} flagsHi 0x{p[8]:X2}");
    var rep = await Try(device, $"getCidReporting({p[0]:X2}{p[1]:X2})", FeatureId.ReprogControlsV4, 2, p[0], p[1]);
    if (rep != null) Console.WriteLine($"  reporting: {Hex(rep.Params[..5])}");
}

Console.WriteLine("\n== 0x19B0 haptics ==");
var hs = await Try(device, "getState (fn1)", FeatureId.Haptics, 1);
if (hs != null) Console.WriteLine($"  enabled bit0={hs.Params[0] & 1} level={hs.Params[1]}");

Console.WriteLine("\n== 0x2150 thumb wheel ==");
var ti = await Try(device, "getInfo (fn0)", FeatureId.ThumbWheel, 0);
if (ti != null) Console.WriteLine($"  caps 0x{ti.Params[5]:X2} timeUnit 0x{(ti.Params[6] << 8) | ti.Params[7]:X4}");
var tc = await Try(device, "getConfig (fn1)", FeatureId.ThumbWheel, 1);
if (tc != null) Console.WriteLine($"  mode {tc.Params[0]} invert {tc.Params[1]}");

Console.WriteLine("\n== 0x2121 hi-res wheel ==");
await Try(device, "getMode (fn1)", FeatureId.HiResWheel, 1);

Console.WriteLine("\n== Battery ==");
var bat = await Try(device, "0x1004 fn1", FeatureId.UnifiedBattery, 1)
          ?? await Try(device, "0x1000 fn0", FeatureId.Battery, 0);
if (bat != null) Console.WriteLine($"  parsed: {LogiMouse.ParseBattery(bat.Params)}");

if (haptic)
{
    Console.WriteLine("\n== --haptic-test ==");
    var r4 = await Try(device, "waveform 0 via fn4", FeatureId.Haptics, 4, 0);
    Console.WriteLine($"  fn4 answered: {r4 != null}");
    await Task.Delay(500, ct);
    var r3 = await Try(device, "waveform 0 via fn3", FeatureId.Haptics, 3, 0);
    Console.WriteLine($"  fn3 answered: {r3 != null}");
}

if (divert)
{
    Console.WriteLine("\n== --divert-test (CID 00C3) ==");
    try
    {
        await Try(device, "divert 00C3", FeatureId.ReprogControlsV4, 3, 0x00, 0xC3, 0x03, 0, 0);
        var rb = await Try(device, "read back fn2", FeatureId.ReprogControlsV4, 2, 0x00, 0xC3);
        if (rb != null) Console.WriteLine($"  flags byte (params[2]) = 0x{rb.Params[2]:X2}");
        Console.WriteLine("Press the gesture button now; waiting 5 s...");
        await Task.Delay(5000, ct);
    }
    finally
    {
        callCt = CancellationToken.None;
        await Try(device, "undivert 00C3", FeatureId.ReprogControlsV4, 3, 0x00, 0xC3, 0x02, 0, 0);
    }
}

if (thumb)
{
    Console.WriteLine("\n== --thumbwheel-test ==");
    try
    {
        await Try(device, "divert thumb wheel", FeatureId.ThumbWheel, 2, 1, 0, 0);
        Console.WriteLine("Scroll/touch the thumb wheel now; waiting 10 s (raw RX lines above)...");
        await Task.Delay(10000, ct);
    }
    finally
    {
        callCt = CancellationToken.None;
        await Try(device, "restore native", FeatureId.ThumbWheel, 2, 0, 0, 0);
    }
}

Console.WriteLine("\nDone.");
return 0;
