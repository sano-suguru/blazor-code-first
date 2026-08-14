using System.Collections.Generic;
using System.Linq;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Xunit;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds the two framework tables a formatted binding is emitted against — <c>CreateBinder</c>'s
/// format-taking overloads, which BCF3031 reads, and <c>BindConverter.FormatValue</c>'s, which the
/// emitter writes the outbound half against — in agreement with each other.
/// </summary>
/// <remarks>
/// <para>
/// BCF3031 reads one table and the emitter writes two calls. That is only honest while the two tables name
/// the same types: a type admitted by the binder but not by the converter would pass the diagnostic and
/// then fail to bind inside the generated file, which is the very failure appendix A.0 says never reaches
/// the author. Placed the way <c>KnownSymbolsSyncTests</c> is placed, holding the curated and void tag
/// tables against each other rather than transcribing either.
/// </para>
/// <para>
/// The expected set is written out rather than derived, because deriving it from the same metadata the
/// production code reads would assert nothing. It is the framework's set, not this repository's: a change
/// here means the framework moved, and the failure is the notice.
/// </para>
/// </remarks>
public sealed class BindFormatTableSyncTests
{
    private static readonly string[] ExpectedFormatTakingTypes =
    [
        "System.DateOnly",
        "System.DateOnly?",
        "System.DateTime",
        "System.DateTime?",
        "System.DateTimeOffset",
        "System.DateTimeOffset?",
        "System.TimeOnly",
        "System.TimeOnly?",
    ];

    private static readonly string[] RejectedTypes =
        ["int", "int?", "string", "bool", "decimal", "System.Guid", "System.DayOfWeek"];

    /// <summary>
    /// Asked through <c>AcceptsBindFormat</c> rather than by walking <c>CreateBinder</c> here. Walking it
    /// again would restate the production reading — the format predicate, and which parameter carries the
    /// bound type — in a second place, and would stay green while the table BCF3031 actually consults was
    /// wrong. The expected list is still written out, so what this compares is a production answer against
    /// an independent claim.
    /// </summary>
    [Fact]
    public void AcceptsBindFormat_AdmitsTheDateAndTimeTypesAndNothingElse()
    {
        var (symbols, types) = Resolve([.. ExpectedFormatTakingTypes, .. RejectedTypes]);

        foreach (var name in ExpectedFormatTakingTypes)
            Assert.True(symbols.AcceptsBindFormat(types[name]), $"'{name}' should accept a format.");

        // The rejections are not only for their own sake. AcceptsBindFormat answers true for everything
        // when its table is empty — that is the check skipping itself on a compilation that cannot see the
        // framework — so without a type it turns down, a table that failed to build would satisfy every
        // assertion above.
        foreach (var name in RejectedTypes)
            Assert.False(symbols.AcceptsBindFormat(types[name]), $"'{name}' should not accept a format.");
    }

    /// <summary>
    /// The other table, walked directly. This one has no production reader to ask: the emitter writes
    /// <c>FormatValue</c> without consulting a set, so the only way to hold it against the binder's is to
    /// read it here.
    /// </summary>
    [Fact]
    public void FormatValue_AgreesWithCreateBinder()
    {
        var compilation = CompilationTestHost.CreateCompilation("class Empty { }");
        var converter = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.BindConverter");

        Assert.NotNull(converter);

        List<string> formatted =
        [
            .. converter!.GetMembers("FormatValue")
                .OfType<IMethodSymbol>()
                .Where(method => method.Parameters.Any(p =>
                    p.Name == "format" && p.Type.SpecialType == SpecialType.System_String))
                .Select(method => method.Parameters[0].Type.ToDisplayString())
                .Distinct()
                .OrderBy(name => name, System.StringComparer.Ordinal),
        ];

        Assert.Equal(ExpectedFormatTakingTypes, formatted);
    }

    /// <summary>
    /// Resolves each name as the compiler sees it, by declaring a field of that type and reading the
    /// field's type back. <c>GetTypeByMetadataName</c> cannot answer for <c>int?</c> or
    /// <c>System.DateOnly?</c>, and constructing <c>Nullable&lt;T&gt;</c> by hand here would be this file
    /// spelling out the very shapes it is checking.
    /// </summary>
    private static (KnownSymbols Symbols, Dictionary<string, ITypeSymbol> Types) Resolve(string[] names)
    {
        var fields = string.Join("\n", names.Select((name, index) => $"    public {name} F{index};"));
        var compilation = CompilationTestHost.CreateCompilation($"class Probe\n{{\n{fields}\n}}\n");

        var symbols = KnownSymbols.TryCreate(compilation);
        Assert.NotNull(symbols);

        var probe = compilation.GetTypeByMetadataName("Probe");
        Assert.NotNull(probe);

        var types = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
        for (var index = 0; index < names.Length; index++)
        {
            var field = Assert.IsAssignableFrom<IFieldSymbol>(Assert.Single(probe!.GetMembers($"F{index}")));
            types[names[index]] = field.Type;
        }

        return (symbols!, types);
    }
}
