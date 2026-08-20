namespace BlazorCodeFirst.Runtime.Tests;

public sealed class ViewTests
{
    [Fact]
    public void StringConversion_IsInert_YieldsDefaultView()
    {
        // The string->View conversion is design-time sugar for mixed content read by the generator;
        // at runtime it must be a no-op producing the default marker View.
        View view = "hello";

        Assert.Equal(default, view);
    }
}
