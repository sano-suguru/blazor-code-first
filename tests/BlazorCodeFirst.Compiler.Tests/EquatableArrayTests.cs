using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class EquatableArrayTests
{
    [Fact]
    public void Default_EqualsEmpty_AndSharesHashCode()
    {
        EquatableArray<string> fromDefault = default;
        EquatableArray<string> fromEmpty = ImmutableArray<string>.Empty;

        Assert.True(fromDefault.Equals(fromEmpty));
        Assert.True(fromDefault == fromEmpty);
        Assert.Equal(fromEmpty.GetHashCode(), fromDefault.GetHashCode());
    }

    [Fact]
    public void Default_LengthIndexerAndEnumeration_DoNotThrow()
    {
        EquatableArray<string> value = default;

        Assert.Equal(0, value.Length);
        Assert.True(value.IsDefaultOrEmpty);

        var enumerated = 0;
        foreach (var _ in value)
            enumerated++;
        Assert.Equal(0, enumerated);

        Assert.Throws<System.IndexOutOfRangeException>(() => { _ = value[0]; });
    }

    [Fact]
    public void EqualValues_FromDistinctArrays_AreEqualAndShareHashCode()
    {
        EquatableArray<string> left = ImmutableArray.Create("a", "b", "c");
        EquatableArray<string> right = ImmutableArray.Create("a", "b", "c");

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void DifferentValues_AreNotEqual()
    {
        EquatableArray<string> left = ImmutableArray.Create("a", "b");
        EquatableArray<string> right = ImmutableArray.Create("a", "c");

        Assert.False(left.Equals(right));
        Assert.True(left != right);
    }

    [Fact]
    public void DifferentLengths_AreNotEqual()
    {
        EquatableArray<string> left = ImmutableArray.Create("a");
        EquatableArray<string> right = ImmutableArray.Create("a", "b");

        Assert.False(left == right);
    }

    [Fact]
    public void NestedEquality_ThroughRecord_IsStructural()
    {
        var childA = new TextContentNode(ExpressionTemplate.Literal("x"));
        var childB = new TextContentNode(ExpressionTemplate.Literal("x"));

        var left = new ElementNode("div", default, default, default, ImmutableArray.Create<RenderNode>(childA));
        var right = new ElementNode("div", default, default, default, ImmutableArray.Create<RenderNode>(childB));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Enumeration_YieldsElementsInOrder()
    {
        EquatableArray<string> value = ImmutableArray.Create("a", "b", "c");

        string[] expected = ["a", "b", "c"];
        Assert.Equal(expected, value.ToArray());
    }

    [Fact]
    public void RecordHoldingDefaultVsEmptyArray_IsEqualAndSharesHashCode()
    {
        // The exact incremental-cache-split vector: a record whose array field is default must be
        // both equal to AND hash-equal with the same record built from an empty array, otherwise
        // Roslyn would treat two value-equal models as different (equal compare, split hash) and
        // silently corrupt caching.
        var fromDefault = new ElementNode("div", default, default, default, default);
        var fromEmpty = new ElementNode("div", default, default, default, ImmutableArray<RenderNode>.Empty);

        Assert.Equal(fromDefault, fromEmpty);
        Assert.Equal(fromDefault.GetHashCode(), fromEmpty.GetHashCode());
    }

    [Fact]
    public void NestedEquality_TwoLevels_IsStructural()
    {
        // Two levels of EquatableArray nesting (div -> div -> text), each built from distinct
        // backing arrays, must compare and hash equal so value equality propagates all the way down.
        static ElementNode Build() =>
            new("div", default, default, default, ImmutableArray.Create<RenderNode>(
                new ElementNode("div", default, default, default, ImmutableArray.Create<RenderNode>(
                    new TextContentNode(ExpressionTemplate.Literal("x"))))));

        var left = Build();
        var right = Build();

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void ComponentSlot_WithStructurallyEqualContent_IsValueEqual()
    {
        // Slots ride the incremental pipeline inside EquatableArray, so they must compare structurally.
        // A reference-equality slip here silently recomputes every component with child content.
        var left = new ComponentTemplateNode(
            "global::X.C",
            EquatableArray<ComponentParameter>.Empty,
            new[]
            {
                new ComponentSlot("ChildContent", new TextContentTemplateNode(ExpressionTemplate.Literal("\"x\""))),
            }.ToImmutableArray());

        var right = new ComponentTemplateNode(
            "global::X.C",
            EquatableArray<ComponentParameter>.Empty,
            new[]
            {
                new ComponentSlot("ChildContent", new TextContentTemplateNode(ExpressionTemplate.Literal("\"x\""))),
            }.ToImmutableArray());

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void ComponentSlot_WithDifferentContent_IsNotEqual()
    {
        var left = new ComponentTemplateNode(
            "global::X.C",
            EquatableArray<ComponentParameter>.Empty,
            new[] { new ComponentSlot("ChildContent", new TextContentTemplateNode(ExpressionTemplate.Literal("\"x\""))) }
                .ToImmutableArray());

        var right = new ComponentTemplateNode(
            "global::X.C",
            EquatableArray<ComponentParameter>.Empty,
            new[] { new ComponentSlot("ChildContent", new TextContentTemplateNode(ExpressionTemplate.Literal("\"y\""))) }
                .ToImmutableArray());

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void GenericSlot_PrimitiveFieldsParticipateInValueEquality()
    {
        static ComponentSlot Build(string contextTypeName) =>
            new ComponentSlot(
                "RowTemplate",
                new TextContentTemplateNode(ExpressionTemplate.Literal("\"x\"")))
            {
                Kind = ComponentSlotKind.GenericContextIgnored,
                ContextTypeName = contextTypeName,
            };

        var left = Build("global::System.Int32");
        var equal = Build("global::System.Int32");
        var different = Build("global::System.String");

        static ComponentSlotNode BuildExpanded(string contextTypeName) =>
            new ComponentSlotNode(
                "RowTemplate",
                new TextContentNode(ExpressionTemplate.Literal("\"x\"")))
            {
                Kind = ComponentSlotKind.GenericContextIgnored,
                ContextTypeName = contextTypeName,
            };

        var expandedLeft = BuildExpanded("global::System.Int32");
        var expandedEqual = BuildExpanded("global::System.Int32");
        var expandedDifferent = BuildExpanded("global::System.String");

        Assert.Equal(left, equal);
        Assert.Equal(left.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(left, different);
        Assert.Equal(expandedLeft, expandedEqual);
        Assert.Equal(expandedLeft.GetHashCode(), expandedEqual.GetHashCode());
        Assert.NotEqual(expandedLeft, expandedDifferent);
    }
}
