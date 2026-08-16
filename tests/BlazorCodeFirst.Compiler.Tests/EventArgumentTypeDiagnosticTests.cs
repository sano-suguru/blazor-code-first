using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// BCF3028: the argument type an event handler declares against the event it is written on.
/// </summary>
/// <remarks>
/// <para>
/// Two shapes, one descriptor. A handler whose argument type disagrees with the event's
/// <c>[EventHandler]</c> mapping binds, so it is reported from the decoration arm on the success path; a
/// <c>TArgs</c> that is not an <c>EventArgs</c> at all never binds, so it is reported from the
/// failure-path sweep, which is where BCF3008 reports from for the same reason (#155).
/// </para>
/// <para>
/// The delivery half of the second shape is pinned against a real build in
/// <c>tests/diagnostic-fixtures</c>: the C# error explaining it is computed for a compilation the
/// declaration-stage cutoff has already abandoned, so an in-process run is not evidence that anything
/// reaches an author. What is pinned here is which shapes report and which do not.
/// </para>
/// </remarks>
public sealed class EventArgumentTypeDiagnosticTests
{
    private static (string Path, string Source)[] HostFiles(string body, string members = "") =>
        [
            ("Host.cs", $$"""
                using System;
                using System.Threading.Tasks;
                using BlazorCodeFirst;
                using Microsoft.AspNetCore.Components;
                using Microsoft.AspNetCore.Components.Web;
                using static BlazorCodeFirst.Html;

                namespace T;

                public partial class Host : BodyComponentBase
                {
                    {{members}}
                    protected override View Body => {{body}};
                }
                """),
        ];

    private static ImmutableArray<Diagnostic> Run(string body, string members = "") =>
        RunResult(body, members).Diagnostics;

    private static GeneratorRunResult RunResult(string body, string members = "") =>
        CompilationTestHost.RunGenerator(HostFiles(body, members));

    /// <summary>
    /// The <c>Host.cs</c> source text <paramref name="diagnostic"/> is located on, which is its report
    /// anchor. Sliced out of the source the test supplied rather than read back through
    /// <c>Location.SourceTree</c>: a generator diagnostic crosses the symbol-free pipeline boundary as a
    /// file path and a span, so it is rebuilt as an external location and carries no tree.
    /// </summary>
    private static string HostSpanText(Diagnostic diagnostic, string body, string members = "")
    {
        var span = diagnostic.Location.SourceSpan;

        Assert.Equal("Host.cs", diagnostic.Location.GetLineSpan().Path);

        return HostFiles(body, members)[0].Source.Substring(span.Start, span.Length);
    }

    // ---------------------------------------------------------------------------
    // The mismatch that binds: reported from the decoration arm
    // ---------------------------------------------------------------------------

    [Fact]
    public void HandlerArgumentTypeUnrelatedToTheEvent_ReportsBCF3028_AtTheHandler()
    {
        const string Body = """Button.On("onclick", (KeyboardEventArgs e) => { })["Go"]""";

        var report = Assert.Single(Run(Body), static d => d.Id == "BCF3028");

        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
        Assert.Equal("(KeyboardEventArgs e) => { }", HostSpanText(report, Body));

        var message = report.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("KeyboardEventArgs", message, StringComparison.Ordinal);
        Assert.Contains("onclick", message, StringComparison.Ordinal);
        Assert.Contains("MouseEventArgs", message, StringComparison.Ordinal);
    }

