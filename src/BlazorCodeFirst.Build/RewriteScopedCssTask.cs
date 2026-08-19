using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BlazorCodeFirst.Build;

// Base class spelled out fully: see the comment on ComputeScopedCssScopeTask.
public sealed class RewriteScopedCssTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] FilesToRewrite { get; set; } = [];

    public override bool Execute()
    {
        // Every current caller writes all outputs under the same directory, but tracking directories
        // already created (rather than hoisting one CreateDirectory call before the loop) stays
        // correct if a future caller's OutputFile values ever span more than one directory.
        var createdDirectories = new HashSet<string>();

        foreach (var item in FilesToRewrite)
        {
            var inputPath = item.GetMetadata("FullPath");
            var outputPath = item.GetMetadata("OutputFile");
            var scope = item.GetMetadata("CssScope");

            var outputDirectory = Path.GetDirectoryName(outputPath)!;
            if (createdDirectories.Add(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var css = File.ReadAllText(inputPath);
            var rewritten = ScopedCssRewriter.Rewrite(inputPath, css, scope, out _);
            File.WriteAllText(outputPath, rewritten);
        }

        return true;
    }
}
