using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Slender.Tests")]

namespace Slender.Engine;

/// <summary>Tiny logging seam so the engine needs no logging package.</summary>
public interface ILog
{
    void Info(string message);
    void Error(string message, Exception? ex = null);
}

public sealed class NullLog : ILog
{
    public static NullLog Instance { get; } = new();
    public void Info(string message) { }
    public void Error(string message, Exception? ex = null) { }
}
