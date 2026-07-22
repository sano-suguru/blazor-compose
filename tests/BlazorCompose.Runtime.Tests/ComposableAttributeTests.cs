using System.Reflection;
using BlazorCompose;

namespace BlazorCompose.Runtime.Tests;

public sealed class ComposableAttributeTests
{
    [Fact]
    public void AttributeTargetsMethodsOnlyAndIsNotInherited()
    {
        var usage = typeof(ComposableAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }
}
