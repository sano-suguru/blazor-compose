# Production Foundation and First Vertical Slice Design

**Status:** Approved design  
**Date:** 2026-07-21

## Objective

Build a production-quality repository foundation and immediately validate it with a narrow end-to-end BlazorCompose slice. The first slice is intentionally small in product surface, but its projects, dependency boundaries, compiler architecture, packaging shape, diagnostics, and validation paths must remain suitable for later expansion.

This is not a throwaway prototype. Foundation work is included when changing it later would be expensive or when it validates a core architectural claim. Infrastructure whose correct form depends on later functionality is deferred until that functionality is introduced.

## Scope

The first slice provides:

- `View`
- `ComposeComponentBase`
- `UI.Text`
- `UI.Button`
- `UI.VStack`
- `UI.If`
- `Body` discovery and `RenderBody(RenderTreeBuilder)` generation
- Static sequence allocation for a linear tree and mutually exclusive `If` branches
- Standard Blazor event dispatch for an interactive counter
- BC1001 for non-`partial` component classes
- BC3001 for render-time state mutation, while permitting mutation inside deferred event handlers
- Generator, analyzer, runtime, integration, and trimming validation

The first slice does not provide `ForEach`, keyed identity, opaque expressions, Razor interoperability, component parameter binding, benchmarking, package publication automation, or .NET 11-only features.

## Repository and Project Structure

The repository starts with these logical projects:

| Project | Target | Responsibility |
| --- | --- | --- |
| `BlazorCompose.Runtime` | `net10.0` | Public runtime API, inert design-time factories, and `ComposeComponentBase` |
| `BlazorCompose.Compiler` | `netstandard2.0` | Incremental source generator, diagnostic analyzers, and shared semantic classification |
| `BlazorCompose.Runtime.Tests` | `net10.0` | Runtime API and base-component contract tests |
| `BlazorCompose.Compiler.Tests` | `net10.0` | Generated-source, sequence-allocation, and diagnostic tests |
| `BlazorCompose.IntegrationTests` | `net10.0` | Real Blazor rendering, events, rerendering, and render-diff behavior |
| `BlazorCompose.TrimTests` | `net10.0` | Trimmed application publication and output inspection |
| `BlazorCompose.Samples.Counter` | `net10.0` | Interactive counter and conditional-rendering demonstration |

The runtime baseline is .NET 10. The compiler extension targets `netstandard2.0` as an analyzer-host compatibility policy consistent with Roslyn templates and Microsoft tooling, not because all analyzer hosts technically require that TFM. Supported compiler behavior is defined against the Roslyn version shipped with the minimum supported .NET 10 SDK; older SDK compatibility is not promised.

Repository-wide configuration includes a pinned SDK, Central Package Management, shared build properties, nullable reference types, deterministic builds, explicit warning policy, current .NET/Roslyn analyzers, and formatting configuration.

## Dependency Boundaries

- Runtime has no Roslyn dependency.
- Compiler references Roslyn as private compile-time dependencies.
- Samples and integration tests consume Runtime and Compiler as a user project would.
- Runtime tests reference Runtime.
- Compiler tests reference Compiler and the minimum API references required to compile test inputs.
- Trim tests consume the locally packed `BlazorCompose` package so the test also validates analyzer and runtime asset wiring.

Generator, analyzers, and semantic classification remain in one `BlazorCompose.Compiler` assembly for the first implementation. Roslyn supports mixed generator and analyzer types in one assembly. This avoids duplicated classification and avoids introducing a secondary analyzer dependency that must also be resolved by every compiler and IDE host. A split is allowed only if an independently validated distribution, loading, or versioning requirement appears.

## Compiler Architecture

`BlazorCompose.Compiler` implements `IIncrementalGenerator`. The pipeline:

1. Finds candidate component declarations through syntax filtering.
2. Resolves `ComposeComponentBase`, `Body`, and known factory symbols semantically.
3. Classifies syntax into rendering-time expressions and deferred expressions such as button event handlers.
4. Converts Roslyn objects into immutable, equality-comparable compiler models.
5. Allocates static sequence ranges by preorder over logical source structure.
6. Emits `RenderBody(RenderTreeBuilder)` into the same partial component class.

`Compilation`, `SemanticModel`, `ISymbol`, and syntax nodes may be used in early transforms but are not retained in the final generation model. This preserves effective incremental caching and isolates emission from mutable compiler state.

The generator and analyzers use the same internal classification implementation. Published source diagnostics are owned by analyzers and are not reported a second time by the generator. The generator may report generation-fatal diagnostics that have no analyzer equivalent.

## Runtime Model

`ComposeComponentBase.BuildRenderTree` delegates only to generated `RenderBody`. On the SSC path, `Body` and factory methods are design-time syntax and are not executed at runtime.

The initial API retains the specification's abstract `Body` contract and inert factory implementations behind a mandatory architecture gate. The first slice does not pass that gate unless a rooted component retains generated rendering behavior while the unused `Body` implementation and inert factory call graph are removed. Failure requires revising the API shape and both authoritative papers before feature expansion; trim warnings or retained code must not be suppressed to force acceptance.

