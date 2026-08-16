using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// BCF3038: an event modifier the event's own <c>[EventHandler]</c> registration disables.
/// </summary>
/// <remarks>
/// Every arm of this diagnostic consults a registration, and most of these ask about one the framework
/// ships, so they run against the default compilation that references
/// <c>Microsoft.AspNetCore.Components.Web</c>. The pair that does not —
/// <see cref="StopPropagation_WithoutComponentsWeb_IsSilent"/> and
/// <see cref="StopPropagation_OnASourceRegistration_WithoutComponentsWeb_IsReported"/> — is what makes
/// "reported" here mean a registration was read rather than that anything was analyzed, and what
/// separates the two tables (#396).
/// </remarks>
public sealed class EventModifierSupportDiagnosticTests
{
    private static (string Path, string Source)[] HostFiles(string body, string members = "") =>
        [
            ("Host.cs", $$"""
                using System;
                using BlazorCodeFirst;
                using Microsoft.AspNetCore.Components;
                using Microsoft.AspNetCore.Components.Web;
                using static BlazorCodeFirst.Html;

                namespace T;

                public partial class Host : BodyComponentBase
                {
                    private string _text = "";
                    private void Go() { }
                    {{members}}
                    protected override View Body => {{body}};
                }
                """),
        ];

    private static ImmutableArray<Diagnostic> Run(string body, string members = "") =>
        CompilationTestHost.RunGenerator(HostFiles(body, members)).Diagnostics;

    /// <summary>
    /// <c>oncancel</c> is one of the two registrations that disable <c>stopPropagation</c>, measured on
    /// <c>Microsoft.AspNetCore.Components.Web</c> 10.0.10 (#369).
    /// </summary>
    [Fact]
    public void StopPropagation_OnAnEventThatDisablesIt_ReportsBCF3038()
    {
        var diagnostics = Run("""Div.On("oncancel", Go).StopPropagation()""");

        var reported = Assert.Single(diagnostics, static d => d.Id == "BCF3038");
        var message = reported.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("StopPropagation", message, System.StringComparison.Ordinal);
        Assert.Contains("oncancel", message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The same event accepts <c>preventDefault</c>, which all 96 framework registrations enable.
    /// </summary>
    /// <remarks>
    /// The pair with the case above is what pins the constructor's argument order, which is
    /// <c>(attributeName, eventArgsType, enableStopPropagation, enablePreventDefault)</c> and not the
    /// intuitive one. Reading the two flags the other way round makes exactly these two tests swap.
    /// </remarks>
    [Fact]
    public void PreventDefault_OnTheEventThatDisablesStopPropagation_ReportsNothing()
    {
        var diagnostics = Run("""Div.On("oncancel", Go).PreventDefault()""");

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3038");
    }

    /// <summary>
    /// An event the table does not carry has nothing to disagree with, so the check is skipped.
    /// </summary>
    [Fact]
    public void StopPropagation_OnAnUnregisteredEvent_ReportsNothing()
    {
        var diagnostics = Run("""Div.On("onnotarealevent", Go).StopPropagation()""");

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3038");
    }

    /// <summary>
    /// A custom registration accepts the modifier only if its constructor enabled it.
    /// </summary>
    /// <remarks>
    /// This is what makes BCF3038 more than a rule about two framework events: the two-argument
    /// constructor declares neither flag and Blazor reads both as disabled, so an author's own event
    /// accepts no modifier until it is respelled with four.
    /// </remarks>
    [Theory]
    [InlineData("typeof(EventArgs)", true)]
    [InlineData("typeof(EventArgs), true, true", false)]
    public void StopPropagation_OnACustomRegistration_FollowsItsConstructor(
        string arguments, bool reported)
    {
        var diagnostics = Run(
            """Div.On("oncustom", Go).StopPropagation()""",
            $$"""[EventHandler("oncustom", {{arguments}})] public static class CustomEvents { }""");

        Assert.Equal(reported, diagnostics.Any(static d => d.Id == "BCF3038"));
    }

    /// <summary>
    /// The modifier on a binding's own event reaches the same check, because it reaches the same
    /// resolution point (#370).
    /// </summary>
    [Fact]
    public void StopPropagation_OnABindingWhoseEventDisablesIt_ReportsBCF3038()
    {
        var diagnostics = Run(
            """Input.Bind("value", "oncancel", () => _text, v => _text = v).StopPropagation()""");

        Assert.Single(diagnostics, static d => d.Id == "BCF3038");
    }

    /// <summary>
    /// A compilation that cannot see the framework's registrations skips the check in silence for an
    /// event only they name, rather than reporting.
    /// </summary>
    /// <remarks>
    /// Paired with <see cref="StopPropagation_WithComponentsWeb_IsReported"/>, which runs the very same
    /// source with that one reference present. Without the pair, "no diagnostic" would hold just as well if
    /// the body had never been analyzed. <c>oncancel</c> is a framework registration, and
    /// <see cref="StopPropagation_OnASourceRegistration_WithoutComponentsWeb_IsReported"/> is the same
    /// compilation reporting on one the compilation declares itself (#396).
    /// </remarks>
    [Fact]
    public void StopPropagation_WithoutComponentsWeb_IsSilent()
    {
        var result = CompilationTestHost.RunGenerator(
            CompilationTestHost.CreateCompilationWithoutComponentsWeb(WebFreeHost));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3038");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void StopPropagation_WithComponentsWeb_IsReported()
    {
        var result = CompilationTestHost.RunGenerator(
            CompilationTestHost.CreateCompilation(WebFreeHost));

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3038");
    }

    /// <summary>
    /// An author's own registration is read without <c>Components.Web</c>, so the modifier it refuses is
    /// reported there too (#396).
    /// </summary>
    /// <remarks>
    /// The two-argument constructor is the whole of the case: it declares neither flag, so this event
    /// refuses both modifiers, and a compilation with no framework table beside it still has this one to
    /// disagree with.
    /// </remarks>
    [Fact]
    public void StopPropagation_OnASourceRegistration_WithoutComponentsWeb_IsReported()
    {
        var result = CompilationTestHost.RunGenerator(
            CompilationTestHost.CreateCompilationWithoutComponentsWeb(
                ("Host.cs", """
                    using System;
                    using BlazorCodeFirst;
                    using Microsoft.AspNetCore.Components;
                    using static BlazorCodeFirst.Html;

                    namespace T;

                    [EventHandler("oncustom", typeof(EventArgs))]
                    public static class CustomHandlers;

                    public partial class Host : BodyComponentBase
                    {
                        private void Go() { }
                        protected override View Body => Div.On("oncustom", Go).StopPropagation();
                    }
                    """)));

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3038");
    }

    /// <summary>
    /// The refused combination, spelled without naming any type from <c>Components.Web</c> so that the
    /// same source compiles with and without that reference, which is what lets one array serve both
    /// halves of the pair above.
    /// </summary>
    private static readonly (string Path, string Source)[] WebFreeHost =
        [
            ("Host.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace T;

                public partial class Host : BodyComponentBase
                {
                    private void Go() { }
                    protected override View Body => Div.On("oncancel", Go).StopPropagation();
                }
                """),
        ];
}
