# Production Foundation and First Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a production-quality .NET 10 foundation and prove BlazorCompose end to end with an interactive counter, conditional rendering, diagnostics, package wiring, and trimming validation.

**Architecture:** A `net10.0` runtime contains only public Blazor-facing APIs. A single `netstandard2.0` compiler extension assembly contains an incremental generator, analyzers, and their shared semantic classifier, and is shipped beside the runtime in one NuGet package. Tests exercise generated source, diagnostics, real Blazor rendering, incremental behavior, package layout, and trimmed output.

**Tech Stack:** .NET SDK 10.0.300, C# 14, ASP.NET Core Blazor 10, Roslyn 5.0.0, xUnit 2.9.3, bUnit 2.7.2, Microsoft.NET.Test.Sdk 17.14.1, Microsoft.CodeAnalysis.PublicApiAnalyzers 5.0.0, GitHub Actions.

## Global Constraints

- Runtime, samples, and tests target `net10.0`; `BlazorCompose.Compiler` targets `netstandard2.0`.
- The minimum supported compiler is the Roslyn version shipped with the minimum supported .NET 10 SDK.
- Runtime must not reference Roslyn, reflection-based binding, or runtime expression compilation.
- Generator, analyzers, and shared classification remain in one compiler assembly.
- The generator implements `IIncrementalGenerator` and emits literal sequence numbers assigned by preorder over logical source structure, not absolute text offsets.
- `Body` and SSC factories are design-time constructs and must not execute during normal rendering.
- BC1001 and BC3001 are published contracts; analyzers own them and the generator must not duplicate them.
- BC3001 initially covers direct assignments and increment/decrement of component instance state, excluding recognized deferred event handlers.
- The user-facing package is `BlazorCompose`, with runtime assets in `lib/net10.0` and compiler assets in `analyzers/dotnet/cs`.
- Repository documentation and source comments are English.
- Do not add AI attribution to commits, pull requests, or repository files.

---

## Planned File Map

```text
global.json                                      Pinned SDK 10.0.300
BlazorCompose.slnx                               Solution membership
Directory.Build.props                            Shared compiler and warning settings
Directory.Packages.props                         Central package versions
.editorconfig                                    Formatting and analyzer severities
.github/workflows/ci.yml                         Restore, build, test, pack, trim

src/BlazorCompose.Runtime/
  BlazorCompose.Runtime.csproj                    Runtime and package owner
  View.cs                                         Marker value
  ComposeComponentBase.cs                         BuildRenderTree bridge
  UI.cs                                           Inert Text/Button/VStack/If factories
  PublicAPI.Shipped.txt                           Released API baseline
  PublicAPI.Unshipped.txt                         Initial public API declaration

src/BlazorCompose.Compiler/
  BlazorCompose.Compiler.csproj                   Analyzer-host assembly
  Polyfills/IsExternalInit.cs                     Record support on netstandard2.0
  BlazorComposeGenerator.cs                       Incremental pipeline registration
  Analysis/KnownSymbols.cs                        Symbol resolution
  Analysis/ComponentModelFactory.cs               Body discovery and classification
  Analysis/ExecutionContextKind.cs                Render-time/deferred context
  Model/ComponentModel.cs                         Equatable generator input
  Model/RenderNode.cs                             Text/Button/VStack/If IR
  Generation/SequenceAllocator.cs                 Static frame-range allocation
  Generation/RenderBodyEmitter.cs                 Generated C# output
  Diagnostics/DiagnosticDescriptors.cs            BC1001 and BC3001 definitions
  Diagnostics/PartialComponentAnalyzer.cs         BC1001
  Diagnostics/RenderMutationAnalyzer.cs           BC3001

tests/BlazorCompose.Runtime.Tests/
  BlazorCompose.Runtime.Tests.csproj
  ComposeComponentBaseTests.cs

tests/BlazorCompose.Compiler.Tests/
  BlazorCompose.Compiler.Tests.csproj
  CompilationTestHost.cs
  GeneratorTests.cs
  SequenceAllocatorTests.cs
  PartialComponentAnalyzerTests.cs
  RenderMutationAnalyzerTests.cs
  IncrementalGeneratorTests.cs

tests/BlazorCompose.IntegrationTests/
  BlazorCompose.IntegrationTests.csproj
  Components/CounterComponent.cs
  Components/ConditionalComponent.cs
  RenderingTests.cs

tests/BlazorCompose.TrimTests/
  BlazorCompose.TrimTests.csproj
  TrimmedOutputTests.cs

tests/BlazorCompose.TrimTestApp/
  BlazorCompose.TrimTestApp.csproj
  Program.cs
  NuGet.config

samples/BlazorCompose.Samples.Counter/
  BlazorCompose.Samples.Counter.csproj
  Program.cs
  Components/App.razor
  Components/CounterPage.cs

eng/verify-package.sh                           NuGet layout assertions
AGENTS.md                                       Validated repository commands
```

### Task 1: Establish the Repository Foundation

