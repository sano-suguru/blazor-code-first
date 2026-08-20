using System.Reflection;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class ViewPartAttributeTests
{
    [Fact]
    public void AttributeUsage_ViewPartAttribute_TargetsOnlyNonInheritedMethods()
    {
        var usage = typeof(ViewPartAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }
}
