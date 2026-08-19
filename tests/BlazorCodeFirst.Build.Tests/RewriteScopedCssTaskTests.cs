using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlazorCodeFirst.Build;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace BlazorCodeFirst.Build.Tests;

public class RewriteScopedCssTaskTests
{
    [Fact]
    public void Execute_writes_the_rewritten_file_when_there_are_no_errors()
    {
        var tempDir = Directory.CreateTempSubdirectory("bcf-rewrite-task-test-");
        try
        {
            var inputPath = Path.Combine(tempDir.FullName, "Counter.cs.css");
            var outputPath = Path.Combine(tempDir.FullName, "Counter.cs.css.rz.scp.css");
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
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Execute_logs_an_error_and_does_not_write_the_output_file_for_an_import_statement()
    {
        var tempDir = Directory.CreateTempSubdirectory("bcf-rewrite-task-test-");
        try
        {
            var inputPath = Path.Combine(tempDir.FullName, "Counter.cs.css");
            var outputPath = Path.Combine(tempDir.FullName, "Counter.cs.css.rz.scp.css");
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
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    private sealed class StubBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(
            string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) =>
            true;

        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }
}
