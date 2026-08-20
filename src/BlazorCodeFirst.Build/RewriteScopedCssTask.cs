using Microsoft.Build.Framework;

namespace BlazorCodeFirst.Build;

// Base class spelled out fully: see the comment on ComputeScopedCssScopeTask.
/// <summary>
/// MSBuild task that rewrites each input's scoped CSS to its output path, attaching the
/// <c>CssScope</c> metadata <see cref="ComputeScopedCssScopeTask"/> computed for it.
/// </summary>
public sealed class RewriteScopedCssTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// The items to rewrite. Each carries <c>FullPath</c> (the source file), <c>OutputFile</c>
    /// (the rewritten destination), and <c>CssScope</c> (the identifier to inject) as metadata.
    /// </summary>
    [Required]
    public ITaskItem[] FilesToRewrite { get; set; } = [];

    /// <inheritdoc />
    public override bool Execute()
    {
        foreach (var item in FilesToRewrite)
        {
            var inputPath = item.GetMetadata("FullPath");
            var outputPath = item.GetMetadata("OutputFile");
            var scope = item.GetMetadata("CssScope");

            var css = File.ReadAllText(inputPath);
            var rewritten = ScopedCssRewriter.Rewrite(inputPath, css, scope, out var errors);

            if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    Log.LogError(
                        subcategory: null,
                        errorCode: null,
                        helpKeyword: null,
                        file: error.FilePath,
                        lineNumber: error.Line,
                        columnNumber: error.Column,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: error.Message);
                }

                continue;
            }

            var outputDirectory = Path.GetDirectoryName(outputPath)!;
            Directory.CreateDirectory(outputDirectory);

            File.WriteAllText(outputPath, rewritten);
        }

        return !Log.HasLoggedErrors;
    }
}
