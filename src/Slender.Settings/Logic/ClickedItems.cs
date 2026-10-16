namespace Slender.Settings.Logic;

/// <summary>
/// Maps a ListView/GridView ItemClick back to the option stored in the clicked row's Tag. When rows are added to Items as
/// ListViewItem/GridViewItem containers, WinUI reports the container's Content (the row's TextBlock or icon) as ClickedItem,
/// not the container, so reading Tag off ClickedItem finds nothing and the click is silently dropped.
/// </summary>
public static class ClickedItems
{
    /// <summary>The Tag of the row whose container or content is <paramref name="clicked"/>; or clicked itself when it is a T.</summary>
    public static T? Find<T>(object? clicked, IEnumerable<(object Container, object? Content, object? Tag)> rows)
        where T : class
    {
        if (clicked is null)
        {
            return null;
        }

        if (clicked is T direct)
        {
            return direct;
        }

        foreach ((object container, object? content, object? tag) in rows)
        {
            if ((ReferenceEquals(container, clicked) || ReferenceEquals(content, clicked)) && tag is T t)
            {
                return t;
            }
        }

        return null;
    }
}
