using System.Collections;
using Microsoft.Build.Framework;

namespace BlazorCodeFirst.Build.Tests;

/// <summary>
/// Minimal <see cref="IBuildEngine"/> stand-in shared by every MSBuild task test in this project.
/// Records logged errors into <see cref="Errors"/> so a test can assert on what a task reported;
/// a test that never logs an error never populates the list, so recording unconditionally costs
/// nothing for tests that only care whether the task succeeded.
/// </summary>
internal sealed class StubBuildEngine : IBuildEngine
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
