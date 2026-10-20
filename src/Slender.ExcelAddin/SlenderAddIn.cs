using ExcelDna.Integration;

namespace Slender.ExcelAddin;

/// <summary>
/// The Slender Excel add-in (Excel-DNA .xll). It has no ribbon, no menus, no worksheet functions, no timers and no network access.
/// At start it opens one named pipe that only the current Windows user can connect to, and then does nothing until Slender writes a
/// request to it. Without Slender running there is never a client, so there is never any work.
/// </summary>
public sealed class SlenderAddIn : IExcelAddIn
{
    private AddinPipeServer? _server;

    public void AutoOpen()
    {
        try
        {
            _server = new AddinPipeServer(new ExcelActions());
            _server.Start();
        }
        catch
        {
            _server = null; // an add-in must never stop Excel from starting
        }
    }

    public void AutoClose()
    {
        _server?.Dispose();
        _server = null;
    }
}
