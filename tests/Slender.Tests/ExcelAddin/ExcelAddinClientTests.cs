using System.IO.Pipes;
using System.Text;
using Slender.Engine;
using Slender.Tests.Engine;
using Slender.Engine.ExcelAddin;

namespace Slender.Tests.ExcelAddin;

internal sealed class FakeTransport : IExcelAddinTransport
{
    public List<(int Pid, AddinRequest Request, TimeSpan TimeBox)> Calls { get; } = [];
    public Func<AddinRequest, AddinReply?> Answer { get; set; } = r => new AddinReply(r.Id, AddinStatus.Ok);
    public bool Throw;

    public AddinReply? Request(int pid, AddinRequest request, TimeSpan timeBox)
    {
        Calls.Add((pid, request, timeBox));
        if (Throw) throw new InvalidOperationException("x");
        return Answer(request);
    }
}

internal sealed class InlineQueue : IWorkQueue
{
    public int Posted;
    public void Post(Action work) { Posted++; work(); }
    public void Dispose() { }
}

public class ExcelAddinClientTests
{
    private static (ExcelAddinClient Client, FakeTransport Transport, Func<DateTime> Clock, Action<TimeSpan> Advance) Make()
    {
        var transport = new FakeTransport();
        var now = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        var client = new ExcelAddinClient(transport, () => now, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(3));
        return (client, transport, () => now, d => now += d);
    }

    [Fact]
    public void OkAndNotApplicableAreHandled()
    {
        var (client, transport, _, _) = Make();
        Assert.Equal(AddinOutcome.Handled, client.Send(100, AddinVerb.NextSheet));
        transport.Answer = r => new AddinReply(r.Id, AddinStatus.NotApplicable);
        Assert.Equal(AddinOutcome.Handled, client.Send(100, AddinVerb.ChartType)); // no chart selected: done, not "try another way"
    }

    [Fact]
    public void EveryCallCarriesTheTimeBox()
    {
        var (client, transport, _, _) = Make();
        client.Send(100, AddinVerb.ZoomIn);
        Assert.Equal(TimeSpan.FromMilliseconds(250), transport.Calls.Single().TimeBox);
        Assert.Equal(100, transport.Calls.Single().Pid);
    }

    [Theory]
    [InlineData(AddinStatus.Busy)]
    [InlineData(AddinStatus.Failed)]
    [InlineData(AddinStatus.BadRequest)]
    public void RefusalsAreUnavailable(AddinStatus status)
    {
        var (client, transport, _, _) = Make();
        transport.Answer = r => new AddinReply(r.Id, status);
        Assert.Equal(AddinOutcome.Unavailable, client.Send(100, AddinVerb.NextSheet));
    }

    [Fact]
    public void NoAnswerThrowAndWrongIdAreUnavailable()
    {
        var (client, transport, _, advance) = Make();
        transport.Answer = _ => null;
        Assert.Equal(AddinOutcome.Unavailable, client.Send(100, AddinVerb.NextSheet));
        advance(TimeSpan.FromSeconds(5));
        transport.Answer = r => new AddinReply(r.Id + 1, AddinStatus.Ok); // someone else's reply
        Assert.Equal(AddinOutcome.Unavailable, client.Send(100, AddinVerb.NextSheet));
        advance(TimeSpan.FromSeconds(5));
        transport.Throw = true;
        Assert.Equal(AddinOutcome.Unavailable, client.Send(100, AddinVerb.NextSheet));
    }

