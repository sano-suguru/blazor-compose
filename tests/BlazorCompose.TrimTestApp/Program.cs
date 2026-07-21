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
            Text($"Count: {_count}"),
            Button("Increment", () => _count++));

    public void RenderForTrimTest(RenderTreeBuilder builder)
        => BuildRenderTree(builder);
}
