using System.Collections.Concurrent;
using Slender.Engine;
using Slender.Engine.Devices;
using Slender.GalaxyBuds;
using Slender.Tests.Engine;

namespace Slender.Tests.GalaxyBuds;

internal sealed class FakeBudsLink : IBudsLink
{
    private readonly BlockingCollection<byte[]> _inbox = [];
    public readonly ConcurrentQueue<BudsFrame> Sent = new();
    public volatile bool Disposed;
    public bool FailConnect;
    public bool AutoAck = true;
    public Action<BudsFrame>? OnWrite;
    public readonly ManualResetEventSlim Connecting = new();

    public void Connect()
    {
        Connecting.Set();
        if (FailConnect) throw new IOException("unreachable");
    }

    public int Read(byte[] buffer)
    {
        try
        {
            var chunk = _inbox.Take();
            chunk.CopyTo(buffer, 0);
            return chunk.Length;
        }
        catch (InvalidOperationException) { return 0; }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (Disposed) throw new IOException("closed");
        var frames = new List<BudsFrame>();
        new BudsFrameReader().Feed(data, frames);
        foreach (var f in frames)
        {
            Sent.Enqueue(f);
            OnWrite?.Invoke(f);
            if (AutoAck && !BudsTestData.IsRead(f.Id)) Push(BudsTestData.FrameBytes(BudsIds.Ack, f.Id));
        }
    }

    public void Push(byte[] bytes)
    {
        if (!_inbox.IsAddingCompleted) _inbox.Add(bytes);
    }

    public void Dispose()
    {
        Disposed = true;
        try { _inbox.CompleteAdding(); }
        catch (ObjectDisposedException) { }
    }
}

internal sealed class FakeBudsTransport : IBudsTransport
{
    public readonly List<FakeBudsLink> Links = [];
    public bool FailConnect;
    public int Creates;

    public IBudsLink Create(ulong address)
    {
        var l = new FakeBudsLink { FailConnect = FailConnect };
        lock (Links) Links.Add(l);
        Interlocked.Increment(ref Creates);
        return l;
    }

    public FakeBudsLink Last { get { lock (Links) return Links[^1]; } }
}

public sealed class BudsModuleTests
{
    private readonly FakeBudsTransport _transport = new();
    private readonly ManualTimeProvider _time = new();
    private readonly List<string> _links = [];
    private readonly List<Exception> _faults = [];
    private ulong? _address = 0x1122;
    private int _hintsRegistered, _hintsDisposed;
    private Action? _hint;

    private BudsModule Make() => new(_transport, () => _address,
        (id, up) => { lock (_links) _links.Add($"{id}:{up}"); }, (id, e) => _faults.Add(e), null, _time,
        connectHint: cb => { _hintsRegistered++; _hint = cb; return new Disposer(() => _hintsDisposed++); })
    { AckTimeout = TimeSpan.FromMilliseconds(300) };

    private sealed class Disposer(Action a) : IDisposable { public void Dispose() => a(); }

    private static void Until(Func<bool> cond, string what = "condition")
    {
        for (int i = 0; i < 400 && !cond(); i++) Thread.Sleep(10);
        Assert.True(cond(), "timed out waiting for " + what);
    }

    private static void SendStatus(FakeBudsLink link, byte[]? esu = null) =>
        link.Push(BudsTestData.FrameBytes(97, esu ?? BudsTestData.Hex(BudsTestData.EsuPayloadHex)));

    private BudsModule Connected(out FakeBudsLink link, byte[]? esu = null)
    {
        var m = Make();
        m.Start();
        Until(() => _transport.Creates == 1, "connect attempt");
        link = _transport.Last;
        SendStatus(link, esu);
        Until(() => m.LinkUp, "link up");
        return m;
    }

    private static byte[] EsuWithPlacement(byte placement)
    {
        var p = BudsTestData.Hex(BudsTestData.EsuPayloadHex);
        p[6] = placement;
        return p;
    }

    [Fact]
    public void LinkIsReportedUpOnFirstFrameAndStateIsParsed()
    {
        using var m = Connected(out var link);
        Assert.Equal(["galaxy-buds4-pro:True"], _links);
        var s = m.GetState();
        Assert.True(s.Connected);
        Assert.Equal(100, s.BatteryLeft);
        Assert.Equal(95, s.BatteryCase);
        Assert.Equal("in case", s.PlacementRight);
        // The model name is requested once per link.
        Until(() => link.Sent.Any(f => f.Id == BudsIds.DebugSku), "model request");
    }

