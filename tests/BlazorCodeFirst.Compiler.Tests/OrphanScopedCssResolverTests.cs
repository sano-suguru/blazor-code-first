using BlazorCodeFirst.Compiler.Analysis;
using Xunit;

namespace BlazorCodeFirst.Compiler.Tests;

public class OrphanScopedCssResolverTests
{
    [Fact]
    public void CssFileWithNoMatchingComponentOrViewPart_ReportsBCF3041()
    {
        var cssScopes = CssScopeRegistry.Create(
            [new CssScopeEntry("/repo/App/Orphan.cs.css", "bcf-abcd1234")]);

        var diagnostics = OrphanScopedCssResolver.CollectOrphanDiagnostics(
            cssScopes,
            componentFilePaths: [],
            viewPartFilePaths: []);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BCF3041", diagnostic.Id);
    }

    [Fact]
    public void CssFileWithMatchingComponent_ReportsNothing()
    {
        var cssScopes = CssScopeRegistry.Create(
            [new CssScopeEntry("/repo/App/Counter.cs.css", "bcf-abcd1234")]);

        var diagnostics = OrphanScopedCssResolver.CollectOrphanDiagnostics(
            cssScopes,
            componentFilePaths: ["/repo/App/Counter.cs"],
            viewPartFilePaths: []);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CssFileWithOnlyAViewPartDeclaration_ReportsNothing()
    {
        var cssScopes = CssScopeRegistry.Create(
            [new CssScopeEntry("/repo/App/Widgets.cs.css", "bcf-abcd1234")]);

        var diagnostics = OrphanScopedCssResolver.CollectOrphanDiagnostics(
            cssScopes,
            componentFilePaths: [],
            viewPartFilePaths: ["/repo/App/Widgets.cs"]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void NoCssFiles_ReportsNothing()
    {
        var diagnostics = OrphanScopedCssResolver.CollectOrphanDiagnostics(
            CssScopeRegistry.Empty,
            componentFilePaths: ["/repo/App/Counter.cs"],
            viewPartFilePaths: []);

        Assert.Empty(diagnostics);
    }
}
