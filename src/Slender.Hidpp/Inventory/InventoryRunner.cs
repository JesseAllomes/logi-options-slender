using System.Diagnostics;
using System.Globalization;

namespace Slender.Hidpp.Inventory;

public sealed class InventoryOptions
{
    /// <summary>Software id the inventory uses; the tray uses 0x0B, so replies to its requests never match ours and vice versa.</summary>
    public const byte InventorySwId = 0x0C;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMilliseconds(2000);
    /// <summary>Retries per request (clamped to 4).</summary>
    public int MaxRetries { get; init; } = 2;
    public int MaxRequests { get; init; } = 800;
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(3);
    /// <summary>The device is treated as asleep/gone after this many timeouts in a row; remaining calls are skipped.</summary>
    public int MaxConsecutiveTimeouts { get; init; } = 6;
}

/// <summary>
/// Read-only inventory of one HID++ 2.0 device. Every request goes through <see cref="InventoryGetters.EnsureAllowed"/> first, so only
/// vetted getters are sent and the never-call features are listed without a single byte written to them.
/// The channel should use <see cref="InventoryOptions.InventorySwId"/>; replies with another SwId and all notifications are ignored by the channel.
/// </summary>
public sealed class InventoryRunner
{
    private readonly HidppChannel _channel;
    private readonly byte _deviceIndex;
    private readonly InventoryOptions _options;
    private readonly OptionsPlusMetadata _plus;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _sent;
    private int _errors;
    private int _consecutiveTimeouts;
    private string? _aborted;

    public InventoryRunner(HidppChannel channel, byte deviceIndex, InventoryOptions? options = null, OptionsPlusMetadata? plus = null)
    {
        _channel = channel;
        _deviceIndex = deviceIndex;
        _options = options ?? new InventoryOptions();
        _plus = plus ?? OptionsPlusMetadata.Empty;
    }

    private bool BudgetExhausted(out string why)
    {
        why = "";
        if (_aborted != null) why = _aborted;
        else if (_sent >= _options.MaxRequests) why = $"request cap {_options.MaxRequests} reached";
        else if (_clock.Elapsed > _options.MaxDuration) why = $"time cap {_options.MaxDuration} reached";
        else if (_consecutiveTimeouts >= _options.MaxConsecutiveTimeouts) why = $"{_consecutiveTimeouts} consecutive timeouts (device asleep or gone)";
        return why.Length > 0;
    }

    private async Task<CallRecord> SendAsync(InventoryGetter g, byte featureIndex, FeatureTypeFlags flags, byte[] p, string label, CancellationToken ct)
    {
        InventoryGetters.EnsureAllowed(g.FeatureId, g.Function, flags); // before any byte is written
        var rec = new CallRecord { Getter = label, Function = g.Function, RequestHex = Convert.ToHexString(p) };
        if (BudgetExhausted(out var why))
        {
            rec.Status = "skipped";
            rec.Decoded = new() { ["note"] = why };
            return rec;
        }
        _sent++;
        try
        {
            var msg = await _channel.RequestAsync(_deviceIndex, featureIndex, g.Function, p, ct,
                Math.Clamp(_options.MaxRetries, 0, 4), _options.RequestTimeout).ConfigureAwait(false);
            _consecutiveTimeouts = 0;
            var reply = msg.Params.ToArray();
            rec.ReplyHex = Convert.ToHexString(reply);
            try { rec.Decoded = g.Decode(p, reply); }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
            {
                rec.Decoded = new() { ["decodeError"] = ex.Message };
            }
        }
        catch (HidppErrorException e)
        {
            _consecutiveTimeouts = 0;
            _errors++;
            rec.Status = "hid-error";
            rec.ErrorCode = e.Code;
        }
        catch (HidppTimeoutException)
        {
            _consecutiveTimeouts++;
            _errors++;
            rec.Status = "timeout";
        }
        catch (HidppException e)
        {
            _errors++;
            _aborted = $"transport failure: {e.Message}";
            rec.Status = "timeout";
            rec.Decoded = new() { ["note"] = _aborted };
        }
        return rec;
    }

