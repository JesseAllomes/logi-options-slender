using Slender.Engine;
using Slender.Settings.Logic;
using Xunit;

namespace Slender.Tests.Settings;

public class ClickedItemsTests
{
    private sealed class Row(object content, object? tag)
    {
        public object Content { get; } = content;
        public object? Tag { get; } = tag;
    }

    private static readonly ActionOption Off = new("Turn off", new ActionSpec.Disabled());
    private static readonly ActionOption Bright = new("Brightness up", new ActionSpec.Brightness(10));

    private static (object, object?, object?)[] Rows(params Row[] rows) => [.. rows.Select(r => ((object)r, (object?)r.Content, r.Tag))];

    [Fact]
    public void ClickedContent_FindsTheRowTag()
    {
        // WinUI reports the container's Content when containers are added to Items directly: the "Turn off" bug.
        var offText = new object();
        Row[] rows = [new Row(new object(), Bright), new Row(offText, Off)];

        Assert.Same(Off, ClickedItems.Find<ActionOption>(offText, Rows(rows)));
    }

    [Fact]
    public void ClickedContainer_FindsItsTag()
    {
        Row[] rows = [new Row(new object(), Bright), new Row(new object(), Off)];

        Assert.Same(Bright, ClickedItems.Find<ActionOption>(rows[0], Rows(rows)));
    }

    [Fact]
    public void ClickedOptionItself_IsReturned()
    {
        Assert.Same(Off, ClickedItems.Find<ActionOption>(Off, []));
    }

    [Fact]
    public void UnknownOrNull_ReturnsNull()
    {
        Row[] rows = [new Row(new object(), Off)];

        Assert.Null(ClickedItems.Find<ActionOption>(new object(), Rows(rows)));
        Assert.Null(ClickedItems.Find<ActionOption>(null, Rows(rows)));
    }

    [Fact]
    public void TagOfAnotherType_IsSkipped()
    {
        var content = new object();
        Row[] rows = [new Row(content, "not an option")];

        Assert.Null(ClickedItems.Find<ActionOption>(content, Rows(rows)));
    }
}
