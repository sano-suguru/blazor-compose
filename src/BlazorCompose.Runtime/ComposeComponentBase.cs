using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCompose;

public abstract class ComposeComponentBase : ComponentBase
{
    protected abstract View Body { get; }

    protected abstract void RenderBody(RenderTreeBuilder builder);

    protected sealed override void BuildRenderTree(RenderTreeBuilder builder) => RenderBody(builder);
}
