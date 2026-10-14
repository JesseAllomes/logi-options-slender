using System.Runtime.CompilerServices;

// Only HidppDevice (allowlisted) may send; tests reach the raw channel directly.
[assembly: InternalsVisibleTo("Slender.Tests")]
