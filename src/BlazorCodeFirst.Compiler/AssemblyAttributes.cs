using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("BlazorCodeFirst.Compiler.Tests")]

// Reads DiagnosticDescriptors so the end-to-end coverage guard enumerates the real descriptor set
// instead of a hand-maintained copy of it.
[assembly: InternalsVisibleTo("BlazorCodeFirst.DiagnosticTests")]