    /// <summary>The asynchronous overload carries the same type argument and the same defect.</summary>
    [Fact]
    public void AsyncHandlerArgumentTypeUnrelatedToTheEvent_ReportsBCF3028()
    {
        var diagnostics = Run("""Button.On("onclick", (KeyboardEventArgs e) => Task.CompletedTask)["Go"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3028");
    }

    /// <summary>
    /// The test is assignability, not equality: an <c>EventCallback&lt;EventArgs&gt;</c> handed a
    /// <c>MouseEventArgs</c> works, so a base type stays legitimate.
    /// </summary>
    [Fact]
    public void BaseOfTheDeliveredArgumentType_IsAccepted()
    {
        var result = RunResult("""Button.On("onclick", (EventArgs e) => { })["Go"]""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3028");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void DeliveredArgumentType_IsAccepted()
    {
        var result = RunResult("""Input.On("oninput", (ChangeEventArgs e) => { })""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3028");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// An event with no <c>[EventHandler]</c> entry has no mapping to disagree with, so nothing is
    /// reported however the handler is typed.
    /// </summary>
    [Fact]
    public void EventWithNoRegistration_IsNotReported()
    {
        var result = RunResult("""Div.On("oncustomthing", (MouseEventArgs e) => { })""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3028");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>The argument-less overloads carry no type argument and are outside this rule.</summary>
    [Fact]
    public void UntypedHandler_IsNotReported()
    {
        var result = RunResult("""Button.OnClick(() => { }).On("onmouseenter", () => { })["Go"]""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3028");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    // ---------------------------------------------------------------------------
    // The constraint violation that does not bind: reported from the failure path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// <c>where TArgs : System.EventArgs</c> makes this call fail to bind, so the decoration arm never sees
    /// it. What the author was left with is BCF1003's "not statically analyzable", said of an expression
    /// that is perfectly analyzable (measured on #155).
    /// </summary>
    [Fact]
    public void ArgumentTypeThatIsNotAnEventArgs_ReportsBCF3028_AtTheHandler()
    {
        const string Body = """Button.On("onclick", (int x) => { })["Go"]""";

        var diagnostics = Run(Body);
        var report = Assert.Single(diagnostics, static d => d.Id == "BCF3028");

        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
        Assert.Equal("(int x) => { }", HostSpanText(report, Body));
        Assert.Contains("int", report.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        // The generic fallback is what this replaces, and it is suppressed because an error of its own was
        // recorded. A run reporting both would mean the author still has to read past BCF1003.
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF1003");
    }

    /// <summary>
    /// The same defect on an event no registration answers for. The constraint half of the rule does not
    /// consult a registration at all, which is why the fixture pinning its delivery asks nothing of what
    /// tables that project can see.
    /// </summary>
    [Fact]
    public void ArgumentTypeThatIsNotAnEventArgs_OnAnUnmappedEvent_ReportsBCF3028()
    {
        var diagnostics = Run("""Button.On("oncustomthing", (int x) => { })["Go"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3028");
    }

    /// <summary>
    /// A missing handler leaves <c>TArgs</c> uninferred, so the candidate carries the type parameter itself
    /// rather than a type the author wrote. Reporting it would blame a shape whose real mistake is the
    /// missing argument, which stays BCF1003.
    /// </summary>
    /// <remarks>
    /// The scanner's explicit type-parameter exclusion is not what holds this: dropping it leaves the test
    /// green, measured, because the unsubstituted parameter's <c>BaseType</c> is the <c>System.EventArgs</c>
    /// the decoration's own constraint declares and the assignability walk accepts it. The exclusion is
    /// written out there anyway, for the reason recorded on it.
    /// </remarks>
    [Fact]
    public void MissingHandlerArgument_StaysBCF1003()
    {
        var diagnostics = Run("""Div.On("onclick")["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3028");
    }

    /// <summary>
    /// A receiver that opens no element frame keeps BCF3008, the more fundamental mistake.
    /// </summary>
    /// <remarks>
    /// Not the branch order in <c>RejectedDecorationScanner</c>: reordering the two leaves this green,
    /// measured. Roslyn offers no candidate symbols at all for a receiver an extension method cannot take,
    /// and BCF3028 reads the type argument off those candidates, so the mistyped-handler branch never sees
    /// this shape. What the test pins is the outcome an author gets, which is what has to hold whichever
    /// mechanism delivers it.
    /// </remarks>
    [Fact]
    public void MistypedHandlerOnANonElement_StaysBCF3008()
    {
        var diagnostics = Run("""Fragment("a").On("onclick", (int x) => { })""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3008");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3028");
    }

    // ---------------------------------------------------------------------------
    // Where the mapping is read from
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A registration in the compilation under analysis is read, that being how a custom event is
    /// registered at all.
    /// </summary>
    [Fact]
    public void RegistrationInTheCompilation_IsRead()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Host.cs", """
                using BlazorCodeFirst;
                using Microsoft.AspNetCore.Components;
                using Microsoft.AspNetCore.Components.Web;
                using static BlazorCodeFirst.Html;

                namespace T;

                public sealed class RatingEventArgs : System.EventArgs
                {
                    public int Stars { get; set; }
                }

                [EventHandler("onrate", typeof(RatingEventArgs))]
                public static class AppEventHandlers;

                public partial class Host : BodyComponentBase
                {
                    protected override View Body => Div.On("onrate", (MouseEventArgs e) => { });
                }
                """));

        var report = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3028");
        Assert.Contains(
            "RatingEventArgs", report.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>The same registration accepts the type it registers.</summary>
    [Fact]
    public void RegistrationInTheCompilation_AcceptsTheTypeItRegisters()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Host.cs", """
                using BlazorCodeFirst;
                using Microsoft.AspNetCore.Components;
                using static BlazorCodeFirst.Html;

                namespace T;

                public sealed class RatingEventArgs : System.EventArgs
                {
                    public int Stars { get; set; }
                }

                [EventHandler("onrate", typeof(RatingEventArgs))]
                public static class AppEventHandlers;

                public partial class Host : BodyComponentBase
                {
                    protected override View Body => Div.On("onrate", (RatingEventArgs e) => { });
                }
                """));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3028");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// The framework's table is read first, so a registration in the compilation cannot displace one of its
    /// names. That is also the right answer, because Blazor's own dispatch uses the framework's type.
    /// </summary>
    [Fact]
    public void RegistrationInTheCompilation_DoesNotDisplaceTheFrameworkTable()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Host.cs", """
                using BlazorCodeFirst;
                using Microsoft.AspNetCore.Components;
                using static BlazorCodeFirst.Html;

                namespace T;

                public sealed class RatingEventArgs : System.EventArgs;

                [EventHandler("onclick", typeof(RatingEventArgs))]
                public static class AppEventHandlers;

                public partial class Host : BodyComponentBase
                {
                    protected override View Body => Div.On("onclick", (RatingEventArgs e) => { });
                }
                """));

        var report = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3028");
        Assert.Contains(
            "MouseEventArgs", report.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// One event name registered twice with two different types is left out of the map entirely, rather
    /// than resolved by a walk order nobody wrote down. Silence under ambiguity is the under-reporting
    /// direction, which is the safe one here.
    /// </summary>
    [Fact]
    public void EventNameRegisteredTwiceWithDifferentTypes_IsNotReported()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Host.cs", """
                using BlazorCodeFirst;
                using Microsoft.AspNetCore.Components;
                using static BlazorCodeFirst.Html;

                namespace T;

                public sealed class RatingEventArgs : System.EventArgs;

                public sealed class SwipeEventArgs : System.EventArgs;

                public sealed class UnrelatedEventArgs : System.EventArgs;

                [EventHandler("onrate", typeof(RatingEventArgs))]
                public static class FirstHandlers;

                [EventHandler("onrate", typeof(SwipeEventArgs))]
                public static class SecondHandlers;

                public partial class Host : BodyComponentBase
                {
                    protected override View Body => Div.On("onrate", (UnrelatedEventArgs e) => { });
                }
                """));

        // The handler's type is a sibling of both registrations, so either one alone would report. It is the
        // ambiguity that makes this silent, not the handler being acceptable to some registration.
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3028");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A registration in a referenced assembly is deliberately not swept, and this is the residue that
    /// records it rather than a note about one: #155 could not bound what sweeping every reference costs.
    /// </summary>
    [Fact]
    public void RegistrationInAReferencedAssembly_IsNotRead()
    {
        var reference = CompilationTestHost.CompileToMetadataReference(
            """
            using Microsoft.AspNetCore.Components;

            namespace Lib;

            public sealed class SwipeEventArgs : System.EventArgs;

            [EventHandler("onswipe", typeof(SwipeEventArgs))]
            public static class LibEventHandlers;
            """,
            "EventRegistrationLib");

        var compilation = CompilationTestHost.CreateCompilation(
            [("Host.cs", """
                using BlazorCodeFirst;
                using Microsoft.AspNetCore.Components.Web;
                using static BlazorCodeFirst.Html;

                namespace T;

                public partial class Host : BodyComponentBase
                {
                    protected override View Body => Div.On("onswipe", (MouseEventArgs e) => { });
                }
                """)],
            reference);

        var result = CompilationTestHost.RunGenerator(compilation);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3028");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A compilation that does not reference <c>Components.Web</c> cannot see the framework's
    /// registrations, so an event only that table names has no mapping and the check is skipped in
    /// silence rather than reported.
    /// </summary>
    /// <remarks>
    /// Paired with <see cref="MismatchedHandler_WithComponentsWeb_IsReported"/>, which runs the same body
    /// against the same compilation plus that one reference. Without the pair "no diagnostic" would hold
    /// just as well if the body had never been analyzed. What the silence is about is the framework table
    /// alone: <see cref="MismatchedHandler_AgainstASourceRegistration_WithoutComponentsWeb_IsReported"/>
    /// runs the same compilation against a registration the compilation declares itself, and that one is
    /// read (#396).
    /// </remarks>
    [Fact]
    public void MismatchedHandler_WithoutComponentsWeb_IsSkippedInSilence()
    {
        var result = CompilationTestHost.RunGenerator(
            CompilationTestHost.CreateCompilationWithoutComponentsWeb(MismatchWithoutWebTypes));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3028");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void MismatchedHandler_WithComponentsWeb_IsReported()
    {
        var result = CompilationTestHost.RunGenerator(
            CompilationTestHost.CreateCompilation(MismatchWithoutWebTypes));

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3028");
    }

    /// <summary>
    /// A registration the compilation declares itself is read whether or not <c>Components.Web</c> is
    /// referenced: <c>[EventHandler]</c> is declared in <c>Microsoft.AspNetCore.Components</c>, which any
    /// compilation able to spell this surface can see (#396).
    /// </summary>
    /// <remarks>
    /// The registration is what the diagnostic disagrees with here, so this is the whole check running,
    /// not a second arm of it. Its counterpart above is the same compilation asking about an event only
    /// the framework registers.
    /// </remarks>
    [Fact]
    public void MismatchedHandler_AgainstASourceRegistration_WithoutComponentsWeb_IsReported()
    {
        var result = CompilationTestHost.RunGenerator(
            CompilationTestHost.CreateCompilationWithoutComponentsWeb(
                ("Host.cs", """
                    using BlazorCodeFirst;
                    using Microsoft.AspNetCore.Components;
                    using static BlazorCodeFirst.Html;

                    namespace T;

                    public sealed class SwipeEventArgs : System.EventArgs;

                    [EventHandler("onswipe", typeof(SwipeEventArgs))]
                    public static class SwipeHandlers;

                    public partial class Host : BodyComponentBase
                    {
                        protected override View Body => Div.On("onswipe", (ChangeEventArgs e) => { });
                    }
                    """)));

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3028");
    }

    /// <summary>
    /// A mismatch spelled without naming any type from <c>Components.Web</c>, so the same source compiles
    /// with and without that reference. <c>onclick</c> delivers a <c>MouseEventArgs</c>, and
    /// <c>ChangeEventArgs</c> is declared in <c>Microsoft.AspNetCore.Components</c>.
    /// </summary>
    private static readonly (string Path, string Source)[] MismatchWithoutWebTypes =
        [("Host.cs", """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body => Button.On("onclick", (ChangeEventArgs e) => { })["Go"];
            }
            """)];
}