**Files:**
- Create: `global.json`
- Create: `BlazorCompose.slnx`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `.editorconfig`
- Create: all project files listed in the file map
- Modify: `AGENTS.md`

**Interfaces:**
- Produces: the project names, targets, package versions, and commands consumed by every later task.

- [ ] **Step 1: Initialize version control and generate the solution and projects**

Run:

```bash
git init
dotnet new globaljson --sdk-version 10.0.300 --roll-forward latestPatch
dotnet new sln --name BlazorCompose --format slnx
dotnet new classlib -n BlazorCompose.Runtime -o src/BlazorCompose.Runtime -f net10.0
dotnet new classlib -n BlazorCompose.Compiler -o src/BlazorCompose.Compiler -f netstandard2.0
dotnet new xunit -n BlazorCompose.Runtime.Tests -o tests/BlazorCompose.Runtime.Tests -f net10.0
dotnet new xunit -n BlazorCompose.Compiler.Tests -o tests/BlazorCompose.Compiler.Tests -f net10.0
dotnet new xunit -n BlazorCompose.IntegrationTests -o tests/BlazorCompose.IntegrationTests -f net10.0
dotnet new xunit -n BlazorCompose.TrimTests -o tests/BlazorCompose.TrimTests -f net10.0
dotnet new console -n BlazorCompose.TrimTestApp -o tests/BlazorCompose.TrimTestApp -f net10.0
dotnet new blazor -n BlazorCompose.Samples.Counter -o samples/BlazorCompose.Samples.Counter -f net10.0 --interactivity Server --empty
dotnet sln BlazorCompose.slnx add src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj
dotnet sln BlazorCompose.slnx add src/BlazorCompose.Compiler/BlazorCompose.Compiler.csproj
dotnet sln BlazorCompose.slnx add tests/BlazorCompose.Runtime.Tests/BlazorCompose.Runtime.Tests.csproj
dotnet sln BlazorCompose.slnx add tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj
dotnet sln BlazorCompose.slnx add tests/BlazorCompose.IntegrationTests/BlazorCompose.IntegrationTests.csproj
dotnet sln BlazorCompose.slnx add samples/BlazorCompose.Samples.Counter/BlazorCompose.Samples.Counter.csproj
```

Expected: all commands exit with code 0 and `BlazorCompose.slnx` lists six projects. The trim verifier and trim app stay outside the solution because they run only after a local package has been produced.

- [ ] **Step 2: Add central package and build configuration**

Create `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="AngleSharp" Version="1.5.2" />
    <PackageVersion Include="bunit" Version="2.7.2" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="5.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
</Project>
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Create `.editorconfig`:

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true

[*.cs]
indent_style = space
indent_size = 4
dotnet_sort_system_directives_first = true
csharp_style_namespace_declarations = file_scoped:warning
```

- [ ] **Step 3: Configure project references and package references**

Use these exact relationships:

```bash
dotnet add tests/BlazorCompose.Runtime.Tests/BlazorCompose.Runtime.Tests.csproj reference src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj
dotnet add tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj reference src/BlazorCompose.Compiler/BlazorCompose.Compiler.csproj
dotnet add tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj reference src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj
dotnet add tests/BlazorCompose.IntegrationTests/BlazorCompose.IntegrationTests.csproj reference src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj
dotnet add samples/BlazorCompose.Samples.Counter/BlazorCompose.Samples.Counter.csproj reference src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj
dotnet add src/BlazorCompose.Compiler/BlazorCompose.Compiler.csproj package Microsoft.CodeAnalysis.CSharp
dotnet add src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj package Microsoft.CodeAnalysis.PublicApiAnalyzers
dotnet add tests/BlazorCompose.IntegrationTests/BlazorCompose.IntegrationTests.csproj package bunit
dotnet add tests/BlazorCompose.IntegrationTests/BlazorCompose.IntegrationTests.csproj package AngleSharp
```

Add this framework reference to `src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj`:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

Set the Roslyn package reference in `src/BlazorCompose.Compiler/BlazorCompose.Compiler.csproj` to:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp"
                  PrivateAssets="all"
                  IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
```

Then add the compiler project to the sample and integration test project files as an analyzer:

```xml
<ProjectReference Include="../../src/BlazorCompose.Compiler/BlazorCompose.Compiler.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

- [ ] **Step 4: Normalize template project files and make the empty solution build**

Delete the generated `Class1.cs` and `UnitTest1.cs` files. In every xUnit project, remove inline `Version` attributes and remove the `coverlet.collector` reference. Retain these versionless references:

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" />
<PackageReference Include="xunit" />
<PackageReference Include="xunit.runner.visualstudio">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

Run:

```bash
dotnet restore BlazorCompose.slnx
dotnet build BlazorCompose.slnx --no-restore
```

Expected: build succeeds with zero warnings.

- [ ] **Step 5: Replace the repository-status section and record validated commands**

Replace the statement that no solution or commands exist with the implemented project layout and then add:

