# BlazorCompose Agent Instructions

## Repository status and commands

This repository now has its initial .NET foundation in place. `WHITEPAPER.md` and `YELLOWPAPER.md` remain the authoritative product and technical specifications, and implementation changes must stay consistent with both papers where their decisions overlap.

- `global.json` pins SDK `10.0.300` with `latestPatch` roll-forward.
- `BlazorCompose.slnx` contains six in-solution projects: `BlazorCompose.Runtime`, `BlazorCompose.Compiler`, `BlazorCompose.Runtime.Tests`, `BlazorCompose.Compiler.Tests`, `BlazorCompose.IntegrationTests`, and `BlazorCompose.Samples.Counter`.
- `tests/BlazorCompose.TrimTests` and `tests/BlazorCompose.TrimTestApp` exist in the repository but remain outside the solution until the local package-based trimming workflow is introduced.
- Repository-wide build configuration lives in `Directory.Build.props`, `Directory.Packages.props`, and `.editorconfig`.
- CI workflow (`.github/workflows/ci.yml`) runs restore/build/test/pack/verify/trim-publish on Ubuntu (linux-x64) and macOS (osx-arm64) with SDK 10.0.300.
- Do not invent additional build, test, lint, format, or single-test commands. Use the validated commands below unless committed tooling files add more.

## Validated commands

- Restore: `dotnet restore BlazorCompose.slnx`
- Build: `dotnet build BlazorCompose.slnx --no-restore`
- Test all: `dotnet test BlazorCompose.slnx --no-build`
- Test one project: `dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --no-build`
- Test one case: `dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --no-build --filter FullyQualifiedName~GeneratorTests`
- Pack: `dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release -o artifacts/package`
- Verify package layout: `bash eng/verify-package.sh artifacts/package/BlazorCompose.0.1.0-dev.nupkg`
- Test package contents: `dotnet test tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj --filter FullyQualifiedName~PackageContentsTests`
- Publish trimmed (osx-arm64): `dotnet publish tests/BlazorCompose.TrimTestApp/BlazorCompose.TrimTestApp.csproj -c Release -r osx-arm64 --self-contained true --configfile tests/BlazorCompose.TrimTestApp/NuGet.config`
- Publish trimmed (linux-x64): `dotnet publish tests/BlazorCompose.TrimTestApp/BlazorCompose.TrimTestApp.csproj -c Release -r linux-x64 --self-contained true --configfile tests/BlazorCompose.TrimTestApp/NuGet.config`
- Run trim tests: `BLAZORCOMPOSE_TRIM_OUTPUT=$(pwd)/tests/BlazorCompose.TrimTestApp/bin/Release/net10.0/osx-arm64/publish dotnet test tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj`
- Hot Reload (sample): `dotnet watch --project samples/BlazorCompose.Samples.Counter/BlazorCompose.Samples.Counter.csproj`

## Architecture

BlazorCompose is a pure-C# declarative UI layer for Blazor. User-authored `Body` expressions and reachable `[Composable]` methods are design-time syntax, not a runtime UI tree. A Roslyn Source Generator analyzes them and emits `RenderBody(RenderTreeBuilder)` into the same `partial` component class; `ComposeComponentBase.BuildRenderTree` delegates to that generated method.

The compilation pipeline has three paths:

- **SSC (Statically Sequenceable Constructs):** direct factories, decorators, `If`, keyed `ForEach`, nested SSC expressions, and statically expanded `[Composable]` calls. Emit direct `RenderTreeBuilder` calls with compile-time sequence constants and no runtime intermediate UI representation.
- **Transplantable:** native control flow such as `if`, `foreach`, and `switch`. Transplant the syntax into generated code and isolate it with a statically numbered Blazor region.
- **Opaque:** calls that cannot be analyzed, such as non-`[Composable]` methods returning `View`. Evaluate only this path at runtime as a `RenderFragment`-backed `View`, isolate it in a region, and report BC2001.

