using Slender.Hidpp;
using Slender.Hidpp.Inventory;
using Slender.Hidpp.Transport;

/// <summary>
/// --inventory: read-only HID++ device inventory (see Slender.Hidpp.Inventory). Uses SwId 0x0C so it never collides with the running
/// tray (0x0B); the HID handle is opened with shared read/write (WindowsHidTransportFactory), so the tray may stay running.
/// </summary>
internal static class InventoryCli
{
    private const string DepotsRoot = @"C:\ProgramData\LogiOptionsPlus\depots";

    private sealed record Target(string Key, string Slug, ushort[] Pids, byte[] IndexOrder);

    private static readonly Target Mx4 = new("mx4", "mx-master-4-2b042", WindowsHidTransportFactory.MxMaster4ProductIds, [0xFF, 0x01]);
    private static readonly Target K860 = new("k860", "ergo-k860-6b359", WindowsHidTransportFactory.ErgoK860ProductIds, [0x01, 0xFF]);

    public static async Task<int> RunAsync(string[] args)
    {
        string deviceArg = Value(args, "--device") ?? "mx4";
        string outDir = Value(args, "--out") ?? Path.Combine("docs", "devices");
        Target[] targets = deviceArg.ToLowerInvariant() switch
        {
            "mx4" => [Mx4],
            "k860" => [K860],
            "all" => [Mx4, K860],
            _ => [],
        };
        if (targets.Length == 0)
        {
            Console.WriteLine("--device must be mx4, k860 or all.");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        int rc = 0;
        foreach (var t in targets)
        {
            int one = await InventoryOneAsync(t, outDir, cts.Token);
            if (one != 0) rc = one;
        }
        return rc;
    }

    private static string? Value(string[] args, string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

    private static async Task<int> InventoryOneAsync(Target t, string outDir, CancellationToken ct)
    {
        Console.WriteLine($"== Inventory {t.Key} ==");
        var collection = WindowsHidTransportFactory.SelectCandidates(WindowsHidTransportFactory.Enumerate(), t.Pids).FirstOrDefault();
        var transport = new WindowsHidTransportFactory(t.Pids).TryOpen();
        if (transport == null)
        {
            Console.WriteLine("  No HID++ collection could be opened (device off, asleep or not paired).");
            return 2;
        }

        using var channel = new HidppChannel(transport, new HidppChannelOptions
        {
            SwId = InventoryOptions.InventorySwId,
            MaxRetries = 2,
            RequestTimeout = TimeSpan.FromMilliseconds(2000),
        });
        channel.Start();

        byte? index = null;
        foreach (byte idx in t.IndexOrder)
        {
            var r = await new HidppDevice(channel, idx).PingAsync(TimeSpan.FromMilliseconds(800), ct);
            Console.WriteLine($"  ping idx 0x{idx:X2} -> {r}");
            if (r == PingResult.Answered) { index = idx; break; }
        }
        if (index == null)
        {
            Console.WriteLine("  No device index answered.");
            return 3;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"slender-optionsplus-{Environment.ProcessId}");
        OptionsPlusMetadata plus;
        try { plus = OptionsPlusMetadata.Load(t.Slug, DepotsRoot, temp); }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        Console.WriteLine($"  Options+ labels: {plus.CidLabels.Count} controls, {plus.Slots.Count} other slots");

        var inv = await new InventoryRunner(channel, index.Value, new InventoryOptions(), plus).RunAsync(t.Slug, ct);
        if (collection != null)
        {
            inv.ProductId = collection.ProductId;
            inv.HidCollection = $"UP {collection.UsagePage:X4} U {collection.Usage:X4} in {collection.InputLength} out {collection.OutputLength}";
        }
        var (json, md) = InventoryReport.Write(inv, outDir, t.Slug);
        Console.WriteLine($"  {inv.Features.Count} features, {inv.RequestsSent} requests, {inv.ErrorCount} errors{(inv.Aborted != null ? $", stopped: {inv.Aborted}" : "")}");
        Console.WriteLine($"  wrote {json}\n  wrote {md}");
        return 0;
    }
}
