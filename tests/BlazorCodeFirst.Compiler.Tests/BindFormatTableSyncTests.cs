using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds the two framework tables a formatted binding is emitted against — <c>CreateBinder</c>'s
/// format-taking overloads and <c>BindConverter.FormatValue</c>'s — in agreement with each other.
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

    [Fact]
    public void CreateBinder_DeclaresFormatOverloadsForTheDateAndTimeTypesOnly()
    {
        var compilation = CompilationTestHost.CreateCompilation("class Empty { }");
        var binder = compilation.GetTypeByMetadataName(
            "Microsoft.AspNetCore.Components.EventCallbackFactoryBinderExtensions");

        Assert.NotNull(binder);

        // The bound value's type in each format-taking overload: the setter parameter's first type
        // argument, which is the bound type in both the Action<T> and the Func<T, Task> shape.
        List<string> bound =
        [
            .. binder!.GetMembers("CreateBinder")
                .OfType<IMethodSymbol>()
                .Where(HasFormatParameter)
                .Select(method => method.Parameters[2].Type)
                .OfType<INamedTypeSymbol>()
                .Where(setter => setter.TypeArguments.Length > 0)
                .Select(setter => setter.TypeArguments[0].ToDisplayString())
                .Distinct()
                .OrderBy(name => name, System.StringComparer.Ordinal),
        ];

        Assert.Equal(ExpectedFormatTakingTypes, bound);
    }

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
                .Where(HasFormatParameter)
                .Select(method => method.Parameters[0].Type.ToDisplayString())
                .Distinct()
                .OrderBy(name => name, System.StringComparer.Ordinal),
        ];

        Assert.Equal(ExpectedFormatTakingTypes, formatted);
    }

    private static bool HasFormatParameter(IMethodSymbol method) =>
        method.Parameters.Any(p =>
            p.Name == "format" && p.Type.SpecialType == SpecialType.System_String);
}
