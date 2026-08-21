using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BlazorCodeFirst.Build;

// Base class spelled out fully (not the bare "Task"): the root Directory.Build.props enables
// ImplicitUsings, which brings in System.Threading.Tasks.Task and makes the bare name ambiguous
// with Microsoft.Build.Utilities.Task.
/// <summary>
/// MSBuild task that assigns each scoped CSS input file the <c>CssScope</c> metadata
/// <see cref="ScopeIdentifier.Compute(string, string, string)"/> derives for it.
/// </summary>
public sealed class ComputeScopedCssScopeTask : Microsoft.Build.Utilities.Task
{
    /// <summary>The <c>.razor.css</c> items to compute a scope identifier for.</summary>
    [Required]
    public ITaskItem[] ScopedCssInputs { get; set; } = [];

    /// <summary>The consuming project's directory, used to derive each item's project-relative path.</summary>
    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <summary>The consuming project's assembly name, mixed into the scope identifier's hash input.</summary>
    [Required]
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary><see cref="ScopedCssInputs"/>, each carrying its computed <c>CssScope</c> metadata.</summary>
    [Output]
    public ITaskItem[] ScopedCssWithScope { get; set; } = [];

    /// <inheritdoc />
    public override bool Execute()
    {
        var result = new ITaskItem[ScopedCssInputs.Length];

        for (var i = 0; i < ScopedCssInputs.Length; i++)
        {
            var input = ScopedCssInputs[i];
            var fullPath = input.GetMetadata("FullPath");
            var scope = ScopeIdentifier.Compute(ProjectDirectory, fullPath, AssemblyName);

            var withScope = new TaskItem(input) { ItemSpec = fullPath };
            withScope.SetMetadata("CssScope", scope);
            result[i] = withScope;
        }

        ScopedCssWithScope = result;
        return true;
    }
}
