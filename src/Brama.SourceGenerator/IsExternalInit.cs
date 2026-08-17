using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill for the marker type the compiler requires to emit <c>init</c> accessors, and therefore
/// records. It ships with .NET 5 and later, but Roslyn hosts generators on netstandard2.0, where it
/// does not exist.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit;
