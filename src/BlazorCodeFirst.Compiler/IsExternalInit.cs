// Polyfill required for C# 9+ records targeting netstandard2.0.
// The compiler emits references to this type for init-only properties; it does not exist
// in netstandard2.0 but can be provided inline.
using System.ComponentModel;

namespace System.Runtime.CompilerServices;

[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit { }
