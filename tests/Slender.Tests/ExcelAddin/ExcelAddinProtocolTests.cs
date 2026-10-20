using Slender.Engine.ExcelAddin;

namespace Slender.Tests.ExcelAddin;

public class ExcelAddinProtocolTests
{
    [Theory]
    [InlineData(AddinVerb.Ping, null, "S1 5 ping")]
    [InlineData(AddinVerb.NextSheet, null, "S1 5 next-sheet")]
    [InlineData(AddinVerb.PreviousSheet, null, "S1 5 prev-sheet")]
    [InlineData(AddinVerb.ZoomIn, null, "S1 5 zoom-in")]
    [InlineData(AddinVerb.ZoomOut, null, "S1 5 zoom-out")]
    [InlineData(AddinVerb.ZoomReset, null, "S1 5 zoom-reset")]
    [InlineData(AddinVerb.TableStyle, null, "S1 5 table-style")]
    [InlineData(AddinVerb.ChartType, null, "S1 5 chart-type")]
    [InlineData(AddinVerb.Wheel, "-3", "S1 5 wheel -3")]
    [InlineData(AddinVerb.Wheel, "12", "S1 5 wheel 12")]
    [InlineData(AddinVerb.Macro, "Module1.Tidy", "S1 5 macro Module1.Tidy")]
    [InlineData(AddinVerb.Macro, "'My Book.xlsm'!Tidy", null)] // spaces are not allowed in a macro name
    public void RequestEncodesAndDecodesBack(AddinVerb verb, string? arg, string? line)
    {
        string? encoded = ExcelAddinProtocol.Encode(new AddinRequest(5, verb, arg));
        Assert.Equal(line, encoded);
        if (line is null) return;
        Assert.True(ExcelAddinProtocol.TryDecodeRequest(encoded, out var back));
        Assert.Equal(new AddinRequest(5, verb, arg), back);
    }