```markdown
## Validated commands

- Restore: `dotnet restore BlazorCompose.slnx`
- Build: `dotnet build BlazorCompose.slnx --no-restore`
- Test all: `dotnet test BlazorCompose.slnx --no-build`
- Test one project: `dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --no-build`
- Test one case: `dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --no-build --filter FullyQualifiedName~GeneratorTests`
```

- [ ] **Step 6: Commit**

```bash
git add global.json BlazorCompose.slnx Directory.Build.props Directory.Packages.props .editorconfig src tests samples docs AGENTS.md
git commit -m "build: establish .NET repository foundation"
```

### Task 2: Implement the Runtime Contract

**Files:**
- Create: `src/BlazorCompose.Runtime/View.cs`
- Create: `src/BlazorCompose.Runtime/ComposeComponentBase.cs`
- Create: `src/BlazorCompose.Runtime/UI.cs`
- Create: `src/BlazorCompose.Runtime/PublicAPI.Shipped.txt`
- Create: `src/BlazorCompose.Runtime/PublicAPI.Unshipped.txt`
- Create: `tests/BlazorCompose.Runtime.Tests/ComposeComponentBaseTests.cs`

**Interfaces:**
- Produces: `View`, `ComposeComponentBase.Body`, `ComposeComponentBase.RenderBody(RenderTreeBuilder)`, and `UI.Text/Button/VStack/If`.

- [ ] **Step 1: Write the failing base-class delegation test**

```csharp
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCompose.Runtime.Tests;

public sealed class ComposeComponentBaseTests
{
    [Fact]
    public void BuildRenderTree_DelegatesToGeneratedRenderBody()
    {
        var component = new TestComponent();
        var builder = new RenderTreeBuilder();

        component.Render(builder);

        Assert.Equal(1, component.RenderBodyCalls);
    }

    private sealed class TestComponent : ComposeComponentBase
    {
        public int RenderBodyCalls { get; private set; }
        protected override View Body => default;
        protected override void RenderBody(RenderTreeBuilder builder) => RenderBodyCalls++;
        public void Render(RenderTreeBuilder builder) => BuildRenderTree(builder);
    }
}
```

- [ ] **Step 2: Run the test and verify failure**

```bash
dotnet test tests/BlazorCompose.Runtime.Tests/BlazorCompose.Runtime.Tests.csproj --filter FullyQualifiedName~ComposeComponentBaseTests
```

Expected: compilation fails because the runtime types do not exist.

- [ ] **Step 3: Implement the minimal runtime**

```csharp
// View.cs
namespace BlazorCompose;

public readonly struct View;
```

```csharp
// ComposeComponentBase.cs
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCompose;

public abstract class ComposeComponentBase : ComponentBase
{
    protected abstract View Body { get; }
    protected abstract void RenderBody(RenderTreeBuilder builder);

    protected sealed override void BuildRenderTree(RenderTreeBuilder builder)
        => RenderBody(builder);
}
```

```csharp
// UI.cs
namespace BlazorCompose;

public static class UI
{
    public static View Text(string content) => default;
    public static View Button(string label, Action onClick) => default;
    public static View VStack(params View[] children) => default;
    public static View If(bool condition, Func<View> then, Func<View>? otherwise = null) => default;
}
```

- [ ] **Step 4: Add public API baselines**

Keep `PublicAPI.Shipped.txt` empty and list every public runtime type/member in `PublicAPI.Unshipped.txt` using the diagnostics produced by PublicApiAnalyzers as the authoritative syntax.

- [ ] **Step 5: Run runtime tests**

```bash
dotnet test tests/BlazorCompose.Runtime.Tests/BlazorCompose.Runtime.Tests.csproj
```

Expected: all tests pass with zero warnings.

- [ ] **Step 6: Commit**

```bash
git add src/BlazorCompose.Runtime tests/BlazorCompose.Runtime.Tests
git commit -m "feat: add inert runtime API"
```

### Task 3: Build the Incremental Generator Test Harness and BC1001

**Files:**
- Create: `tests/BlazorCompose.Compiler.Tests/CompilationTestHost.cs`
- Create: `tests/BlazorCompose.Compiler.Tests/GeneratorTests.cs`
- Create: `tests/BlazorCompose.Compiler.Tests/PartialComponentAnalyzerTests.cs`
- Create: `src/BlazorCompose.Compiler/BlazorComposeGenerator.cs`
- Create: `src/BlazorCompose.Compiler/Diagnostics/DiagnosticDescriptors.cs`
- Create: `src/BlazorCompose.Compiler/Diagnostics/PartialComponentAnalyzer.cs`

**Interfaces:**
- Produces: `CompilationTestHost.RunGenerator(string)`, `CompilationTestHost.RunAnalyzerAsync<T>(string)`, BC1001, and an incremental generator entry point.

- [ ] **Step 1: Write failing tests for partial and non-partial components**

Use this input in `GeneratorTests`:

