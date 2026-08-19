using BlazorCodeFirst.Build;
using Microsoft.Build.Utilities;
using Xunit;

namespace BlazorCodeFirst.Build.Tests;

public class ComputeScopedCssScopeTaskTests
{
    [Fact]
    public void Execute_stamps_CssScope_metadata_on_every_input()
    {
        var task = new ComputeScopedCssScopeTask
        {
            BuildEngine = new StubBuildEngine(),
            ProjectDirectory = "/repo/App",
            AssemblyName = "App",
            ScopedCssInputs =
            [
                new TaskItem("/repo/App/Counter.cs.css"),
                new TaskItem("/repo/App/NavMenu.cs.css"),
            ],
        };

        var succeeded = task.Execute();

        Assert.True(succeeded);
        Assert.Equal(2, task.ScopedCssWithScope.Length);
        Assert.All(task.ScopedCssWithScope, item =>
            Assert.Matches("^bcf-[0-9a-f]{8}$", item.GetMetadata("CssScope")));
        Assert.NotEqual(
            task.ScopedCssWithScope[0].GetMetadata("CssScope"),
            task.ScopedCssWithScope[1].GetMetadata("CssScope"));
    }
}