    [Fact]
    public void StopClosesEverythingAndReportsLinkDown()
    {
        var m = Connected(out var link);
        m.Stop();
        Assert.True(link.Disposed);
        Until(() => m.LiveThreads == 0, "reader thread to end");
        Assert.False(m.HasTimers);
        Assert.Equal(0, _time.ArmedTimers);
        Assert.Equal(1, _hintsDisposed);
        Assert.Equal(["galaxy-buds4-pro:True", "galaxy-buds4-pro:False"], _links);
        Assert.False(m.GetState().Connected);
        m.Stop(); // idempotent
    }

    [Fact]
    public void StopWhileConnectingLeavesNoThread()
    {
        var link = new FakeBudsLink();
        var m = new BudsModule(new OneLinkTransport(link), () => 1UL, (_, _) => { }, (_, _) => { }, null, _time);
        m.Start();
        Until(() => link.Connecting.IsSet, "connect to begin");
        m.Stop();
        Until(() => m.LiveThreads == 0, "thread to end");
        Assert.False(m.HasTimers);
    }

    private sealed class OneLinkTransport(FakeBudsLink link) : IBudsTransport
    {
        public IBudsLink Create(ulong address) => link;
    }

    [Fact]
    public void StartDoesNotBlockOnConnect()
    {
        var link = new FakeBudsLink();
        var gate = new ManualResetEventSlim();
        var m = new BudsModule(new BlockingTransport(gate), () => 1UL, (_, _) => { }, (_, _) => { }, null, _time);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        m.Start();
        Assert.True(sw.ElapsedMilliseconds < 500);
        gate.Set();
        m.Stop();
        _ = link;
    }

    private sealed class BlockingTransport(ManualResetEventSlim gate) : IBudsTransport
    {
        public IBudsLink Create(ulong address)
        {
            gate.Wait(5000);
            return new FakeBudsLink();
        }
    }

    [Fact]
    public void FailedConnectRetriesSlowlyAndOnlyWhileRunning()
    {
        _transport.FailConnect = true;
        var m = Make();
        m.Start();
        Until(() => _time.ArmedTimers == 1, "retry timer");
        Assert.Equal(1, _transport.Creates);
        Assert.Empty(_links);

        _time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(1, _transport.Creates);
        _time.Advance(TimeSpan.FromSeconds(2));
        Until(() => _transport.Creates == 2, "second attempt");
        Until(() => _time.ArmedTimers == 1, "next retry timer");
        // Next wait is 120 s, doubling up to 5 minutes.
        var due = _time.Timers.Last(t => !t.Disposed).Due!.Value - _time.GetUtcNow();
        Assert.Equal(TimeSpan.FromSeconds(120), due);

        m.Stop();
        Assert.Equal(0, _time.ArmedTimers);
        Assert.False(m.HasTimers);
        Until(() => m.LiveThreads == 0, "thread to end");
    }

    [Fact]
    public void RetryDelayStopsGrowingAtFiveMinutes()
    {
        _transport.FailConnect = true;
        var m = Make();
        m.Start();
        for (int i = 0; i < 6; i++)
        {
            int before = _transport.Creates;
            Until(() => _time.ArmedTimers == 1, "retry timer");
            _time.Advance(TimeSpan.FromMinutes(6));
            Until(() => _transport.Creates == before + 1, "attempt");
        }

        Until(() => _time.ArmedTimers == 1, "retry timer");
        Assert.Equal(BudsModule.MaxRetry, _time.Timers.Last(t => !t.Disposed).Due!.Value - _time.GetUtcNow());
        m.Stop();
    }

    [Fact]
    public void NoPairedBudsMeansQuietRetry()
    {
        _address = null;
        var m = Make();
        m.Start();
        Until(() => _time.ArmedTimers == 1, "retry timer");
        Assert.Equal(0, _transport.Creates);
        m.Stop();
    }

    [Fact]
    public void WakeAndConnectHintTryAtOnceWhileDown()
    {
        _transport.FailConnect = true;
        var m = Make();
        m.Start();
        Until(() => _time.ArmedTimers == 1, "retry timer");
        Assert.Equal(1, _hintsRegistered);

        _transport.FailConnect = false;
        m.Wake();
        Until(() => _transport.Creates == 2, "attempt after Wake");
        SendStatus(_transport.Last);
        Until(() => m.LinkUp, "link up");
        Assert.Equal(0, _time.ArmedTimers);

        // Link up: hints and wakes do nothing.
        _hint!();
        m.Wake();
        Thread.Sleep(50);
        Assert.Equal(2, _transport.Creates);
        m.Stop();
    }