```csharp
using BlazorCompose;
using static BlazorCompose.UI;

public partial class Counter : ComposeComponentBase
{
    protected override View Body => Text("Count");
}
```

Assert that generated output contains:

```csharp
protected override void RenderBody(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)
```

Use the same class without `partial` in `PartialComponentAnalyzerTests` and assert one error with ID `BC1001` at the class identifier.

- [ ] **Step 2: Run the focused tests**

```bash
dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --filter "FullyQualifiedName~GeneratorTests|FullyQualifiedName~PartialComponentAnalyzerTests"
```

Expected: tests fail because the host, generator, and analyzer do not exist.

- [ ] **Step 3: Implement the test host**

`CompilationTestHost` must:

1. Parse with `LanguageVersion.CSharp14`.
2. Reference `System.Runtime`, `Microsoft.AspNetCore.Components`, and `BlazorCompose.Runtime`.
3. Create `CSharpCompilation` with `OutputKind.DynamicallyLinkedLibrary`.
4. Create `CSharpGeneratorDriver` with `new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true)`.
5. Run analyzers through `CompilationWithAnalyzers`.
6. Return the updated `GeneratorDriver`, generated source, tracked steps, and diagnostics without normalizing away locations so Task 7 can reuse the same driver instance.

- [ ] **Step 4: Implement the incremental generator skeleton**

```csharp
[Generator(LanguageNames.CSharp)]
public sealed class BlazorComposeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var components = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
            static (syntaxContext, cancellationToken) =>
                ComponentModelFactory.TryCreate(syntaxContext, cancellationToken))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(
            components,
            static (productionContext, model) =>
                productionContext.AddSource(model!.HintName, RenderBodyEmitter.Emit(model)));
    }
}
```

Initially emit an empty `RenderBody` so the partial component compiles.

- [ ] **Step 5: Implement BC1001 as an analyzer**

Register a `SymbolAction` for named types, identify direct or indirect inheritance from `ComposeComponentBase`, and report BC1001 when no declaring class syntax has a `partial` modifier. Do not report the diagnostic from the generator.

- [ ] **Step 6: Run focused tests**

Expected: generator and BC1001 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/BlazorCompose.Compiler tests/BlazorCompose.Compiler.Tests
git commit -m "feat: discover components and report BC1001"
```

### Task 4: Generate Text, Button, and VStack Rendering

**Files:**
- Create: `src/BlazorCompose.Compiler/Analysis/KnownSymbols.cs`
- Create: `src/BlazorCompose.Compiler/Analysis/ComponentModelFactory.cs`
- Create: `src/BlazorCompose.Compiler/Analysis/ExecutionContextKind.cs`
- Create: `src/BlazorCompose.Compiler/Model/ComponentModel.cs`
- Create: `src/BlazorCompose.Compiler/Model/RenderNode.cs`
- Create: `src/BlazorCompose.Compiler/Polyfills/IsExternalInit.cs`
- Create: `src/BlazorCompose.Compiler/Generation/SequenceAllocator.cs`
- Create: `src/BlazorCompose.Compiler/Generation/RenderBodyEmitter.cs`
- Create: `tests/BlazorCompose.Compiler.Tests/SequenceAllocatorTests.cs`
- Modify: `tests/BlazorCompose.Compiler.Tests/GeneratorTests.cs`

**Interfaces:**
- Produces: equatable `ComponentModel`, `RenderNode` variants, sequence allocation, and valid render calls for the initial linear SSC.

- [ ] **Step 1: Add generated-source tests**

For:

```csharp
protected override View Body =>
    VStack(
        Text($"Count: {_count}"),
        Button("Increment", () => _count++));