    [Fact]
    public void ABadProcessIdNeverReachesTheTransport()
    {
        var (client, transport, _, _) = Make();
        Assert.Equal(AddinOutcome.Unavailable, client.Send(0, AddinVerb.NextSheet));
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void AFailureBacksOffThatExcelOnlyThenRetries()
    {
        var (client, transport, _, advance) = Make();
        transport.Answer = _ => null;
        client.Send(100, AddinVerb.Wheel, "1");
        client.Send(100, AddinVerb.Wheel, "1");
        client.Send(100, AddinVerb.Wheel, "1");
        Assert.Single(transport.Calls); // two notches skipped the pipe: one time box lost, not three
        client.Send(200, AddinVerb.Wheel, "1"); // another Excel is not affected
        Assert.Equal(2, transport.Calls.Count);

        advance(TimeSpan.FromSeconds(3));
        transport.Answer = r => new AddinReply(r.Id, AddinStatus.Ok);
        Assert.Equal(AddinOutcome.Handled, client.Send(100, AddinVerb.Wheel, "1"));
        Assert.Equal(3, transport.Calls.Count);
    }

    [Fact]
    public void ResetClearsTheBackoff()
    {
        var (client, transport, _, _) = Make();
        transport.Answer = _ => null;
        client.Send(100, AddinVerb.ZoomIn);
        client.Reset();
        client.Send(100, AddinVerb.ZoomIn);
        Assert.Equal(2, transport.Calls.Count);
    }

    [Fact]
    public void RequestIdsDiffer()
    {
        var (client, transport, _, _) = Make();
        client.Send(100, AddinVerb.ZoomIn);
        client.Send(100, AddinVerb.ZoomOut);
        Assert.NotEqual(transport.Calls[0].Request.Id, transport.Calls[1].Request.Id);
    }
}

public class AddinFirstExcelBridgeTests
{
    private sealed class Rig
    {
        public FakeExcel Fallback { get; } = new();
        public FakeTransport Transport { get; } = new();
        public InlineQueue Queue { get; } = new();
        public AddinFirstExcelBridge Bridge { get; }
        public Rig() => Bridge = new AddinFirstExcelBridge(Fallback, new ExcelAddinClient(Transport), w => (int)w + 1000, Queue);
    }

    [Fact]
    public void AnsweringAddinDoesTheWorkAndTheFallbackStaysQuiet()
    {
        var r = new Rig();
        Assert.True(r.Bridge.TryRunCommand(42, ExcelCommand.NextSheet));
        Assert.Equal(AddinVerb.NextSheet, r.Transport.Calls.Single().Request.Verb);
        Assert.Equal(1042, r.Transport.Calls.Single().Pid);
        Assert.Empty(r.Fallback.Commands);
    }

    [Fact]
    public void SilentAddinFallsBackToTodaysBehaviour()
    {
        var r = new Rig { };
        r.Transport.Answer = _ => null;
        r.Bridge.TryRunCommand(42, ExcelCommand.ZoomIn);
        Assert.Equal([ExcelCommand.ZoomIn], r.Fallback.Commands);
        r.Bridge.TryScrollColumns(42, 3);
        Assert.Equal([((nint)42, 3)], r.Fallback.Scrolls);
    }

    [Fact]
    public void BusyExcelFallsBack()
    {
        var r = new Rig();
        r.Transport.Answer = q => new AddinReply(q.Id, AddinStatus.Busy);
        r.Bridge.TryRunCommand(42, ExcelCommand.PreviousSheet);
        Assert.Equal([ExcelCommand.PreviousSheet], r.Fallback.Commands);
    }

    [Fact]
    public void WheelGoesToTheAddinAsSignedColumns()
    {
        var r = new Rig();
        r.Bridge.TryScrollColumns(42, -4);
        var call = r.Transport.Calls.Single().Request;
        Assert.Equal(AddinVerb.Wheel, call.Verb);
        Assert.Equal("-4", call.Arg);
        Assert.Empty(r.Fallback.Scrolls);
    }

    [Fact]
    public void AddinOnlyCommandsHaveNoFallback()
    {
        var r = new Rig();
        r.Transport.Answer = _ => null;
        Assert.True(r.Bridge.TryRunCommand(42, ExcelCommand.CycleTableStyle));
        Assert.True(r.Bridge.TryRunCommand(42, ExcelCommand.CycleChartType));
        Assert.Empty(r.Fallback.Commands);
    }

    [Fact]
    public void CommandsWithoutAVerbGoStraightToTheBridge()
    {
        var r = new Rig();
        r.Bridge.TryRunCommand(42, ExcelCommand.ScrollToFirstColumn);
        Assert.Empty(r.Transport.Calls);
        Assert.Equal([ExcelCommand.ScrollToFirstColumn], r.Fallback.Commands);
    }

    [Fact]
    public void MacrosGoToTheAddinAndOnlyWithAValidName()
    {
        var r = new Rig();
        Assert.True(r.Bridge.TryRunMacro(42, "Module1.Tidy"));
        Assert.Equal((AddinVerb.Macro, "Module1.Tidy"), (r.Transport.Calls.Single().Request.Verb, r.Transport.Calls.Single().Request.Arg));
        Assert.False(r.Bridge.TryRunMacro(42, "Tidy(); Shell"));
        Assert.Single(r.Transport.Calls);
    }

