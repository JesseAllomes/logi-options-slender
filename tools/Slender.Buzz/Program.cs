using System.IO.Pipes;
using System.Text;

// Slender.Buzz.exe [name]: asks the running Slender tray to buzz the mouse (docs/buzz.md).
// Exit 0 = sent, 2 = Slender is not running. The pipe name matches BuzzProtocol.PipeName in Slender.Engine; this tool
// stays dependency-free so a hook or build script can call it quickly.
const string PipeName = "Slender.Buzz";

string name = string.Join(' ', args).Trim();
if (name.Length > 64)
{
    Console.Error.WriteLine("Alert name is too long (64 characters at most).");
    return 1;
}

try
{
    using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
    pipe.Connect(1000);
    var bytes = Encoding.UTF8.GetBytes(name + "\n");
    pipe.Write(bytes, 0, bytes.Length);
    pipe.Flush();
    return 0;
}
catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("Slender is not running (could not reach the buzz pipe within 1 second).");
    return 2;
}
