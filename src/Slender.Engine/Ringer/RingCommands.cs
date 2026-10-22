using System.IO.Pipes;
using System.Text;

namespace Slender.Engine.Ringer;

/// <summary>What a ring button asks the 3CX web client to do.</summary>
public enum RingCommand { Answer, Reject }

/// <summary>The extension's report back: whether it found the 3CX control for <see cref="Command"/> and clicked it.</summary>
public readonly record struct RingResult(RingCommand Command, bool Found);

/// <summary>Which mouse buttons can answer or reject a ringing call (docs/3cx-ringer.md), and which command a press means.</summary>
public static class RingButtons
{
    /// <summary>The buttons the mouse hook sees: only those can be held back from their normal action, and only while a call rings.</summary>
    public static readonly IReadOnlyList<ushort> Choices = [Slender.Hidpp.ControlId.Middle, Slender.Hidpp.ControlId.Forward, Slender.Hidpp.ControlId.Back];

    /// <summary>The id as stored: 0 (off) for anything that is not a choice.</summary>
    public static ushort Normalize(int id) => id > 0 && id <= ushort.MaxValue && Choices.Contains((ushort)id) ? (ushort)id : (ushort)0;

    /// <summary>The command a press of <paramref name="id"/> means, or null when it is neither button (off never matches). Answer wins if both are the same button.</summary>
    public static RingCommand? Match(HapticSettings haptics, ushort id)
    {
        if (id == 0) return null;
        if (haptics.RingAnswerButton == id) return RingCommand.Answer;
        if (haptics.RingRejectButton == id) return RingCommand.Reject;
        return null;
    }

    public static bool AnyOn(HapticSettings haptics) => haptics.RingAnswerButton != 0 || haptics.RingRejectButton != 0;
}

/// <summary>Where a pressed ring button's command waits for the extension to fetch it.</summary>
internal interface IRingCommandChannel : IDisposable
{
    /// <summary>A ring started: from now the extension may poll.</summary>
    void Arm();

    /// <summary>The ring ended: stop answering polls, unless a command is still waiting to be fetched.</summary>
    void Disarm();

    /// <summary>A ring button was pressed: hand this to the next poll.</summary>
    void Offer(RingCommand command);
}

/// <summary>
/// The pipe the native host polls (<c>\\.\pipe\Slender.RingCmd</c>, current user only). It exists only from a ring start with ring buttons
/// set until the command has been fetched, the ring stops or 100 s pass; a poll then reads one line: <c>none</c> (armed, nothing yet),
/// <c>answer</c> or <c>reject</c>. No pipe means <c>off</c> to the client, so the extension stops polling.
/// </summary>
internal sealed class RingCommandServer(string pipeName = RingCommandPipe.PipeName) : IRingCommandChannel
{
    private const int LifeMs = 100_000;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts; // null = idle
    private string _state = RingCommandPipe.None;

    public bool IsArmed { get { lock (_gate) return _cts is not null; } }

    public void Arm()
    {
        lock (_gate)
        {
            if (_cts is not null) return;
            _state = RingCommandPipe.None;
            var cts = _cts = new CancellationTokenSource(LifeMs);
            _ = Task.Run(() => Serve(cts));
        }
    }

    public void Offer(RingCommand command)
    {
        lock (_gate)
        {
            if (_cts is not null) _state = command == RingCommand.Answer ? RingCommandPipe.Answer : RingCommandPipe.Reject;
        }
    }

    public void Disarm()
    {
        lock (_gate)
        {
            if (_state == RingCommandPipe.None) Stop();
        }
    }

    private void Stop() // _gate held
    {
        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private async Task Serve(CancellationTokenSource cts)
    {
        try
        {
            using var pipe = await CreatePipeAsync(cts.Token).ConfigureAwait(false);
            while (!cts.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
                string state;
                lock (_gate) state = _state;
                await pipe.WriteAsync(Encoding.ASCII.GetBytes(state + "\n"), cts.Token).ConfigureAwait(false);
                await pipe.FlushAsync(cts.Token).ConfigureAwait(false);
                await Task.Run(pipe.WaitForPipeDrain).ConfigureAwait(false); // the client must have read it before the pipe goes
                try { pipe.Disconnect(); } catch (InvalidOperationException) { }
                if (state != RingCommandPipe.None) { lock (_gate) { if (_cts == cts) Stop(); } }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            lock (_gate)
            {
                if (_cts == cts) _cts = null;
            }
            cts.Dispose();
        }
    }

    /// <summary>A pipe from an ended ring may still be closing (one instance only), so a quick next ring waits for it a moment.</summary>
    private async Task<NamedPipeServerStream> CreatePipeAsync(CancellationToken ct)
    {
        for (int tries = 0; ; tries++)
        {
            try { return new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); }
            catch (Exception ex) when (tries < 20 && ex is IOException or UnauthorizedAccessException) { await Task.Delay(25, ct).ConfigureAwait(false); }
        }
    }

    public void Dispose()
    {
        lock (_gate) Stop();
    }
}

/// <summary>The native host's side of the command pipe.</summary>
public static class RingCommandPipe
{
    public const string PipeName = "Slender.RingCmd";
    public const string None = "none", Answer = "answer", Reject = "reject", Off = "off";

    /// <summary>One poll: "none", "answer" or "reject", or "off" when nothing is armed (no pipe, Slender not running, or a bad reply).</summary>
    public static string Poll(string pipeName = PipeName, int timeoutMs = 150)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeoutMs);
            using var reader = new StreamReader(pipe, Encoding.ASCII, false, 16);
            string? line = reader.ReadLine();
            return line is None or Answer or Reject ? line : Off;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { return Off; }
    }
}