    [Fact]
    public void EveryVerbHasADistinctName()
    {
        var names = Enum.GetValues<AddinVerb>().Select(ExcelAddinProtocol.VerbName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("S1")]
    [InlineData("S2 1 ping")]            // wrong version
    [InlineData("S1 x ping")]            // id not a number
    [InlineData("S1 -1 ping")]           // negative id
    [InlineData("S1 1 unknown")]
    [InlineData("S1 1 ping extra")]      // ping takes no argument
    [InlineData("S1 1 next-sheet 1")]
    [InlineData("S1 1 wheel")]           // wheel needs one
    [InlineData("S1 1 wheel 0")]
    [InlineData("S1 1 wheel 1001")]
    [InlineData("S1 1 wheel two")]
    [InlineData("S1 1 macro")]
    [InlineData("S1 1 macro Tidy()")]
    [InlineData("S1 1 macro a b")]
    [InlineData("S1 1 macro Tidy\r")]
    [InlineData("S1 1 ping\nS1 2 ping")] // embedded newline
    [InlineData("S1 1 macro ..\\evil")]
    public void BadRequestsAreRejected(string? line)
    {
        Assert.False(ExcelAddinProtocol.TryDecodeRequest(line, out _));
    }

    [Fact]
    public void OverlongRequestsAreRejected()
    {
        var name = new string('a', ExcelAddinProtocol.MaxMacroNameChars + 1);
        Assert.Null(ExcelAddinProtocol.Encode(new AddinRequest(1, AddinVerb.Macro, name)));
        Assert.False(ExcelAddinProtocol.TryDecodeRequest("S1 1 macro " + name, out _));
        Assert.False(ExcelAddinProtocol.TryDecodeRequest("S1 1 ping" + new string(' ', 300), out _));
    }

    [Fact]
    public void EncodingRejectsInvalidRequests()
    {
        Assert.Null(ExcelAddinProtocol.Encode(new AddinRequest(-1, AddinVerb.Ping)));
        Assert.Null(ExcelAddinProtocol.Encode(new AddinRequest(1, AddinVerb.Wheel, "0")));
        Assert.Null(ExcelAddinProtocol.Encode(new AddinRequest(1, AddinVerb.Ping, "arg")));
    }

    [Theory]
    [InlineData("Module1.Tidy", true)]
    [InlineData("_Hidden", true)]
    [InlineData("Book1.xlsm!Sheet1.Run", true)]
    [InlineData("", false)]
    [InlineData("1Start", false)]
    [InlineData("Has Space", false)]
    [InlineData("Quote\"d", false)]
    [InlineData("a;b", false)]
    [InlineData("C:\\x", false)]
    public void MacroNamesAreRestricted(string name, bool valid) => Assert.Equal(valid, ExcelAddinProtocol.IsValidMacroName(name));

    [Theory]
    [InlineData(AddinStatus.Ok, null, "S1 9 ok")]
    [InlineData(AddinStatus.Ok, "scrolled", "S1 9 ok scrolled")]
    [InlineData(AddinStatus.NotApplicable, null, "S1 9 na")]
    [InlineData(AddinStatus.Busy, null, "S1 9 busy")]
    [InlineData(AddinStatus.Failed, null, "S1 9 fail")]
    [InlineData(AddinStatus.BadRequest, null, "S1 9 bad")]
    public void ReplyEncodesAndDecodesBack(AddinStatus status, string? text, string line)
    {
        var reply = new AddinReply(9, status, text);
        Assert.Equal(line, ExcelAddinProtocol.Encode(reply));
        Assert.True(ExcelAddinProtocol.TryDecodeReply(line, out var back));
        Assert.Equal(reply, back);
    }

    [Fact]
    public void ReplyTextCannotInjectLines()
    {
        string line = ExcelAddinProtocol.Encode(new AddinReply(1, AddinStatus.Failed, "a\nS1 2 ok"));
        Assert.DoesNotContain('\n', line);
        Assert.True(ExcelAddinProtocol.TryDecodeReply(line, out var back));
        Assert.Equal(1, back.Id);
        Assert.Equal(AddinStatus.Failed, back.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("S1 1")]
    [InlineData("S1 1 maybe")]
    [InlineData("S1 q ok")]
    [InlineData("S3 1 ok")]
    public void BadRepliesAreRejected(string? line) => Assert.False(ExcelAddinProtocol.TryDecodeReply(line, out _));

    [Fact]
    public void PipeNameIsPerExcelProcess() => Assert.Equal("Slender.Excel.4242", ExcelAddinProtocol.PipeName(4242));
}

public class ExcelAddinLogicTests
{
    [Fact]
    public void NextSheetSkipsHiddenAndWraps()
    {
        bool[] visible = [true, false, true, false, true];
        Assert.Equal(2, ExcelAddinLogic.NextVisibleSheet(visible, 0, 1));
        Assert.Equal(4, ExcelAddinLogic.NextVisibleSheet(visible, 2, 1));
        Assert.Equal(0, ExcelAddinLogic.NextVisibleSheet(visible, 4, 1)); // wraps
        Assert.Equal(4, ExcelAddinLogic.NextVisibleSheet(visible, 0, -1)); // wraps back
        Assert.Equal(0, ExcelAddinLogic.NextVisibleSheet(visible, 2, -1));
    }

    [Fact]
    public void NextSheetWithNowhereToGoIsMinusOne()
    {
        Assert.Equal(-1, ExcelAddinLogic.NextVisibleSheet([true], 0, 1));
        Assert.Equal(-1, ExcelAddinLogic.NextVisibleSheet([true, false, false], 0, 1));
        Assert.Equal(-1, ExcelAddinLogic.NextVisibleSheet([], 0, 1));
        Assert.Equal(-1, ExcelAddinLogic.NextVisibleSheet([true, true], 0, 0));
    }

    [Fact]
    public void NextSheetFromAHiddenCurrentStillMoves()
    {
        Assert.Equal(2, ExcelAddinLogic.NextVisibleSheet([true, false, true], 1, 1));
        Assert.Equal(0, ExcelAddinLogic.NextVisibleSheet([true, false, true], 1, -1));
    }

    [Theory]
    [InlineData(100, 1, 125)]
    [InlineData(100, -1, 75)]
    [InlineData(110, 1, 125)]   // between presets
    [InlineData(110, -1, 100)]
    [InlineData(400, 1, 400)]   // ends stay put
    [InlineData(10, -1, 10)]
    [InlineData(5, -1, 10)]
    public void ZoomMovesToTheNextPreset(int current, int direction, int expected) => Assert.Equal(expected, ExcelAddinLogic.NextZoom(current, direction));

    [Fact]
    public void CyclesWrapAndRecoverFromUnknownValues()
    {
        Assert.Equal("TableStyleMedium9", ExcelAddinLogic.NextInCycle(ExcelAddinLogic.TableStyles, "TableStyleMedium2"));
        Assert.Equal(ExcelAddinLogic.TableStyles[0], ExcelAddinLogic.NextInCycle(ExcelAddinLogic.TableStyles, ExcelAddinLogic.TableStyles[^1]));
        Assert.Equal(ExcelAddinLogic.TableStyles[0], ExcelAddinLogic.NextInCycle(ExcelAddinLogic.TableStyles, "Custom"));
        Assert.Equal(57, ExcelAddinLogic.NextInCycle(ExcelAddinLogic.ChartTypes, 51));
        Assert.Equal(51, ExcelAddinLogic.NextInCycle(ExcelAddinLogic.ChartTypes, -4169));
    }

    [Fact]
    public void WheelFollowsTheSelection()
    {
        Assert.Equal(WheelTarget.Columns, ExcelAddinLogic.WheelTargetFor(selectionIsRange: true));
        Assert.Equal(WheelTarget.Sheets, ExcelAddinLogic.WheelTargetFor(selectionIsRange: false));
    }
}
