using BlazorCompose;
using static BlazorCompose.UI;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ComposableCounterComponent : ComposeComponentBase
{
    private int _count;
    private int _argumentEvaluations;

    public int ArgumentEvaluations => _argumentEvaluations;

    protected override View Body =>
        VStack(
            CounterLabel(GetCountLabel()),
            Button("Increment", () => _count++));

    [Composable]
    private static View CounterLabel(string value) => Text(value);

    private string GetCountLabel()
    {
        _argumentEvaluations++;
        return $"Count: {_count}";
    }
}
