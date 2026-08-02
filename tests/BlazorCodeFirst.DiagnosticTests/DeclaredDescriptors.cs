using System.Collections.Immutable;
using System.Reflection;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>
/// Every <see cref="DiagnosticDescriptor"/> the compiler declares, read by reflection so a new one is
/// picked up by each guard that consumes this without anyone remembering to register it.
/// </summary>
internal static class DeclaredDescriptors
{
    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
        [.. typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(static field => (DiagnosticDescriptor)field.GetValue(null)!)];

    public static ImmutableHashSet<string> Ids { get; } =
        All.Select(static descriptor => descriptor.Id).ToImmutableHashSet(StringComparer.Ordinal);

    public static ImmutableDictionary<string, DiagnosticDescriptor> ById { get; } =
        All.ToImmutableDictionary(static descriptor => descriptor.Id, StringComparer.Ordinal);
}
