using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The narrowest possible <see cref="AdditionalText"/>: a path and empty content. Generator tests
/// that exercise <c>CssScope</c> metadata need the path to exist on an <see cref="AdditionalText"/>
/// so <c>AnalyzerConfigOptionsProvider.GetOptions(AdditionalText)</c> has something to key off of;
/// the file's actual text content is never read by anything under test.
/// </summary>
internal sealed class TestAdditionalText(string path) : AdditionalText
{
    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default) =>
        SourceText.From(string.Empty);
}

/// <summary>
/// A per-<see cref="AdditionalText"/> options bag holding exactly the keys a test supplies. Mirrors
/// the one key shape this compiler reads today (<c>build_metadata.AdditionalFiles.CssScope</c>);
/// extend the dictionary passed to <see cref="TestAnalyzerConfigOptionsProvider"/> rather than this
/// type if a test needs a second key.
/// </summary>
internal sealed class TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
        values.TryGetValue(key, out value);
}

/// <summary>
/// Maps each <see cref="AdditionalText"/> to a <c>CssScope</c> value by path, the same
/// <c>build_metadata.AdditionalFiles.CssScope</c> key
/// <c>src/BlazorCodeFirst.Build/build/BlazorCodeFirst.props</c> declares as compiler-visible for the
/// real build. <see cref="GetOptions(SyntaxTree)"/> is never read by this compiler and always returns
/// an empty bag.
/// </summary>
internal sealed class TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> cssScopeByPath)
    : AnalyzerConfigOptionsProvider
{
    private static readonly AnalyzerConfigOptions Empty = new TestAnalyzerConfigOptions(
        new Dictionary<string, string>());

    public override AnalyzerConfigOptions GlobalOptions => Empty;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        cssScopeByPath.TryGetValue(textFile.Path, out var scope)
            ? new TestAnalyzerConfigOptions(
                new Dictionary<string, string> { ["build_metadata.AdditionalFiles.CssScope"] = scope })
            : Empty;
}
