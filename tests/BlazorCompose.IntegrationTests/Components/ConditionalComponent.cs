using BlazorCompose;
using static BlazorCompose.UI;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ConditionalComponent : ComposeComponentBase
{
    private bool _showPrefix = true;

    protected override View Body =>
        VStack(
            If(_showPrefix, () => Text("Prefix")),
            Text("Always"),
            Button("Toggle", () => _showPrefix = !_showPrefix));
}
