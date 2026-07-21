using BlazorCompose.IntegrationTests.Components;
using Bunit;

namespace BlazorCompose.IntegrationTests;

public sealed class RenderingTests : BunitContext
{
    [Fact]
    public void CounterClickRerendersThroughBlazor()
    {
        var cut = Render<CounterComponent>();

        cut.MarkupMatches("<div><span>Count: 0</span><button>Increment</button></div>");

        cut.Find("button").Click();

        cut.MarkupMatches("<div><span>Count: 1</span><button>Increment</button></div>");
    }

    [Fact]
    public void ConditionalToggleRemovesPrefixAndPreservesFollowingMarkupOrder()
    {
        var cut = Render<ConditionalComponent>();

        cut.MarkupMatches("<div><span>Prefix</span><span>Always</span><button>Toggle</button></div>");

        cut.Find("button").Click();

        cut.MarkupMatches("<div><span>Always</span><button>Toggle</button></div>");
    }
}
