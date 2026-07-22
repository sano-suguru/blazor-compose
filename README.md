# BlazorCompose

A code-first, type-safe declarative UI layer for Blazor — write your UI in pure C# (no HTML markup), the way you would in SwiftUI or Jetpack Compose.

A Roslyn Source Generator analyzes your `Body` expressions and reachable `[Composable]` methods at build time and emits a standard Blazor `RenderTreeBuilder` render method with statically assigned sequence numbers. The generated component is an ordinary `ComponentBase` descendant, so it inherits Razor's proven diffing performance and stays trimming/AOT-safe, with no runtime UI tree, reflection, or expression compilation.

```csharp
public partial class CounterPage : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        VStack(spacing: 16,
            Text($"Count: {_count}").FontSize(24).Bold(),
            Button("Increment", () => _count++).Style(ButtonStyle.Primary)
        )
        .Padding(24);
}
```

## Documentation

- **[DESIGN.md](DESIGN.md)** — design overview: background, goals, API design, and platform strategy. Start here.
- **[ARCHITECTURE.md](ARCHITECTURE.md)** — internal architecture: the compilation algorithm, static sequence assignment, memory layout, and analyzer diagnostics.

Both documents describe the intended finished design. They do **not** track progress.

## Current status

To see what actually works today, look at the code, the tests, and the Issues — not the design documents. `AGENTS.md` lists the validated build and test commands.
