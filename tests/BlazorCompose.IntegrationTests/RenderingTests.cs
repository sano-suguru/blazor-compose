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

    [Fact]
    public void KeyedList_ClickingARow_InvokesThatRowsHandler()
    {
        var cut = Render<KeyedHandlerComponent>();

        cut.MarkupMatches(
            "<div><span>Total: 0</span><button>+1</button><button>+5</button><button>+10</button></div>");

        // Clicking the second row's button (+5) must mutate state using THAT row's captured item,
        // not the last item from the loop — a last-item-capture bug would produce 10 here instead.
        cut.FindAll("button")[1].Click();
        cut.Find("span").MarkupMatches("<span>Total: 5</span>");

        cut.FindAll("button")[2].Click();
        cut.Find("span").MarkupMatches("<span>Total: 15</span>");
    }
}
