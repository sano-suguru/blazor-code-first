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
        foreach (var item in FilesToRewrite)
        {
            var inputPath = item.GetMetadata("FullPath");
            var outputPath = item.GetMetadata("OutputFile");
            var scope = item.GetMetadata("CssScope");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var css = File.ReadAllText(inputPath);
            var rewritten = FlatSelectorCssRewriter.Rewrite(css, scope);
            File.WriteAllText(outputPath, rewritten);
        }

        return true;
    }
}