    [Fact]
    public void NoWindowOrNoColumnsDoesNothing()
    {
        var r = new Rig();
        Assert.False(r.Bridge.TryRunCommand(0, ExcelCommand.NextSheet));
        Assert.False(r.Bridge.TryScrollColumns(0, 1));
        Assert.False(r.Bridge.TryScrollColumns(42, 0));
        Assert.False(r.Bridge.TryRunMacro(0, "Tidy"));
        Assert.Empty(r.Transport.Calls);
        Assert.Equal(0, r.Queue.Posted);
    }

    [Fact]
    public void PipeWorkIsQueuedNotRunOnTheCaller()
    {
        var queued = new List<Action>();
        var transport = new FakeTransport();
        var bridge = new AddinFirstExcelBridge(new FakeExcel(), new ExcelAddinClient(transport), w => 1, new CapturingQueue(queued));
        bridge.TryRunCommand(42, ExcelCommand.NextSheet);
        bridge.TryScrollColumns(42, 1);
        Assert.Empty(transport.Calls); // nothing happened on the calling (hook) thread
        Assert.Equal(2, queued.Count);
    }

    private sealed class CapturingQueue(List<Action> items) : IWorkQueue
    {
        public void Post(Action work) => items.Add(work);
        public void Dispose() { }
    }

    [Fact]
    public void EveryAddinCommandMapsToItsVerb()
    {
        Assert.Equal(AddinVerb.NextSheet, AddinFirstExcelBridge.VerbFor(ExcelCommand.NextSheet));
        Assert.Equal(AddinVerb.TableStyle, AddinFirstExcelBridge.VerbFor(ExcelCommand.CycleTableStyle));
        Assert.Equal(AddinVerb.ChartType, AddinFirstExcelBridge.VerbFor(ExcelCommand.CycleChartType));
        Assert.Null(AddinFirstExcelBridge.VerbFor(ExcelCommand.ScrollToLastUsedColumn));
    }
}

/// <summary>The real pipe transport against a server in this process (a made-up Excel pid, so no real Excel or add-in is involved).</summary>
public class NamedPipeExcelAddinTransportTests
{
    private static int FakePid() => 2_000_000_000 + Random.Shared.Next(0, 90_000_000);

    private static NamedPipeServerStream Server(int pid) => new(ExcelAddinProtocol.PipeName(pid), PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    [Fact]
    public async Task RoundTripsOneRequest()
    {
        int pid = FakePid();
        using var server = Server(pid);
        var served = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            string? line = await NamedPipeExcelAddinTransport.ReadLineAsync(server, CancellationToken.None);
            Assert.True(ExcelAddinProtocol.TryDecodeRequest(line, out var req));
            await server.WriteAsync(Encoding.UTF8.GetBytes(ExcelAddinProtocol.Encode(new AddinReply(req.Id, AddinStatus.Ok, "pong")) + "\n"));
            await server.FlushAsync();
            return req;
        });

        var reply = new NamedPipeExcelAddinTransport().Request(pid, new AddinRequest(77, AddinVerb.Ping), TimeSpan.FromSeconds(5));

        Assert.Equal(new AddinReply(77, AddinStatus.Ok, "pong"), reply);
        Assert.Equal(AddinVerb.Ping, (await served).Verb);
    }

    [Fact]
    public void MissingPipeAnswersAtOnceNotAfterTheTimeBox()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var reply = new NamedPipeExcelAddinTransport().Request(FakePid(), new AddinRequest(1, AddinVerb.NextSheet), TimeSpan.FromSeconds(5));
        Assert.Null(reply);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ASilentServerIsCutOffAtTheTimeBox()
    {
        int pid = FakePid();
        using var server = Server(pid);
        var accepted = server.WaitForConnectionAsync(); // accepts and never answers
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var reply = new NamedPipeExcelAddinTransport().Request(pid, new AddinRequest(1, AddinVerb.NextSheet), TimeSpan.FromMilliseconds(300));

        Assert.Null(reply);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3));
        await accepted;
    }

    [Fact]
    public async Task AnInvalidReplyIsNull()
    {
        int pid = FakePid();
        using var server = Server(pid);
        var served = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await NamedPipeExcelAddinTransport.ReadLineAsync(server, CancellationToken.None);
            await server.WriteAsync(Encoding.UTF8.GetBytes("garbage\n"));
            await server.FlushAsync();
        });
        Assert.Null(new NamedPipeExcelAddinTransport().Request(pid, new AddinRequest(1, AddinVerb.Ping), TimeSpan.FromSeconds(5)));
        await served;
    }
}
