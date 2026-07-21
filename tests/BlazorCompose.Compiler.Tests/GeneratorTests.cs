namespace BlazorCompose.Compiler.Tests;

public sealed class GeneratorTests
{
    // The canonical counter component used to verify generator output.
    private const string PartialCounterSource = """
        using BlazorCompose;
        using static BlazorCompose.UI;

        public partial class Counter : ComposeComponentBase
        {
            protected override View Body => Text("Count");
        }
        """;

    [Fact]
    public void PartialComponentGeneratesRenderBody()
    {
        var result = CompilationTestHost.RunGenerator(PartialCounterSource);

        var source = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains(
            "protected override void RenderBody(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)",
            source);
    }
}
