using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Reports BCF3041 for every <c>.cs.css</c> file in <see cref="CssScopeRegistry"/> whose matching
/// <c>.cs</c> file (its own path with the trailing <c>.css</c> removed) declares neither a component
/// nor a <c>[ViewPart]</c> method. A <c>[ViewPart]</c>-only file counts as a match because its scope
/// still reaches rendered elements through expansion at every call site
/// (<c>ViewPartExpander.ExpandCall</c>), so lacking a component of its own does not make it orphaned.
/// </summary>
internal static class OrphanScopedCssResolver
{
    internal static ImmutableArray<DiagnosticInfo> CollectOrphanDiagnostics(
        CssScopeRegistry cssScopes,
        ImmutableArray<string> componentFilePaths,
        ImmutableArray<string> viewPartFilePaths)
    {
        if (cssScopes.Entries.Length == 0)
            return [];

        var known = new HashSet<string>(componentFilePaths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in viewPartFilePaths)
            known.Add(path);

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        foreach (var entry in cssScopes.Entries)
        {
            if (known.Contains(entry.ComponentFilePath))
                continue;

            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3041,
                Location.Create(entry.CssFilePath, default, default),
                [System.IO.Path.GetFileName(entry.CssFilePath), System.IO.Path.GetFileName(entry.ComponentFilePath)]));
        }

        return diagnostics.ToImmutable();
    }
}