    [Fact]
    public void SocketLossReportsDownAndSchedulesRetry()
    {
        var m = Connected(out var link);
        link.Dispose(); // the buds went away
        Until(() => !m.LinkUp, "link down");
        Until(() => _time.ArmedTimers == 1, "retry timer");
        Assert.Equal(["galaxy-buds4-pro:True", "galaxy-buds4-pro:False"], _links);
        var s = m.GetState();
        Assert.False(s.Connected);
        Assert.Null(s.BatteryLeft);
        m.Stop();
    }

    [Fact]
    public void UnexpectedExceptionIsAFaultNotACrash()
    {
        var m = new BudsModule(new ThrowingTransport(), () => 1UL, (_, _) => { }, (_, e) => _faults.Add(e), null, _time);
        m.Start();
        Until(() => _faults.Count == 1, "fault");
        Assert.IsType<InvalidOperationException>(_faults[0]);
        Assert.Equal(0, _time.ArmedTimers);
        m.Stop();
    }

    private sealed class ThrowingTransport : IBudsTransport
    {
        public IBudsLink Create(ulong address) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void SetSendsTheEncodedMessageAndUpdatesOnAck()
    {
        using var m = Connected(out var link);
        int sidetone = GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.Sidetone);
        m.Set(sidetone, 1);
        var sent = link.Sent.Last(f => f.Id == 139);
        Assert.Equal(new byte[] { 1 }, sent.Payload);
        var s = m.GetState();
        Assert.Null(s.WriteProblem);
        Assert.Equal(1, s.Settings!.First(x => x.Key == GalaxyBudsSettingsCatalog.Sidetone).Raw);
    }

