// Polyfill required for C# 9+ records targeting net472. The compiler emits references to this
// type for init-only properties; it does not exist on net472 but can be provided inline. Guarded to
// net472 only: net10.0 already ships the real type, and defining it again there is a duplicate-type
// error.
#if NETFRAMEWORK
using System.ComponentModel;

namespace System.Runtime.CompilerServices;

[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit { }
#endif
