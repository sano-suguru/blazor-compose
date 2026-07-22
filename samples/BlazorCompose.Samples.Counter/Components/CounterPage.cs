using Microsoft.AspNetCore.Components;
using static BlazorCompose.UI;

namespace BlazorCompose.Samples.Counter.Components;

[Route("/counter")]
public partial class CounterPage : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        VStack(
            Text($"Count: {_count}"),
            If(_count >= 3, () => Text("Milestone reached")),
            Button("Increment", () => _count++));
}
