using BlazorCompose.IntegrationTests.Components;
using Bunit;

namespace BlazorCompose.IntegrationTests;

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

    [Fact]
    public void KeyedList_WhenRotateClicked_RerendersRowsInNewOrder()
    {
        var cut = Render<KeyedListComponent>();

        cut.MarkupMatches(
            "<div><span>one</span><span>two</span><span>three</span><button>Rotate</button></div>");

        cut.Find("button").Click();

        cut.MarkupMatches(
            "<div><span>two</span><span>three</span><span>one</span><button>Rotate</button></div>");
    }
}