Button events use Blazor's standard `EventCallback` and renderer dispatch. The library does not introduce a separate event loop, scheduler, or synchronization context.

## Sequence Allocation and Conditional Rendering

Sequence numbers are compile-time literals assigned by preorder over logical source structure, never absolute text offsets or runtime counters. Whitespace or comments before a node do not define its sequence number.

For a linear SSC tree, preorder allocation assigns each emitted frame a stable position. For `If`, the compiler reserves disjoint static ranges for the `then` and `otherwise` branches and isolates the conditional boundary with a statically numbered region where required by the final emitted shape.

The counter validates event and rerender plumbing. A separate conditional scenario validates the core sequence claim:

- Toggling a leading conditional node does not renumber an unchanged following node.
- The produced render batch does not model the following node as content replacement plus removal.
- Generated-source tests pin the branch ranges and literal sequence values.

This proves conditional diff shape and sequence stability, not keyed item identity or arbitrary component-state preservation. Keyed reordering and instance-state preservation become acceptance criteria when `ForEach` is introduced.

## Diagnostics

### BC1001

BC1001 is an error when a component deriving from `ComposeComponentBase` is not declared `partial`. The analyzer owns the diagnostic so it is available during normal IDE analysis. The generator does not duplicate it.

### BC3001

BC3001 is an error for state mutation in expressions that execute while rendering `Body` or an expanded composable body.

The semantic classifier distinguishes:

- Immediately evaluated rendering expressions, where mutation is prohibited.
- Deferred event-handler lambdas passed to recognized event parameters, where mutation is permitted.

The initial enforceable contract covers direct assignments and increment/decrement operations whose target resolves to component instance state, including nested rendering expressions. It excludes deferred event handlers. Arbitrary method side effects aren't inferred in the first slice because general interprocedural mutation analysis isn't tractable as a baseline analyzer feature. The implementation must clarify this boundary in both authoritative papers rather than presenting BC3001 as proof against every possible hidden side effect. The diagnostic must not be implemented as a blanket syntactic search inside the `Body` syntax tree.

BC2001 and BC3002 are deferred until opaque expressions and keyed `ForEach` exist.

## Packaging Shape

The user-facing distribution is one `BlazorCompose` package:

- Runtime assembly under `lib/net10.0`.
- `BlazorCompose.Compiler.dll` under `analyzers/dotnet/cs`.
- Roslyn dependencies remain private and are not exposed as application dependencies.

Publication automation is deferred, but pack layout is validated from the beginning so later publication does not require project relocation or a package-topology redesign.

## Validation

The initial automated quality gate includes:

- Restore, build, and test with repository-owned warnings treated according to the explicit warning policy.
- Generated-source assertions for counter and conditional components.
- Incremental-generator behavior tests for unchanged and selectively changed inputs.
- BC1001 and BC3001 positive and negative cases.
- Blazor integration tests for initial render, click dispatch, state update, rerender, and conditional render-batch shape.
- Runtime public API tracking with PublicApiAnalyzers.
- Trim analysis on the runtime library.
- A real trimmed application publish with zero unexplained trim warnings.
- Inspection of the trimmed output to verify the specification's claim that unused `Body` and inert factory implementation code is removed.

`dotnet watch` Hot Reload is exercised with the first vertical slice because generator rerun behavior is an architectural risk. Visual Studio and Rider Hot Reload checks remain manual Phase 1 completion criteria, as specified by the whitepaper. They are not treated as portable CI jobs.

Wasm Native AOT execution, benchmark infrastructure, allocation comparison with Razor, full package publication, and broader CI matrices are added at the milestones that require them.

## Acceptance Criteria

The first vertical slice is complete when:

1. A consumer can author a partial counter component using `VStack`, `Text`, `Button`, and `If`.
2. The compiler emits a valid `RenderBody` with literal, syntax-derived sequence numbers.
3. The component renders and updates through standard Blazor event dispatch without evaluating `Body` at runtime.
4. Conditional structural changes preserve the sequence identity of unchanged following syntax and produce the expected minimal diff shape.
5. BC1001 and BC3001 behave correctly, including the event-lambda exclusion.
6. The incremental generator avoids recomputing unaffected component models in its tested scenarios.
7. The runtime passes trim analysis, the trimmed sample runs, and the output validates or falsifies the inert-API removal claim.
8. The generated package layout contains Runtime and Compiler assets in their decided locations.
9. `dotnet watch` applies a `Body` edit through regenerated output, or the result is recorded as a blocking architecture finding before further feature expansion.

## Deferred Milestones

The next compiler milestone introduces keyed `ForEach` and BC3002, with tests for insertion, deletion, reordering, and stateful child identity. Opaque expressions and BC2001 follow separately. Razor interoperability, `Component<T>()`, trimming-safe parameter binding, Wasm AOT, benchmarks, and .NET 11 conditional features each require their own approved design or implementation plan.
