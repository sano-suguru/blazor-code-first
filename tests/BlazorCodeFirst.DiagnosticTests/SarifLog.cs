using System.Collections.Immutable;
using System.Text.Json;

namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>
/// Reads the SARIF 2.1 log csc writes when <c>ErrorLog</c> is set.  Console output is not parsed
/// instead: it is localized, it truncates, and it does not distinguish "reported with no location"
/// from "reported at the project file".
/// </summary>
internal static class SarifLog
{
    public static ImmutableArray<BuildDiagnostic> Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var diagnostics = ImmutableArray.CreateBuilder<BuildDiagnostic>();

        foreach (var run in document.RootElement.GetProperty("runs").EnumerateArray())
        {
            var rules = run.TryGetProperty("tool", out var tool) &&
                tool.TryGetProperty("driver", out var driver) &&
                driver.TryGetProperty("rules", out var declaredRules)
                    ? declaredRules
                    : default;

            if (!run.TryGetProperty("results", out var results))
                continue;

            foreach (var result in results.EnumerateArray())
                diagnostics.Add(ReadResult(result, rules));
        }

        return diagnostics.ToImmutable();
    }

    private static BuildDiagnostic ReadResult(JsonElement result, JsonElement rules)
    {
        var id = result.GetProperty("ruleId").GetString() ?? string.Empty;
        var message = result.TryGetProperty("message", out var messageElement) &&
            messageElement.TryGetProperty("text", out var text)
                ? text.GetString() ?? string.Empty
                : string.Empty;

        var region = ReadLocation(result);

        return new BuildDiagnostic(
            id,
            ReadSeverity(result, rules),
            region.FilePath,
            region.Line,
            region.Column,
            region.EndLine,
            region.EndColumn,
            message);
    }

    /// <summary>
    /// SARIF omits <c>level</c> when it matches the rule's own default configuration, so the rule
    /// table is the fallback, and SARIF's own default ("warning") the last resort.
    /// </summary>
    private static string ReadSeverity(JsonElement result, JsonElement rules)
    {
        if (result.TryGetProperty("level", out var level) && level.GetString() is { } explicitLevel)
            return explicitLevel;

        if (rules.ValueKind == JsonValueKind.Array &&
            result.TryGetProperty("ruleIndex", out var indexElement) &&
            indexElement.TryGetInt32(out var index) &&
            index >= 0 && index < rules.GetArrayLength() &&
            rules[index].TryGetProperty("defaultConfiguration", out var configuration) &&
            configuration.TryGetProperty("level", out var defaultLevel) &&
            defaultLevel.GetString() is { } ruleLevel)
        {
            return ruleLevel;
        }

        return "warning";
    }

    private static (string FilePath, int Line, int Column, int EndLine, int EndColumn) ReadLocation(
        JsonElement result)
    {
        var none = (string.Empty, 0, 0, 0, 0);

        if (!result.TryGetProperty("locations", out var locations) ||
            locations.ValueKind != JsonValueKind.Array ||
            locations.GetArrayLength() == 0)
        {
            return none;
        }

        if (!locations[0].TryGetProperty("physicalLocation", out var physical))
            return none;

        var filePath = physical.TryGetProperty("artifactLocation", out var artifact) &&
            artifact.TryGetProperty("uri", out var uri) &&
            uri.GetString() is { } uriText
                ? ToLocalPath(uriText)
                : string.Empty;

        if (!physical.TryGetProperty("region", out var region))
            return (filePath, 0, 0, 0, 0);

        var line = ReadInt(region, "startLine");
        var column = ReadInt(region, "startColumn");

        // SARIF omits endLine when the region does not span lines, and endColumn when it ends where it
        // starts; both then default to the start.
        var endLine = region.TryGetProperty("endLine", out _) ? ReadInt(region, "endLine") : line;
        var endColumn = region.TryGetProperty("endColumn", out _) ? ReadInt(region, "endColumn") : column;

        return (filePath, line, column, endLine, endColumn);
    }

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string ToLocalPath(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile
            ? parsed.LocalPath
            : uri;
}