```

assert generated source contains literal calls in this order:

```csharp
__builder.OpenElement(0, "div");
__builder.OpenElement(1, "span");
__builder.AddContent(2, $"Count: {_count}");
__builder.CloseElement();
__builder.OpenElement(3, "button");
__builder.AddAttribute(4, "onclick", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _count++));
__builder.AddContent(5, "Increment");
__builder.CloseElement();
__builder.CloseElement();
```

Also assert that generated source contains no `Body` access and no runtime sequence counter.

- [ ] **Step 2: Add allocator unit tests**

Assert these widths:

```csharp
Assert.Equal(2, SequenceAllocator.Width(new TextNode(...)));
Assert.Equal(3, SequenceAllocator.Width(new ButtonNode(...)));
Assert.Equal(1 + children.Sum(SequenceAllocator.Width), SequenceAllocator.Width(new VStackNode(children)));
```

- [ ] **Step 3: Run tests and verify failure**

```bash
dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --filter "FullyQualifiedName~GeneratorTests|FullyQualifiedName~SequenceAllocatorTests"
```

- [ ] **Step 4: Implement immutable compiler models**

Use sealed records containing only strings, primitive values, source spans, and nested model values:

```csharp
internal abstract record RenderNode;
internal sealed record TextNode(string ContentExpression) : RenderNode;
internal sealed record ButtonNode(string LabelExpression, string HandlerExpression) : RenderNode;
internal sealed record VStackNode(ImmutableArray<RenderNode> Children) : RenderNode;
internal sealed record IfNode(string ConditionExpression, RenderNode Then, RenderNode? Otherwise) : RenderNode;
```

Do not store absolute `TextSpan` offsets, syntax nodes, symbols, semantic models, or compilations in these records. Source locations needed for generator failures belong in a separate diagnostic value and do not participate in render-model equality or sequence allocation.

- [ ] **Step 5: Add the netstandard2.0 record polyfill**

```csharp
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
```

- [ ] **Step 6: Implement semantic factory recognition**

Resolve methods by symbol equality against `BlazorCompose.UI`, not by method name alone. Reject unsupported shapes with an internal classification result instead of silently generating incorrect code. Preserve dynamic argument text exactly from syntax for interpolation and event lambdas.

- [ ] **Step 7: Implement allocation and emission**

Allocate preorder sequence literals. Emit fully qualified framework names and generated headers. Use stable hint names derived from namespace plus metadata type name.

Treat allocator width as the count of builder calls that consume a sequence argument. `CloseElement` and `CloseRegion` do not consume sequence numbers. Update both papers in Task 5 so their `FrameWidth` examples use this definition.

- [ ] **Step 8: Run compiler tests**

Expected: all compiler tests pass with zero warnings.

- [ ] **Step 9: Commit**

```bash
git add src/BlazorCompose.Compiler tests/BlazorCompose.Compiler.Tests
git commit -m "feat: generate linear SSC render code"
```

### Task 5: Add If Branch Ranges and BC3001

**Files:**
- Modify: `src/BlazorCompose.Compiler/Analysis/ComponentModelFactory.cs`
- Modify: `src/BlazorCompose.Compiler/Generation/SequenceAllocator.cs`
- Modify: `src/BlazorCompose.Compiler/Generation/RenderBodyEmitter.cs`
- Create: `src/BlazorCompose.Compiler/Diagnostics/RenderMutationAnalyzer.cs`
- Create: `tests/BlazorCompose.Compiler.Tests/RenderMutationAnalyzerTests.cs`
- Modify: `tests/BlazorCompose.Compiler.Tests/GeneratorTests.cs`
- Modify: `tests/BlazorCompose.Compiler.Tests/SequenceAllocatorTests.cs`

**Interfaces:**
- Produces: disjoint `If` branch ranges and BC3001 with deferred-event exclusion.

- [ ] **Step 1: Write failing branch-allocation tests**

For a leading `If` followed by `Text("Always")`, assert:

- The conditional region receives one literal sequence.
- `then` and `otherwise` receive disjoint ranges.
- `Text("Always")` receives the same sequence regardless of which branch executes.
- No generated sequence expression contains `++`.

- [ ] **Step 2: Write failing BC3001 tests**

Report BC3001:

```csharp
protected override View Body => Text($"{_count++}");
protected override View Body => Text($"{_count = 4}");
```

Do not report BC3001:

```csharp
protected override View Body => Button("Increment", () => _count++);
```

Do not claim to detect this first-slice limitation:

```csharp
protected override View Body => Text(MutateAndReturnText());
```

- [ ] **Step 3: Implement `If` allocation**

Reserve:

```text
region = k
then   = [k + 1, k + 1 + width(then))
else   = [thenEnd, thenEnd + width(else))
next   = elseEnd
```

Emit `OpenRegion(k)`, transplanted `if/else`, and `CloseRegion()`.

- [ ] **Step 4: Implement execution-context classification**

When descending into the second `UI.Button` argument, switch from `Rendering` to `DeferredEventHandler`. All other initial factory arguments remain `Rendering`.

- [ ] **Step 5: Implement BC3001**

Register operation actions for `ISimpleAssignmentOperation`, compound assignments, and `IIncrementOrDecrementOperation`. Report only when:

1. The target resolves to an instance field or property on the containing component.
2. The operation lies in a `Body` getter.
3. The operation is not inside a lambda classified as the recognized `Button` handler argument.

- [ ] **Step 6: Run focused and full compiler tests**

```bash
dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --filter "FullyQualifiedName~RenderMutationAnalyzerTests|FullyQualifiedName~SequenceAllocatorTests|FullyQualifiedName~GeneratorTests"
dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 7: Update both papers**

Clarify in `WHITEPAPER.md` and `YELLOWPAPER.md` that the initial BC3001 guarantee covers statically identifiable direct state writes and excludes recognized deferred event handlers; arbitrary interprocedural side effects aren't claimed as fully detectable.

Also clarify that sequence allocation uses preorder ordinals over logical syntax, not absolute source offsets, and correct Yellow Paper `FrameWidth` examples to count only builder calls that consume sequence arguments.

- [ ] **Step 8: Commit**

```bash
git add src/BlazorCompose.Compiler tests/BlazorCompose.Compiler.Tests WHITEPAPER.md YELLOWPAPER.md
git commit -m "feat: add conditional sequences and mutation diagnostics"
```

