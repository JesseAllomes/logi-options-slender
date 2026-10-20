using System.IO.Pipes;
using System.Text;

namespace Slender.Engine.ExcelAddin;

/// <summary>How a request to the add-in ended.</summary>
public enum AddinOutcome
{
    /// <summary>The add-in did it, or answered that the selection does not suit it. Nothing more to do.</summary>
    Handled,
    /// <summary>No add-in, no answer inside the time box, or Excel could not run it now. Use the key-based behaviour.</summary>
    Unavailable,
}

/// <summary>One request and one reply over the add-in's pipe. Null = no add-in, refused, or no complete reply inside the time box.</summary>
public interface IExcelAddinTransport
{
    AddinReply? Request(int excelProcessId, AddinRequest request, TimeSpan timeBox);
}

/// <summary>
/// Sends requests to the add-in inside Excel. Every call is time-boxed. After a call fails the same Excel is left alone for
/// <c>backoff</c>, so a missing or hung add-in costs one time box, not one per wheel notch. Call it from the engine's worker, never
/// from a hook or HID thread.
/// </summary>
public sealed class ExcelAddinClient
{
    public static readonly TimeSpan DefaultTimeBox = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(3);

    private readonly IExcelAddinTransport _transport;
    private readonly Func<DateTime> _utcNow;
    private readonly TimeSpan _timeBox;
    private readonly TimeSpan _backoff;
    private readonly Dictionary<int, DateTime> _retryAfter = [];
    private readonly object _gate = new();
    private int _nextId;

    public ExcelAddinClient(IExcelAddinTransport transport, Func<DateTime>? utcNow = null, TimeSpan? timeBox = null, TimeSpan? backoff = null)
    {
        _transport = transport;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _timeBox = timeBox ?? DefaultTimeBox;
        _backoff = backoff ?? DefaultBackoff;
    }

    public AddinOutcome Send(int excelProcessId, AddinVerb verb, string? arg = null)
    {
        if (excelProcessId <= 0) return AddinOutcome.Unavailable;
        var now = _utcNow();
        int id;
        lock (_gate)
        {
            if (_retryAfter.TryGetValue(excelProcessId, out var until))
            {
                if (now < until) return AddinOutcome.Unavailable;
                _retryAfter.Remove(excelProcessId);
            }
            id = _nextId = (_nextId + 1) & 0x7FFFFFFF;
        }

        AddinReply? reply;
        try { reply = _transport.Request(excelProcessId, new AddinRequest(id, verb, arg), _timeBox); }
        catch { reply = null; } // a transport must not throw, but nothing here may escape into the engine

        if (reply is { } r && r.Id == id && r.Status is AddinStatus.Ok or AddinStatus.NotApplicable) return AddinOutcome.Handled;
        // Busy means the add-in is there but Excel is in edit mode or a dialog: the same is true for the next notch, so back off too.
        lock (_gate) _retryAfter[excelProcessId] = now + _backoff;
        return AddinOutcome.Unavailable;
    }

    /// <summary>Forgets a failed attempt so the next request goes out at once (the add-in has just been installed or Excel restarted).</summary>
    public void Reset()
    {
        lock (_gate) _retryAfter.Clear();
    }
}

/// <summary>
/// The real transport: the add-in's current-user-only named pipe. The client asks for CurrentUserOnly too, so a pipe of the same name
/// made by another account is never trusted. Connect and reply share one time box.
/// </summary>
public sealed class NamedPipeExcelAddinTransport : IExcelAddinTransport
{
    public AddinReply? Request(int excelProcessId, AddinRequest request, TimeSpan timeBox)
    {
        var line = ExcelAddinProtocol.Encode(request);
        if (line is null) return null;
        // A pipe that does not exist makes Connect wait out the whole time box. Listing the pipe folder is free and, unlike
        // File.Exists on the pipe itself, never takes the server's single connection.
        if (!PipeExists(ExcelAddinProtocol.PipeName(excelProcessId))) return null;
        using var cts = new CancellationTokenSource(timeBox);
        try
        {
            using var pipe = new NamedPipeClientStream(".", ExcelAddinProtocol.PipeName(excelProcessId), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.ConnectAsync(cts.Token).GetAwaiter().GetResult();
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            pipe.WriteAsync(bytes, cts.Token).AsTask().GetAwaiter().GetResult();
            pipe.FlushAsync(cts.Token).GetAwaiter().GetResult();
            var reply = ReadLineAsync(pipe, cts.Token).GetAwaiter().GetResult();
            return ExcelAddinProtocol.TryDecodeReply(reply, out var decoded) ? decoded : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            return null;
        }
    }

    internal static bool PipeExists(string pipeName)
    {
        try
        {
            foreach (var _ in Directory.EnumerateFiles(@"\\.\pipe\", pipeName)) return true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Reads up to the first newline, never more than one protocol line. Null when the pipe closes or overruns first.</summary>
    internal static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[ExcelAddinProtocol.MaxLineChars * 4 + 4];
        var used = 0;
        while (used < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(used), ct).ConfigureAwait(false);
            if (read == 0) return null;
            var nl = Array.IndexOf(buffer, (byte)'\n', used, read);
            used += read;
            if (nl >= 0) return Encoding.UTF8.GetString(buffer, 0, nl).TrimEnd('\r');
        }
        return null;
    }
}
