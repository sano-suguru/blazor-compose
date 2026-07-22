using BlazorCompose;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCompose.UI;

var component = new TrimCounter();
component.RenderForTrimTest(new RenderTreeBuilder());

public partial class TrimCounter : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        VStack(
            CountLabel($"Count: {_count}"),
            Button("Increment", () => _count++));

    [Composable]
    private static View CountLabel(string value) => Text(value);

    public void RenderForTrimTest(RenderTreeBuilder builder)
        => BuildRenderTree(builder);
}