### Task 6: Prove Rendering with bUnit and the Counter Sample

**Files:**
- Create: `tests/BlazorCompose.IntegrationTests/Components/CounterComponent.cs`
- Create: `tests/BlazorCompose.IntegrationTests/Components/ConditionalComponent.cs`
- Create: `tests/BlazorCompose.IntegrationTests/RenderingTests.cs`
- Create: `samples/BlazorCompose.Samples.Counter/Components/CounterPage.cs`
- Modify: sample routing files

**Interfaces:**
- Consumes: Runtime API and compiler analyzer wiring.
- Produces: real render/event proof and a manually runnable sample.

- [ ] **Step 1: Write the counter component**

```csharp
using BlazorCompose;
using static BlazorCompose.UI;

namespace BlazorCompose.IntegrationTests.Components;

public partial class CounterComponent : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        VStack(
            Text($"Count: {_count}"),
            Button("Increment", () => _count++));
}
```

- [ ] **Step 2: Write failing bUnit tests**

```csharp
public sealed class RenderingTests : BunitContext
{
    [Fact]
    public void Counter_Click_RerendersThroughBlazor()
    {
        var cut = Render<CounterComponent>();
        cut.Find("button").Click();
        cut.MarkupMatches("<div><span>Count: 1</span><button>Increment</button></div>");
    }
}
```

Create `ConditionalComponent.cs`:

```csharp
using BlazorCompose;
using static BlazorCompose.UI;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ConditionalComponent : ComposeComponentBase
{
    private bool _showPrefix = true;

    protected override View Body =>
        VStack(
            If(_showPrefix, () => Text("Prefix")),
            Text("Always"),
            Button("Toggle", () => _showPrefix = !_showPrefix));
}
```

Add a test that clicks `Toggle` and asserts `Always` remains present and ordered before the button. Pair this integration assertion with the exact sequence and render-shape compiler tests from Task 5; do not label the bUnit assertion alone as proof of minimal diffing.

- [ ] **Step 3: Run integration tests and verify failure**

```bash
dotnet test tests/BlazorCompose.IntegrationTests/BlazorCompose.IntegrationTests.csproj
```

- [ ] **Step 4: Correct compiler output until the tests pass**

Fix emission so `VStack` maps to `div`, `Text` maps to `span`, `Button` maps to `button`, event attributes use `EventCallback.Factory.Create`, and all elements are closed. Do not introduce a runtime `View` tree or evaluate `Body`. Rerun the integration project and require all tests to pass.

- [ ] **Step 5: Add the runnable sample**

Implement `CounterPage`:

```csharp
using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.UI;

namespace BlazorCompose.Samples.Counter.Components;

[Route("/counter")]
public partial class CounterPage : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        VStack(
            Text($"Count: {_count}"),
            If(_count >= 3, () => Text("Milestone reached")),
            Button("Increment", () => _count++));
}
```

In `samples/BlazorCompose.Samples.Counter/Components/App.razor`, change the template's route host to:

```razor
<Routes @rendermode="InteractiveServer" />
```

Keep the template-generated `AddInteractiveServerComponents` and `AddInteractiveServerRenderMode` calls in `Program.cs`. Without the render mode on `Routes`, the page is static SSR and button events do not execute.

Run:

```bash
dotnet run --project samples/BlazorCompose.Samples.Counter/BlazorCompose.Samples.Counter.csproj
```

Expected: the app starts, `/counter` renders `Count: 0`, and clicking the button renders `Count: 1`.

- [ ] **Step 6: Commit**

```bash
git add tests/BlazorCompose.IntegrationTests samples/BlazorCompose.Samples.Counter
git commit -m "test: prove interactive Blazor rendering"
```

### Task 7: Validate Incremental Behavior

**Files:**
- Create: `tests/BlazorCompose.Compiler.Tests/IncrementalGeneratorTests.cs`
- Modify: `src/BlazorCompose.Compiler/BlazorComposeGenerator.cs`

**Interfaces:**
- Produces: tracked incremental steps proving unaffected component models are cached.

- [ ] **Step 1: Name incremental pipeline stages**

Apply `.WithTrackingName(...)` to candidate discovery, semantic modeling, sequence allocation, and emission.

- [ ] **Step 2: Write the failing caching test**

Create two components in separate syntax trees. Construct the driver with `new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true)`, retain the same driver instance returned from the first run, replace only one syntax tree, and run the returned driver again. Inspect named transform steps plus Roslyn's source-output tracking. Assert the unchanged component model is cached or unchanged while the changed component is recomputed.

- [ ] **Step 3: Run the test**

```bash
dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --filter FullyQualifiedName~IncrementalGeneratorTests
```

Expected: failure until the pipeline is granular and equality-comparable.

- [ ] **Step 4: Refine pipeline boundaries**

Remove broad `.Collect()` operations from per-component paths and ensure equality is based on immutable model values, not Roslyn object identity.

