using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BlazorCodeFirst.Build;

// Base class spelled out fully (not the bare "Task"): the root Directory.Build.props enables
// ImplicitUsings, which brings in System.Threading.Tasks.Task and makes the bare name ambiguous
// with Microsoft.Build.Utilities.Task.
public sealed class ComputeScopedCssScopeTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] ScopedCssInputs { get; set; } = [];

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    [Required]
    public string AssemblyName { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] ScopedCssWithScope { get; set; } = [];

    public override bool Execute()
    {
        var result = new ITaskItem[ScopedCssInputs.Length];

        for (var i = 0; i < ScopedCssInputs.Length; i++)
        {
            var input = ScopedCssInputs[i];
            var fullPath = input.GetMetadata("FullPath");
            var scope = ScopeIdentifier.Compute(ProjectDirectory, fullPath, AssemblyName);

            var withScope = new TaskItem(input);
            withScope.SetMetadata("CssScope", scope);
            result[i] = withScope;
        }

        ScopedCssWithScope = result;
        return true;
    }
}
