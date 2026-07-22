using System.Diagnostics.CodeAnalysis;
using BlazorCompose.IntegrationTests.Components;
using Bunit;

namespace BlazorCompose.IntegrationTests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "xUnit tests use Subject_Scenario_ExpectedBehavior names.")]
public sealed class RenderingTests : BunitContext
{
    [Fact]
    public void Counter_WhenIncrementButtonClicked_RerendersWithIncrementedCount()
    {
        var cut = Render<CounterComponent>();

        cut.MarkupMatches("<div><span>Count: 0</span><button>Increment</button></div>");

        cut.Find("button").Click();

        cut.MarkupMatches("<div><span>Count: 1</span><button>Increment</button></div>");
    }

    [Fact]
    public void ConditionalComponent_WhenToggleButtonClicked_RemovesPrefixAndPreservesMarkupOrder()
    {
        var cut = Render<ConditionalComponent>();

        cut.MarkupMatches("<div><span>Prefix</span><span>Always</span><button>Toggle</button></div>");

        cut.Find("button").Click();

        cut.MarkupMatches("<div><span>Always</span><button>Toggle</button></div>");
    }

    [Fact]
    public void ComposableCounterComponent_WhenRenderedAndIncremented_EvaluatesArgumentsOncePerRender()
    {
        var cut = Render<ComposableCounterComponent>();

        cut.MarkupMatches("<div><span>Count: 0</span><button>Increment</button></div>");
        Assert.Equal(1, cut.Instance.ArgumentEvaluations);

        cut.Find("button").Click();

        cut.MarkupMatches("<div><span>Count: 1</span><button>Increment</button></div>");
        Assert.Equal(2, cut.Instance.ArgumentEvaluations);
    }
}
