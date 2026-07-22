using System.Collections.Generic;
using static BlazorCompose.UI;

namespace BlazorCompose.Runtime.Tests;

public sealed class UIForEachTests
{
    [Fact]
    public void ForEach_WhenInvokedAtRuntime_ReturnsDefaultInertView()
    {
        IEnumerable<int> source = [1, 2, 3];

        var view = ForEach(source, key: static x => x, content: static x => Text(x.ToString()));

        Assert.Equal(default, view);
    }
}
