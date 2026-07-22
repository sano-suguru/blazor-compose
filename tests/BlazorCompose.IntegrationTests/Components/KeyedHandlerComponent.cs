using System.Collections.Generic;
using BlazorCompose;
using static BlazorCompose.UI;

namespace BlazorCompose.IntegrationTests.Components;

public partial class KeyedHandlerComponent : ComposeComponentBase
{
    private int _total;
    private readonly List<Step> _steps = [new(1, 1), new(2, 5), new(3, 10)];

    protected override View Body =>
        VStack(
            Text($"Total: {_total}"),
            ForEach(_steps, key: s => s.Id, content: s => Button($"+{s.Amount}", () => _total += s.Amount)));

    private sealed record Step(int Id, int Amount);
}