    private static InventoryGetter Core(ushort feature, byte fn) => InventoryGetters.For(feature).First(g => g.Function == fn && g.Core);

    private static int Int(object? o) => Convert.ToInt32(o, CultureInfo.InvariantCulture);

    public async Task<DeviceInventory> RunAsync(string deviceName, CancellationToken ct)
    {
        var inv = new DeviceInventory
        {
            Device = deviceName,
            GeneratedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            SwId = _channel.SwId,
            DeviceIndex = _deviceIndex,
        };

        try { await RunCoreAsync(inv, ct).ConfigureAwait(false); }
        finally
        {
            inv.RequestsSent = _sent;
            inv.ErrorCount = _errors;
            inv.Aborted ??= _aborted ?? (BudgetExhausted(out var why) ? why : null);
            inv.OptionsPlusSlots = [.. _plus.Slots];
        }
        return inv;
    }

    private async Task RunCoreAsync(DeviceInventory inv, CancellationToken ct)
    {
        // IRoot fn1 ping: protocol version.
        var ping = await SendAsync(Core(0x0000, 1), 0, default, [0, 0, 0x5A], "ping", ct).ConfigureAwait(false);
        if (ping.Status == "ok") inv.ProtocolVersion = $"{Int(ping.Decoded!["major"])}.{Int(ping.Decoded["minor"])}";

        // IRoot fn0: where is IFeatureSet?
        var look = await SendAsync(Core(0x0000, 0), 0, default, [0x00, 0x01, 0], "IRoot.getFeature(0x0001)", ct).ConfigureAwait(false);
        if (look.Status != "ok" || Int(look.Decoded!["index"]) == 0)
        {
            inv.Aborted = "IFeatureSet (0x0001) not available";
            return;
        }
        byte fsIdx = (byte)Int(look.Decoded["index"]);

        var count = await SendAsync(Core(0x0001, 0), fsIdx, default, [], "IFeatureSet.getCount", ct).ConfigureAwait(false);
        if (count.Status != "ok") return;
        int n = Int(count.Decoded!["count"]);
        inv.FeatureCount = n;

        // Feature 0 (IRoot) is implicit; indices 1..n come from getFeatureId.
        inv.Features.Add(MakeFeature(0, 0x0000, null, new FeatureTypeFlags(0)));
        for (int i = 1; i <= n; i++)
        {
            var rec = await SendAsync(Core(0x0001, 1), fsIdx, default, [(byte)i], $"IFeatureSet.getFeatureId({i})", ct).ConfigureAwait(false);
            if (rec.Status != "ok")
            {
                var bad = MakeFeature(i, 0xFFFF, null, new FeatureTypeFlags(0));
                bad.Name = "(getFeatureId failed)";
                bad.Reason = $"getFeatureId({i}) {rec.Status}{(rec.ErrorCode is { } c ? $" 0x{c:X2}" : "")}";
                bad.Calls.Add(rec);
                inv.Features.Add(bad);
                continue;
            }
            var r = Convert.FromHexString(rec.ReplyHex!);
            ushort id = (ushort)((r[0] << 8) | r[1]);
            inv.Features.Add(MakeFeature(i, id, r[3], new FeatureTypeFlags(r[2])));
        }

        // Getters per feature. Indices come from the enumeration above, so no IRoot lookup per feature is needed.
        foreach (var f in inv.Features)
        {
            if (f.Id == 0xFFFF) { f.Disposition = "listed-only"; continue; }
            var fl = new FeatureTypeFlags((byte)f.TypeFlagsRaw);
            var plan = InventoryPlanner.Plan(f.Id, fl);
            f.Called = plan.Disposition == PlanDisposition.Called;
            f.Disposition = f.Called ? "called" : "listed-only";
            f.Reason = plan.Reason;
            if (f.Id == 0x0000) f.Calls.Add(ping);
            else if (f.Id == 0x0001) { f.Calls.Add(look); f.Calls.Add(count); }
            if (plan.Getters.Count == 0) continue;
            await RunFeatureAsync(f, plan, fl, ct).ConfigureAwait(false);
        }

        Summarise(inv);
    }

