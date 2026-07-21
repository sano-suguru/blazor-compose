// Polyfill required for C# 9+ records targeting netstandard2.0.
// The compiler emits references to this type for init-only properties; it does not exist
// in netstandard2.0 but can be provided inline.
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