The runtime must remain standard Blazor: generated components are normal `ComponentBase` descendants, rendering uses `RenderTreeBuilder`, dispatch uses Blazor's `SynchronizationContext`/`InvokeAsync`, and Razor interoperability uses `RenderFragment` plus `Component<T>()`.

Target net10.0 as the required baseline. Keep net11.0 features opt-in behind `NET11_0_OR_GREATER`; the C# 15 union/`closed` `ViewNode` model remains conditional until the platform feature is finalized.

## Key conventions

- Sequence numbers represent source syntax positions, never runtime generation order. Allocate them statically with preorder traversal and disjoint ranges for mutually exclusive branches.
- `ForEach` requires a key that represents item identity. Sequence numbers identify template positions; keys identify data instances.
- Components deriving from `ComposeComponentBase` must be `partial` so the generator can emit `RenderBody` (BC1001 otherwise).
- `Body`, factory APIs, and decorators are inert design-time constructs on the SSC path. `Body` must not be evaluated at runtime or mutate state; report state mutation as BC3001.
- Preserve one-way flow: event dispatch precedes state mutation, which precedes rendering and DOM diff application. Coalesce external state notifications without replacing Blazor's dispatcher.
- Keep SSC rendering free of runtime UI trees, reflection, and runtime expression compilation. `Component<T>().Param(...)` must compile to static parameter setters and remain trimming/AOT safe.
- Decorator chains should collapse into the owning element's emitted attributes rather than introduce wrapper nodes or additional frame widths.
- Preserve bidirectional Razor compatibility: generate `...AsFragment` siblings for `[Composable]` methods and support existing Razor components through `Component<T>()`.
- Treat diagnostic IDs BC1001, BC2001, BC3001, and BC3002 as published specification contracts.
- Do not revive the rejected interceptor-based or runtime `ref struct` tree architectures unless both papers are explicitly revised with new evidence.
- Do not present whitepaper performance estimates as measured results. Replace them only with reproducible PoC benchmark data.

## Engineering standard

Treat this repository as a reference-quality modern .NET codebase rather than a minimal proof of concept.

- Use SDK-style projects, central package/version management, repository-wide build settings, and a pinned SDK once implementation begins.
- Enable nullable reference types, implicit usings where appropriate, deterministic builds, and current .NET/Roslyn analyzers. Keep warning policy explicit and make repository-owned code warning-clean.
- Prefer modern C# features when they improve clarity, safety, or allocation behavior without compromising the net10.0 baseline. Isolate net11.0-only code with compile-time guards and matching tests.
- Design public APIs deliberately: keep nullability annotations accurate, avoid unnecessary allocations and reflection, support trimming/AOT, and document compatibility-affecting behavior.
- Keep the runtime, source generator, analyzers, tests, benchmarks, and samples as separate focused projects when the solution is introduced. Share configuration instead of duplicating it across projects.
- Test behavior at the appropriate layer: runtime unit tests, Roslyn generator/analyzer tests including generated source and diagnostics, integration tests against Blazor rendering, and benchmarks only for performance claims.
- Add repository automation together with the implementation: formatting, build, test, analyzer, package, trimming/AOT, and benchmark checks should be reproducible locally and in CI.
- Update `AGENTS.md` with exact validated commands, including single-test invocation, when project and tooling files are added.

## Collaboration

- Write source code comments and repository documentation in English. Communicate with the user in Japanese.
- Do not add AI-agent attribution or signatures to commits, commit trailers, pull request titles, descriptions, or comments. This includes `Co-authored-by` trailers naming Copilot, Claude, or other coding agents.

## Validation focus once implementation exists

Derive exact commands from the committed solution and project configuration. Validation should cover generated code shape, static sequence stability and component-state preservation, analyzer diagnostics, Razor interoperability, trimming/AOT behavior, allocation parity with equivalent Razor, and Hot Reload behavior in Visual Studio, `dotnet watch`, and Rider.
