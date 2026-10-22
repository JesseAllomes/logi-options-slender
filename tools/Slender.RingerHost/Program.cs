using Slender.Engine;
using Slender.RingerHost;

// Slender.RingerHost.exe: the Edge native messaging host of the 3CX ringer (docs/3cx-ringer.md). Edge starts it for one message from the
// extension; it passes ring start/stop to the running Slender tray over the buzz pipe, answers {"ok":true|false} and exits. It keeps nothing.
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();

bool ok = NativeMessage.ReadFrame(input) is { } body
    && NativeMessage.Parse(body) is { } signal
    && BuzzProtocol.TrySend(BuzzProtocol.RingLine(signal));
NativeMessage.WriteReply(output, ok);
return ok ? 0 : 1;
