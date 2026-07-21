using BlazorCompose;
using static BlazorCompose.UI;

namespace BlazorCompose.IntegrationTests.Components;

public partial class CounterComponent : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        VStack(
            Text($"Count: {_count}"),
            Button("Increment", () => _count++));
}