    private FeatureRecord MakeFeature(int index, ushort id, byte? version, FeatureTypeFlags fl) => new()
    {
        Index = index,
        Id = id,
        Name = InventoryNames.FeatureName(id),
        Version = version,
        TypeFlagsRaw = fl.Raw,
        Flags = [.. fl.Names()],
        OptionsPlusNote = _plus.FeatureSlots.TryGetValue(id, out var slot) ? $"Options+ slot {slot}" : null,
    };

    private async Task RunFeatureAsync(FeatureRecord f, FeaturePlan plan, FeatureTypeFlags fl, CancellationToken ct)
    {
        var replies = new Dictionary<byte, List<byte[]>>();
        foreach (var g in plan.Getters)
        {
            var view = replies.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<byte[]>)kv.Value);
            foreach (var call in InventoryPlanner.Expand(g, view))
            {
                var rec = await SendAsync(g, (byte)f.Index, fl, call.Params, call.Label, ct).ConfigureAwait(false);
                f.Calls.Add(rec);
                if (rec.Status == "ok")
                {
                    if (!replies.TryGetValue(g.Function, out var list)) replies[g.Function] = list = [];
                    list.Add(Convert.FromHexString(rec.ReplyHex!));
                }
            }
        }
    }

    // ---- post-processing: firmware entities, device name, control table

    private void Summarise(DeviceInventory inv)
    {
        foreach (var f in inv.Features)
        {
            switch (f.Id)
            {
                case 0x0003:
                    foreach (var c in f.Calls.Where(c => c.Function == 1 && c.Status == "ok"))
                        inv.Firmware.Add(new FirmwareEntity
                        {
                            Entity = Convert.ToInt32(c.RequestHex[..2], 16),
                            Kind = (string)c.Decoded!["kind"]!,
                            Name = (string)c.Decoded["name"]!,
                            Version = (string)c.Decoded["version"]!,
                            Build = (string)c.Decoded["build"]!,
                            Active = (bool)c.Decoded["active"]!,
                        });
                    break;
                case 0x0005:
                    inv.DeviceName = string.Concat(f.Calls.Where(c => c.Function == 1 && c.Status == "ok").Select(c => (string)c.Decoded!["chunk"]!));
                    inv.DeviceType = f.Calls.FirstOrDefault(c => c.Function == 2 && c.Status == "ok")?.Decoded?["name"] as string;
                    break;
                case 0x0007:
                    inv.FriendlyName = string.Concat(f.Calls.Where(c => c.Function == 1 && c.Status == "ok").Select(c => (string)c.Decoded!["chunk"]!));
                    break;
                case 0x1B04:
                    BuildCids(inv, f);
                    break;
            }
        }
    }

    private void BuildCids(DeviceInventory inv, FeatureRecord f)
    {
        var reporting = f.Calls.Where(c => c.Function == 2 && c.Status == "ok").ToDictionary(c => (string)c.Decoded!["cid"]!, c => c.Decoded!);
        foreach (var c in f.Calls.Where(c => c.Function == 1 && c.Status == "ok"))
        {
            var d = c.Decoded!;
            var cidText = (string)d["cid"]!;
            ushort cid = ushort.Parse(cidText[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int flags = Int(d["flags"]);
            inv.Cids.Add(new CidRecord
            {
                Cid = cid,
                Task = int.Parse(((string)d["task"]!)[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                Flags = flags,
                FlagNames = InventoryGetters.DecodeKeyFlags(flags),
                Position = Int(d["position"]),
                Group = Int(d["group"]),
                GroupMask = Int(d["groupMask"]),
                Name = InventoryNames.ControlName(cid),
                OptionsPlusLabel = _plus.CidLabels.TryGetValue(cid, out var l) ? l : null,
                Reporting = reporting.GetValueOrDefault(cidText),
            });
        }
    }
}
