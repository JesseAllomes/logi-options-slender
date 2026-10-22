using Slender.Engine;
using Slender.Engine.Ringer;
using Slender.RingerHost;

// Slender.RingerHost.exe: the Edge native messaging host of the 3CX ringer (docs/3cx-ringer.md). Edge starts it for one message from the
// extension; it passes ring start/stop (and the extension's answer/reject click report) to the running Slender tray over the buzz pipe,
// or, for a poll, reads the waiting ring-button command from the command pipe. It answers {"ok":true|false}[,"state":...] and exits. It keeps nothing.
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();

var body = NativeMessage.ReadFrame(input);
if (body is not null && NativeMessage.IsPoll(body))
{
    NativeMessage.WriteReply(output, true, RingCommandPipe.Poll());
    return 0;
}

bool ok = body is not null
    && (NativeMessage.Parse(body) is { } signal ? BuzzProtocol.TrySend(BuzzProtocol.RingLine(signal))
        : NativeMessage.ParseResult(body) is { } result && BuzzProtocol.TrySend(BuzzProtocol.RingResultLine(result)));
NativeMessage.WriteReply(output, ok);
return ok ? 0 : 1;