- [ ] **Step 5: Run all compiler tests**

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/BlazorCompose.Compiler tests/BlazorCompose.Compiler.Tests
git commit -m "perf: validate incremental generator caching"
```

### Task 8: Pack the Single Distribution

**Files:**
- Modify: `src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj`
- Create: `eng/verify-package.sh`
- Create: `tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj`

**Interfaces:**
- Produces: `artifacts/package/BlazorCompose.0.1.0-dev.nupkg` containing runtime and compiler assets.

- [ ] **Step 1: Configure the package owner**

Set in `BlazorCompose.Runtime.csproj`:

```xml
<PropertyGroup>
  <PackageId>BlazorCompose</PackageId>
  <Version>0.1.0-dev</Version>
  <IsPackable>true</IsPackable>
  <IsTrimmable>true</IsTrimmable>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="../BlazorCompose.Compiler/BlazorCompose.Compiler.csproj"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
  <None Include="../BlazorCompose.Compiler/bin/$(Configuration)/netstandard2.0/BlazorCompose.Compiler.dll"
        Pack="true"
        PackagePath="analyzers/dotnet/cs"
        Visible="false" />
</ItemGroup>

<Target Name="BuildCompilerForPack" BeforeTargets="Pack">
  <MSBuild Projects="../BlazorCompose.Compiler/BlazorCompose.Compiler.csproj"
           Targets="Build"
           Properties="Configuration=$(Configuration)" />
</Target>
```

Add the locally produced package version to `Directory.Packages.props`:

```xml
<PackageVersion Include="BlazorCompose" Version="0.1.0-dev" />
```

- [ ] **Step 2: Write package verification**

`eng/verify-package.sh` must unzip the package to a temporary directory and assert exactly:

```text
lib/net10.0/BlazorCompose.Runtime.dll
analyzers/dotnet/cs/BlazorCompose.Compiler.dll
```

It must fail if any `Microsoft.CodeAnalysis*.dll` is present.

- [ ] **Step 3: Pack and run verification**

```bash
dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release -o artifacts/package
bash eng/verify-package.sh artifacts/package/BlazorCompose.0.1.0-dev.nupkg
```

Expected: package verification succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj eng/verify-package.sh tests/BlazorCompose.TrimTests
git commit -m "build: package runtime and compiler together"
```

### Task 9: Enforce the Trimming Architecture Gate

**Files:**
- Create: `tests/BlazorCompose.TrimTestApp/Program.cs`
- Modify: `tests/BlazorCompose.TrimTestApp/BlazorCompose.TrimTestApp.csproj`
- Create: `tests/BlazorCompose.TrimTestApp/NuGet.config`
- Create: `tests/BlazorCompose.TrimTests/TrimmedOutputTests.cs`
- Modify: `WHITEPAPER.md`
- Modify: `YELLOWPAPER.md`

**Interfaces:**
- Produces: evidence that the package is trim-compatible and a decision on the abstract `Body`/inert factory architecture.

- [ ] **Step 1: Make TrimTests consume the local package**

Replace `tests/BlazorCompose.TrimTestApp/BlazorCompose.TrimTestApp.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>full</TrimMode>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <ILLinkTreatWarningsAsErrors>true</ILLinkTreatWarningsAsErrors>
    <TrimmerSingleWarn>false</TrimmerSingleWarn>
    <RestorePackagesPath>../../artifacts/nuget-packages</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="BlazorCompose" />
  </ItemGroup>
</Project>
```

Create `tests/BlazorCompose.TrimTestApp/NuGet.config` after the local package exists:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-blazorcompose" value="../../artifacts/package" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

Before each restore, delete only `artifacts/nuget-packages/blazorcompose/0.1.0-dev` so NuGet cannot reuse stale contents for the fixed development version.

- [ ] **Step 2: Add a rooted component**

Create `tests/BlazorCompose.TrimTestApp/Program.cs`:

```csharp
using BlazorCompose;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCompose.UI;

var component = new TrimCounter();
component.RenderForTrimTest(new RenderTreeBuilder());

public partial class TrimCounter : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        VStack(
            Text($"Count: {_count}"),
            Button("Increment", () => _count++));

    public void RenderForTrimTest(RenderTreeBuilder builder)
        => BuildRenderTree(builder);
}
```

The explicit render call roots `BuildRenderTree` and generated `RenderBody` without calling `Body`.

- [ ] **Step 3: Publish the trimmed app**

```bash
rm -rf artifacts/nuget-packages/blazorcompose/0.1.0-dev
dotnet publish tests/BlazorCompose.TrimTestApp/BlazorCompose.TrimTestApp.csproj -c Release -r osx-arm64 --self-contained true --configfile tests/BlazorCompose.TrimTestApp/NuGet.config
BLAZORCOMPOSE_TRIM_OUTPUT=tests/BlazorCompose.TrimTestApp/bin/Release/net10.0/osx-arm64/publish \
  dotnet test tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj
```

Expected: publish succeeds with no trim warnings.

