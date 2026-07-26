using System.Globalization;
using System.Linq;
using System.Reflection;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class DiagnosticInfoTests
{
    // A [Fact] that iterates all descriptors internally, rather than a [Theory] with a
    // DiagnosticDescriptor member-data parameter, to avoid xUnit1045 (non-serializable theory data)
    // under TreatWarningsAsErrors.
    [Fact]
    public void ToDiagnostic_ForEveryDescriptor_RoundTripsToSameDescriptor()
    {
        var descriptors = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToArray();

        Assert.NotEmpty(descriptors);
        foreach (var descriptor in descriptors)
        {
            var diagnostic = DiagnosticInfo.Create(descriptor, Location.None, []).ToDiagnostic();
            Assert.Equal(descriptor.Id, diagnostic.Id);
            Assert.Same(descriptor, diagnostic.Descriptor);
        }
    }

    [Fact]
    public void IsError_ForWarningDescriptor_IsFalse()
    {
        var info = DiagnosticInfo.Create(DiagnosticDescriptors.BC3002, Location.None, []);

        Assert.False(info.IsError);
    }

    [Theory]
    [InlineData("BC3005")]
    [InlineData("BC3006")]
    public void ById_ResolvesNewComponentDiagnostics_AsErrors(string id)
    {
        var descriptor = BlazorCompose.Compiler.Diagnostics.DiagnosticDescriptors.ById(id);

        Assert.Equal(id, descriptor.Id);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    // ToDiagnostic_ForEveryDescriptor_RoundTripsToSameDescriptor above passes empty message arguments
    // and never formats the message, so it cannot catch a placeholder/argument mismatch or an
    // unescaped literal brace in messageFormat — exactly the defect BC1004 shipped with in this
    // branch's plan (a literal "{ return expr; }" that needed escaping to "{{ return expr; }}") and
    // that only a hand-written BC1004 test happened to catch.
    //
    // This must call string.Format directly on the raw messageFormat text rather than going through
    // Diagnostic.Create(...).GetMessage(...): that path was verified (by probe) to swallow the exact
    // FormatException this test exists to catch and silently return the unformatted string instead,
    // which would make an exception-based assertion vacuous.
    [Fact]
    public void EveryDescriptor_MessageFormat_FormatsWithoutThrowing()
    {
        var descriptors = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToArray();

        // No shipped descriptor has more than three placeholders (BC1001); ten dummy arguments is
        // comfortably more than any current or near-future descriptor could need, and surplus
        // arguments are not an error for composite formatting.
        var dummyArguments = Enumerable.Range(0, 10).Select(i => (object)$"arg{i}").ToArray();

        Assert.NotEmpty(descriptors);
        foreach (var descriptor in descriptors)
        {
            var format = descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture);
            var exception = Record.Exception(
                () => string.Format(CultureInfo.InvariantCulture, format, dummyArguments));
            Assert.Null(exception);
        }
    }
}
