using System.Reflection;
using BlazorCodeFirst;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class ComposableAttributeTests
{
    [Fact]
    public void AttributeUsage_ComposableAttribute_TargetsOnlyNonInheritedMethods()
    {
        var usage = typeof(ComposableAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }
}