The first run treats every ILLink warning as an error. If ASP.NET Core itself produces a warning unrelated to BlazorCompose, capture the exact warning code and call chain, document why it is framework-owned, and allow only that exact warning in the TrimTestApp. BlazorCompose-owned warnings, wildcard suppressions, and assembly-wide suppressions remain forbidden.

- [ ] **Step 4: Inspect output metadata**

Use `TrimmedOutputTests` with `System.Reflection.Metadata` to read the directory from `BLAZORCOMPOSE_TRIM_OUTPUT`, fail when it is absent, open `BlazorCompose.TrimTestApp.dll` and `BlazorCompose.Runtime.dll`, and assert:

- The generated `RenderBody` method remains.
- The component's `Body` getter is absent.
- Unreferenced inert factory method bodies are absent when no retained code calls them.

If the trimmer retains the getter or factory graph, stop feature expansion. Record the observed roots, revise the runtime API shape, and update both papers before proceeding. Do not suppress the result.

Run the verifier with the published directory made explicit:

```bash
BLAZORCOMPOSE_TRIM_OUTPUT=tests/BlazorCompose.TrimTestApp/bin/Release/net10.0/osx-arm64/publish \
  dotnet test tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj
```

- [ ] **Step 5: Update performance wording**

Only after the metadata assertion passes, retain the papers' removal claim and cite the PoC validation method. If it fails, replace the claim in both papers with the measured behavior and the approved revised architecture.

- [ ] **Step 6: Commit**

```bash
git add tests/BlazorCompose.TrimTests tests/BlazorCompose.TrimTestApp WHITEPAPER.md YELLOWPAPER.md
git commit -m "test: enforce trimming architecture gate"
```

### Task 10: Add CI and Hot Reload Acceptance

**Files:**
- Create: `.github/workflows/ci.yml`
- Modify: `AGENTS.md`
- Modify: `WHITEPAPER.md`
- Modify: `YELLOWPAPER.md`

**Interfaces:**
- Produces: reproducible quality gates and recorded `dotnet watch` behavior.

- [ ] **Step 1: Add CI**

The workflow runs on macOS and Ubuntu with SDK 10.0.300:

```yaml
steps:
  - uses: actions/checkout@v4
  - uses: actions/setup-dotnet@v4
    with:
      dotnet-version: 10.0.300
  - run: dotnet restore BlazorCompose.slnx
  - run: dotnet build BlazorCompose.slnx --no-restore
  - run: dotnet test BlazorCompose.slnx --no-build
  - run: dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release -o artifacts/package
  - run: bash eng/verify-package.sh artifacts/package/BlazorCompose.0.1.0-dev.nupkg
  - run: rm -rf artifacts/nuget-packages/blazorcompose/0.1.0-dev
  - run: dotnet publish tests/BlazorCompose.TrimTestApp/BlazorCompose.TrimTestApp.csproj -c Release -r ${{ matrix.rid }} --self-contained true --configfile tests/BlazorCompose.TrimTestApp/NuGet.config
  - run: dotnet test tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj
    env:
      BLAZORCOMPOSE_TRIM_OUTPUT: tests/BlazorCompose.TrimTestApp/bin/Release/net10.0/${{ matrix.rid }}/publish
```

Use `linux-x64` and `osx-arm64` matrix RIDs.

- [ ] **Step 2: Exercise `dotnet watch`**

Run the sample with:

```bash
dotnet watch --project samples/BlazorCompose.Samples.Counter/BlazorCompose.Samples.Counter.csproj
```

Edit only the `Body` text literal. Confirm the generator reruns, the generated `RenderBody` update is applied, and the browser displays the new text without restarting the process.

- [ ] **Step 3: Record the result**

Document the SDK, OS, command, edit performed, and observed result in both papers' Phase 1 Hot Reload evidence section. If it fails, mark feature expansion blocked and investigate the existing DEBUG interpretation-mode contingency; do not silently remove the acceptance criterion.

- [ ] **Step 4: Final validation**

```bash
dotnet restore BlazorCompose.slnx
dotnet build BlazorCompose.slnx --no-restore
dotnet test BlazorCompose.slnx --no-build
dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release -o artifacts/package
bash eng/verify-package.sh artifacts/package/BlazorCompose.0.1.0-dev.nupkg
rm -rf artifacts/nuget-packages/blazorcompose/0.1.0-dev
dotnet publish tests/BlazorCompose.TrimTestApp/BlazorCompose.TrimTestApp.csproj -c Release -r osx-arm64 --self-contained true --configfile tests/BlazorCompose.TrimTestApp/NuGet.config
BLAZORCOMPOSE_TRIM_OUTPUT=tests/BlazorCompose.TrimTestApp/bin/Release/net10.0/osx-arm64/publish \
  dotnet test tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj
```

Expected: every command exits with code 0 and no unexplained warnings.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/ci.yml AGENTS.md WHITEPAPER.md YELLOWPAPER.md
git commit -m "ci: enforce vertical slice quality gates"
```