    [Fact]
    public void SetKeepsTheOtherFieldOfAMultiFieldMessage()
    {
        using var m = Connected(out var link);
        m.Set(GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.CycleLeft), 11);
        Assert.Equal(new byte[] { 11, 9 }, link.Sent.Last(f => f.Id == 121).Payload);
        Assert.Equal(11, m.GetState().Settings!.First(x => x.Key == GalaxyBudsSettingsCatalog.CycleLeft).Raw);
    }

    [Fact]
    public void SetWithoutAnAckSetsAWriteProblemAndKeepsTheValue()
    {
        using var m = Connected(out var link);
        link.AutoAck = false;
        m.Set(GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.Sidetone), 1);
        var s = m.GetState();
        Assert.Equal("The earbuds did not take that change.", s.WriteProblem);
        Assert.Equal(0, s.Settings!.First(x => x.Key == GalaxyBudsSettingsCatalog.Sidetone).Raw);

        link.AutoAck = true;
        m.Set(GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.Sidetone), 1);
        Assert.Null(m.GetState().WriteProblem);
    }

    [Fact]
    public void AnAckForAnotherIdIsNotAnAnswer()
    {
        using var m = Connected(out var link);
        link.AutoAck = false;
        var t = Task.Run(() => m.Set(GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.Sidetone), 1));
        Until(() => link.Sent.Any(f => f.Id == 139), "write");
        link.Push(BudsTestData.FrameBytes(BudsIds.Ack, 140));
        Until(() => t.IsCompleted, "Set to finish");
        Assert.NotNull(m.GetState().WriteProblem);
    }

    [Fact]
    public void AStatusUpdateCountsAsTheAnswer()
    {
        using var m = Connected(out var link);
        link.AutoAck = false;
        var t = Task.Run(() => m.Set(GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.NoiseMode), 1));
        Until(() => link.Sent.Any(f => f.Id == 120), "write");
        link.Push(BudsTestData.FrameBytes(119, 1, 0x33, 1, 0, 9, 9, 2, 2, 1, 2, 2));
        Until(() => t.IsCompleted, "Set to finish");
        var s = m.GetState();
        Assert.Null(s.WriteProblem);
        Assert.Equal(1, s.Settings!.First(x => x.Key == GalaxyBudsSettingsCatalog.NoiseMode).Raw);
    }

    [Fact]
    public void RefusedChangesNeverReachTheLink()
    {
        using var m = Connected(out var link);
        int before = link.Sent.Count(f => !BudsTestData.IsRead(f.Id));
        m.Set(GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.Gestures), 0);
        Assert.NotNull(m.GetState().WriteProblem);
        m.Set(GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.NoiseMode), 9);
        m.Set(-1, 0);
        m.Set(999, 0);
        m.Set(0, long.MaxValue);
        Assert.Equal(before, link.Sent.Count(f => !BudsTestData.IsRead(f.Id)));
        Assert.DoesNotContain(link.Sent, f => f.Id is 120 or 144);
    }

    [Fact]
    public void SetWhileDisconnectedSaysSo()
    {
        var m = Make();
        m.Set(0, 1);
        Assert.Equal("The earbuds are not connected.", m.GetState().WriteProblem);
    }

    [Fact]
    public void FindIsRefusedWhileAnEarbudIsWorn()
    {
        using var m = Connected(out var link, EsuWithPlacement(0x31));
        m.Find(true);
        var s = m.GetState();
        Assert.Equal("Take the earbuds out first; the beep is loud.", s.WriteProblem);
        Assert.False(s.FindActive);
        Assert.DoesNotContain(link.Sent, f => f.Id == BudsIds.FindStart);
        Assert.False(m.HasTimers);
    }

    [Fact]
    public void FindStartsStopsAndTimesOutAfter30Seconds()
    {
        using var m = Connected(out var link);
        m.Find(true);
        Assert.True(m.GetState().FindActive);
        Assert.Contains(link.Sent, f => f.Id == BudsIds.FindStart);
        Assert.Equal(1, _time.ArmedTimers);

        _time.Advance(BudsModule.FindLimit + TimeSpan.FromSeconds(1));
        Until(() => !m.GetState().FindActive, "automatic stop");
        Until(() => link.Sent.Any(f => f.Id == BudsIds.FindStop), "stop message");
        Assert.Equal(0, _time.ArmedTimers);

        m.Find(true);
        Assert.True(m.GetState().FindActive);
        m.Find(false);
        Assert.False(m.GetState().FindActive);
        Assert.Equal(2, link.Sent.Count(f => f.Id == BudsIds.FindStop));
    }

    [Fact]
    public void FindStopsWhenAnEarbudIsPutIn()
    {
        using var m = Connected(out var link);
        m.Find(true);
        Assert.True(m.GetState().FindActive);
        link.Push(BudsTestData.FrameBytes(96, 4, 100, 100, 1, 1, 0x13, 95, 0));
        Until(() => !m.GetState().FindActive, "find to stop");
        Until(() => link.Sent.Any(f => f.Id == BudsIds.FindStop), "stop message");
    }

    [Fact]
    public void StoppingWhileFindingSendsStop()
    {
        var m = Connected(out var link);
        m.Find(true);
        m.Stop();
        Assert.Contains(link.Sent, f => f.Id == BudsIds.FindStop);
        Assert.Equal(0, _time.ArmedTimers);
        Until(() => m.LiveThreads == 0, "thread to end");
    }

    [Fact]
    public void StateChangedFiresForStatusAndWrites()
    {
        var m = Make();
        int n = 0;
        m.StateChanged += () => Interlocked.Increment(ref n);
        m.Start();
        Until(() => _transport.Creates == 1, "attempt");
        SendStatus(_transport.Last);
        Until(() => Volatile.Read(ref n) >= 1, "event");
        int before = Volatile.Read(ref n);
        m.Set(GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.Sidetone), 1);
        Assert.True(Volatile.Read(ref n) > before);
        m.Stop();
    }

    [Fact]
    public void ReconnectAfterStopStartWorks()
    {
        var m = Connected(out _);
        m.Stop();
        Until(() => m.LiveThreads == 0, "thread to end");
        m.Start();
        Until(() => _transport.Creates == 2, "second connect");
        SendStatus(_transport.Last);
        Until(() => m.LinkUp, "link up again");
        Assert.Equal(2, _transport.Links.Count);
        m.Stop();
    }

    [Fact]
    public void DeviceIdIsTheCatalogEntry() => Assert.Equal(DeviceCatalog.GalaxyBuds4Pro, Make().DeviceId);

    private static void Answer(FakeBudsLink link)
    {
        link.Push(BudsTestData.FrameWithHeader(BudsTestData.MultipointHeader, BudsIds.MultipointInfo, BudsTestData.Hex(BudsTestData.MultipointPayloadHex)));
        link.Push(BudsTestData.FrameWithHeader(BudsTestData.CyclesHeader, BudsIds.BatteryCycles, BudsTestData.Hex(BudsTestData.CyclesPayloadHex)));
    }

    [Fact]
    public void LinkUpAsksMultipointAndCyclesEmptyAndShowsTheAnswers()
    {
        using var m = Connected(out var link);
        Until(() => link.Sent.Any(f => f.Id == BudsIds.DebugSku), "model request");
        Assert.Single(link.Sent, f => f.Id == BudsIds.MultipointInfo);
        Assert.Single(link.Sent, f => f.Id == BudsIds.BatteryCycles);
        Assert.All(link.Sent, f => Assert.Empty(f.Payload));
        Assert.DoesNotContain(link.Sent, f => f.Id == 207);

        Answer(link);
        Until(() => m.GetState().BatteryCyclesRight is not null && m.GetState().MultipointSupported is not null, "answers");
        var s = m.GetState();
        Assert.Equal(2.62, s.BatteryCyclesLeft!.Value, 6);
        Assert.Equal(20.39, s.BatteryCyclesRight!.Value, 6);
        Assert.True(s.MultipointSupported);
        Assert.Equal(0, s.MultipointStreaming);
    }

    [Fact]
    public void CyclesAreAskedAtMostEverySixHoursAndKeptAcrossReconnects()
    {
        var m = Connected(out var link);
        Until(() => link.Sent.Any(f => f.Id == BudsIds.DebugSku), "model request");
        Answer(link);
        Until(() => m.GetState().BatteryCyclesLeft is not null, "cycles");

        // More status within the window: no second ask.
        SendStatus(link);
        SendStatus(link);
        Until(() => m.GetState().MultipointSupported is not null, "multipoint");
        Assert.Single(link.Sent, f => f.Id == BudsIds.BatteryCycles);

        // Reconnect an hour later: multipoint is asked again, cycles are not, and the old cycles still show.
        m.Stop();
        Until(() => m.LiveThreads == 0, "thread to end");
        Assert.Equal(2.62, m.GetState().BatteryCyclesLeft!.Value, 6);
        Assert.Null(m.GetState().MultipointSupported);
        _time.Advance(TimeSpan.FromHours(1));
        m.Start();
        Until(() => _transport.Creates == 2, "second connect");
        var second = _transport.Last;
        SendStatus(second);
        Until(() => second.Sent.Any(f => f.Id == BudsIds.DebugSku), "model request again");
        Assert.Single(second.Sent, f => f.Id == BudsIds.MultipointInfo);
        Assert.DoesNotContain(second.Sent, f => f.Id == BudsIds.BatteryCycles);
        Assert.Equal(20.39, m.GetState().BatteryCyclesRight!.Value, 6);

        // Past six hours a status frame on the same link asks once more.
        _time.Advance(BudsModule.CycleReadEvery);
        SendStatus(second);
        Until(() => second.Sent.Any(f => f.Id == BudsIds.BatteryCycles), "second cycles ask");
        SendStatus(second);
        Thread.Sleep(50);
        Assert.Single(second.Sent, f => f.Id == BudsIds.BatteryCycles);
        Assert.DoesNotContain(second.Sent, f => f.Id == 207);
        m.Stop();
    }

    [Fact]
    public void RefreshAsksMultipointWhenUpAndConnectsWhenDown()
    {
        using var m = Connected(out var link);
        Until(() => link.Sent.Any(f => f.Id == BudsIds.DebugSku), "model request");
        m.Refresh();
        Until(() => link.Sent.Count(f => f.Id == BudsIds.MultipointInfo) == 2, "second multipoint ask");
        Assert.Single(link.Sent, f => f.Id == BudsIds.BatteryCycles);

        link.Dispose();
        Until(() => !m.LinkUp, "link down");
        m.Refresh();
        Until(() => _transport.Creates == 2, "reconnect at once");
    }

    [Fact]
    public void FindIsStoppedWhenAnEarbudIsPutInDuringTheAckWait()
    {
        using var m = Connected(out var link);
        link.AutoAck = false;
        var t = Task.Run(() => m.Find(true));
        Until(() => link.Sent.Any(f => f.Id == BudsIds.FindStart), "find start");
        link.Push(BudsTestData.FrameBytes(96, 4, 100, 100, 1, 1, 0x13, 95, 0));
        link.Push(BudsTestData.FrameBytes(BudsIds.Ack, BudsIds.FindStart));
        Until(() => t.IsCompleted, "Find to finish");
        var s = m.GetState();
        Assert.False(s.FindActive);
        Assert.Equal("Find stopped: an earbud was put in.", s.WriteProblem);
        Assert.Contains(link.Sent, f => f.Id == BudsIds.FindStop);
        Assert.False(m.HasTimers);
    }

    [Fact]
    public void FindSendsStopWhenTheStartIsNotAnswered()
    {
        using var m = Connected(out var link);
        link.AutoAck = false;
        m.Find(true);
        var s = m.GetState();
        Assert.Equal("The earbuds did not start the beep.", s.WriteProblem);
        Assert.False(s.FindActive);
        var ids = link.Sent.Select(f => f.Id).Where(id => id is BudsIds.FindStart or BudsIds.FindStop).ToArray();
        Assert.Equal([BudsIds.FindStart, BudsIds.FindStop], ids);
        Assert.False(m.HasTimers);
    }

    [Fact]
    public void StoppingDuringTheFirstReadRequestsNeverLeavesTheLinkReportedUp()
    {
        var m = Make();
        m.Start();
        Until(() => _transport.Creates == 1, "connect attempt");
        var link = _transport.Last;
        link.OnWrite = f => { if (f.Id == BudsIds.MultipointInfo) m.Stop(); };
        SendStatus(link);
        Until(() => m.LiveThreads == 0, "thread to end");
        lock (_links) Assert.True(_links.Count == 0 || !_links[^1].EndsWith(":True"), string.Join(",", _links));
    }

    [Fact]
    public void AStatusUpdateWithoutTheNewValueIsNotTheAnswer()
    {
        using var m = Connected(out var link);
        link.AutoAck = false;
        var t = Task.Run(() => m.Set(GalaxyBudsSettingsCatalog.IndexOf(GalaxyBudsSettingsCatalog.NoiseMode), 1));
        Until(() => link.Sent.Any(f => f.Id == 120), "write");
        link.Push(BudsTestData.FrameBytes(96, 4, 100, 100, 1, 1, 0x33, 95, 0));
        SendStatus(link);
        Until(() => t.IsCompleted, "Set to finish");
        var s = m.GetState();
        Assert.Equal("The earbuds did not take that change.", s.WriteProblem);
        Assert.Equal(0, s.Settings!.First(x => x.Key == GalaxyBudsSettingsCatalog.NoiseMode).Raw);
    }

    [Fact]
    public void ADropDuringFindSendsStopOnReconnect()
    {
        var m = Connected(out var first);
        m.Find(true);
        Assert.True(m.GetState().FindActive);
        first.Dispose();
        Until(() => !m.LinkUp, "link down");
        Until(() => _time.ArmedTimers == 1, "retry timer");
        _time.Advance(BudsModule.FirstRetry + TimeSpan.FromSeconds(1));
        Until(() => _transport.Creates == 2, "second attempt");
        var second = _transport.Last;
        SendStatus(second);
        Until(() => m.LinkUp, "link up again");
        Until(() => second.Sent.Any(f => f.Id == BudsIds.FindStop), "stop on reconnect");
        Assert.False(m.GetState().FindActive);
        m.Stop();
    }

    [Fact]
    public void ReconnectWithoutFindSendsNoStop()
    {
        var m = Connected(out var first);
        first.Dispose();
        Until(() => !m.LinkUp, "link down");
        Until(() => _time.ArmedTimers == 1, "retry timer");
        _time.Advance(BudsModule.FirstRetry + TimeSpan.FromSeconds(1));
        Until(() => _transport.Creates == 2, "second attempt");
        var second = _transport.Last;
        SendStatus(second);
        Until(() => m.LinkUp, "link up again");
        Until(() => second.Sent.Any(f => f.Id == BudsIds.DebugSku), "model request");
        Assert.DoesNotContain(second.Sent, f => f.Id == BudsIds.FindStop);
        m.Stop();
    }

    [Fact]
    public void StopDisposesTheConnectHintOutsideTheModuleLock()
    {
        bool? finished = null;
        BudsModule? m = null;
        int disposed = 0;
        m = new BudsModule(_transport, () => _address,
            (id, up) => { lock (_links) _links.Add($"{id}:{up}"); }, (id, e) => _faults.Add(e), null, _time,
            connectHint: cb => new Disposer(() =>
            {
                Interlocked.Increment(ref disposed);
                var task = Task.Run(() => m!.Wake());
                finished = task.Wait(TimeSpan.FromSeconds(2));
            }))
        { AckTimeout = TimeSpan.FromMilliseconds(300) };
        m.Start();
        Until(() => _transport.Creates == 1, "connect attempt");
        m.Stop();
        Assert.Equal(1, disposed);
        Assert.True(finished);
    }
}
