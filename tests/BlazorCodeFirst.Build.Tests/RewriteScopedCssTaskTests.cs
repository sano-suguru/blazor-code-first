using Microsoft.Build.Utilities;
using Xunit;

namespace BlazorCodeFirst.Build.Tests;

public sealed class RewriteScopedCssTaskTests : IDisposable
{
    private readonly DirectoryInfo _tempDir = Directory.CreateTempSubdirectory("bcf-rewrite-task-test-");

    public void Dispose() => _tempDir.Delete(recursive: true);

    [Fact]
    public void Execute_writes_the_rewritten_file_when_there_are_no_errors()
    {
        var inputPath = Path.Combine(_tempDir.FullName, "Counter.cs.css");
        var outputPath = Path.Combine(_tempDir.FullName, "Counter.cs.css.rz.scp.css");
        File.WriteAllText(inputPath, ".my-component { color: red; }");

        var item = new TaskItem(inputPath);
        item.SetMetadata("OutputFile", outputPath);
        item.SetMetadata("CssScope", "bcf-abcd1234");

        var task = new RewriteScopedCssTask
        {
            BuildEngine = new StubBuildEngine(),
            FilesToRewrite = [item],
        };

        var succeeded = task.Execute();

        Assert.True(succeeded);
        Assert.Equal(".my-component[bcf-abcd1234] { color: red; }", File.ReadAllText(outputPath));
    }

    [Fact]
    public void Execute_creates_each_items_output_directory_even_when_they_differ()
    {
        var inputPathA = Path.Combine(_tempDir.FullName, "Counter.cs.css");
        var outputPathA = Path.Combine(_tempDir.FullName, "a", "Counter.cs.css.rz.scp.css");
        File.WriteAllText(inputPathA, ".counter { color: red; }");

        var inputPathB = Path.Combine(_tempDir.FullName, "Nav.cs.css");
        var outputPathB = Path.Combine(_tempDir.FullName, "b", "Nav.cs.css.rz.scp.css");
        File.WriteAllText(inputPathB, ".nav { color: blue; }");

        var itemA = new TaskItem(inputPathA);
        itemA.SetMetadata("OutputFile", outputPathA);
        itemA.SetMetadata("CssScope", "bcf-a");

        var itemB = new TaskItem(inputPathB);
        itemB.SetMetadata("OutputFile", outputPathB);
        itemB.SetMetadata("CssScope", "bcf-b");

        var task = new RewriteScopedCssTask
        {
            BuildEngine = new StubBuildEngine(),
            FilesToRewrite = [itemA, itemB],
        };

        var succeeded = task.Execute();

        Assert.True(succeeded);
        Assert.Equal(".counter[bcf-a] { color: red; }", File.ReadAllText(outputPathA));
        Assert.Equal(".nav[bcf-b] { color: blue; }", File.ReadAllText(outputPathB));
    }

    [Fact]
    public void Execute_logs_an_error_and_does_not_write_the_output_file_for_an_import_statement()
    {
        var inputPath = Path.Combine(_tempDir.FullName, "Counter.cs.css");
        var outputPath = Path.Combine(_tempDir.FullName, "Counter.cs.css.rz.scp.css");
        File.WriteAllText(inputPath, "@import \"other.css\";\n.my-component { color: red; }");

        var item = new TaskItem(inputPath);
        item.SetMetadata("OutputFile", outputPath);
        item.SetMetadata("CssScope", "bcf-abcd1234");

        var buildEngine = new StubBuildEngine();
        var task = new RewriteScopedCssTask
        {
            BuildEngine = buildEngine,
            FilesToRewrite = [item],
        };

        var succeeded = task.Execute();

        Assert.False(succeeded);
        Assert.Single(buildEngine.Errors);
        Assert.Contains("@import", buildEngine.Errors[0].Message);
        Assert.False(File.Exists(outputPath));
    }
}
