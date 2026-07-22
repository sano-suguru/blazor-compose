# Test Conventions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename every xUnit test to Microsoft's three-part convention and document a behavior-oriented testing policy.

**Architecture:** This is a behavior-preserving test-maintenance change. Test bodies and production code remain unchanged; only test identifiers and contributor documentation change. Existing real Roslyn, Blazor, packaging, and metadata collaborators remain in place.

**Tech Stack:** .NET 10, C# 14, xUnit, bUnit, Roslyn, Markdown

## Global Constraints

- Test names use `SubjectOrMethod_Scenario_ExpectedBehavior`.
- Existing test behavior and coverage must not be removed.
- Real implementations are preferred when fast, deterministic, and locally controlled.
- Test doubles are reserved for slow, nondeterministic, or out-of-process boundaries.
- Generated source, sequence numbers, incremental cache behavior, and diagnostic spans remain valid structural contracts.
- Do not introduce Moq, NSubstitute, or another mocking dependency.

---

### Task 1: Document Repository Test Conventions

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: The approved policy in `docs/superpowers/specs/2026-07-22-test-conventions-design.md`.
- Produces: A contributor-visible `Testing conventions` section.

- [ ] **Step 1: Add the documented convention**

Add a concise section stating:

```markdown
## Testing conventions

Test methods follow `SubjectOrMethod_Scenario_ExpectedBehavior`. Tests should
prefer observable behavior and real, deterministic collaborators over
interaction-based mocks. Test doubles are reserved for boundaries such as
remote services, wall-clock time, and randomness.

Compiler tests may inspect generated source, sequence numbers, incremental
cache behavior, and diagnostic spans because these are architectural
contracts. Test files may correspond to production types, but a one-to-one
file mapping is not required; group tests by cohesive capability.
```

- [ ] **Step 2: Review the rendered Markdown**

Run:

```bash
sed -n '/## Testing conventions/,+12p' README.md
```

Expected: The section is readable and contains the naming, behavior, real-collaborator, compiler-contract, and organization rules.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: document test conventions"
```

### Task 2: Rename Runtime, Integration, and Artifact Tests

**Files:**
- Modify: `tests/BlazorCompose.Runtime.Tests/ComposableAttributeTests.cs`
- Modify: `tests/BlazorCompose.Runtime.Tests/ComposeComponentBaseTests.cs`
- Modify: `tests/BlazorCompose.IntegrationTests/RenderingTests.cs`
- Modify: `tests/BlazorCompose.TrimTests/PackageContentsTests.cs`
- Modify: `tests/BlazorCompose.TrimTests/TrimmedOutputTests.cs`

**Interfaces:**
- Consumes: Existing test bodies and xUnit discovery attributes.
- Produces: Three-part test identifiers without behavioral changes.

- [ ] **Step 1: Rename each `[Fact]` method**

Use names whose three segments identify the subject, scenario, and result. Representative required names:

```csharp
AttributeUsage_ComposableAttribute_TargetsOnlyNonInheritedMethods()
BuildRenderTree_WhenRendered_DelegatesToGeneratedRenderBody()
Counter_WhenIncrementButtonClicked_RerendersWithIncrementedCount()
Package_WhenPacked_ContainsOnlyExpectedPayloadAndMetadata()
TrimmedApp_AfterPublish_RetainsRenderBodyMethod()
```

Apply the same pattern to every `[Fact]` in the five files. Preserve `nameof(...)` relationships by renaming the referenced method identifier together with its declaration.

- [ ] **Step 2: Verify naming mechanically**

Run:

```bash
perl -ne 'if (/\[(?:Fact|Theory)\]/) {$pending=1; next} if ($pending && /public\s+(?:async\s+)?(?:void|Task|ValueTask)\s+([A-Za-z0-9_]+)\s*\(/) { print "$ARGV:$1\n" unless $1 =~ /^[^_]+_[^_]+_[^_]+$/; $pending=0 }' tests/BlazorCompose.Runtime.Tests/*.cs tests/BlazorCompose.IntegrationTests/*.cs tests/BlazorCompose.TrimTests/*.cs
```

Expected: no output.

- [ ] **Step 3: Run affected tests**

Run:

```bash
dotnet test BlazorCompose.slnx --no-build
```

Expected: all in-solution tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/BlazorCompose.Runtime.Tests tests/BlazorCompose.IntegrationTests tests/BlazorCompose.TrimTests
git commit -m "test: standardize runtime and integration test names"
```

### Task 3: Rename Focused Compiler Tests

**Files:**
- Modify: `tests/BlazorCompose.Compiler.Tests/ComposableDefinitionTests.cs`
- Modify: `tests/BlazorCompose.Compiler.Tests/ExpressionTemplateTests.cs`
- Modify: `tests/BlazorCompose.Compiler.Tests/PartialComponentAnalyzerTests.cs`
- Modify: `tests/BlazorCompose.Compiler.Tests/RenderMutationAnalyzerTests.cs`
- Modify: `tests/BlazorCompose.Compiler.Tests/SequenceAllocatorTests.cs`

**Interfaces:**
- Consumes: Existing compiler test behavior.
- Produces: Three-part test identifiers for analyzer, model, template, and allocator contracts.

- [ ] **Step 1: Rename all `[Fact]` and `[Theory]` methods**

Use the tested compiler capability as the first segment:

```csharp
ComposableDefinition_UnsupportedDeclaration_ReportsBC1002()
ExpressionTemplate_WhenSubstituted_ReplacesOnlyParameterHoles()
PartialComponentAnalyzer_NonPartialComponent_ReportsBC1001AtClassIdentifier()
RenderMutationAnalyzer_IncrementInsideButtonHandler_DoesNotReportBC3001()
SequenceAllocator_TextNode_HasWidthTwo()
```

Keep diagnostic IDs and expected outcomes explicit in names.

- [ ] **Step 2: Verify naming mechanically**

Run the naming check from Task 2 against these five files.

Expected: no output.

- [ ] **Step 3: Build and run compiler tests**

Run:

```bash
dotnet build BlazorCompose.slnx --no-restore
dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --no-build
```

Expected: build succeeds and all compiler tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/BlazorCompose.Compiler.Tests
git commit -m "test: standardize focused compiler test names"
```

### Task 4: Rename Generator and Incremental Tests

**Files:**
- Modify: `tests/BlazorCompose.Compiler.Tests/GeneratorTests.cs`
- Modify: `tests/BlazorCompose.Compiler.Tests/IncrementalGeneratorTests.cs`

**Interfaces:**
- Consumes: Generator and incremental pipeline contracts.
- Produces: Three-part names for every remaining compiler test.

- [ ] **Step 1: Rename generator tests**

Use `Generator` as the subject when the operation is the complete generation pipeline:

```csharp
Generator_PartialComponent_EmitsRenderBody()
Generator_RecursiveComposableCycle_ReportsBC1002AndEmitsNoSource()
Generator_NameofParameter_EmitsCompileTimeConstantString()
```

Use `IncrementalGenerator` for caching and invalidation behavior:

```csharp
IncrementalGenerator_WhenUnrelatedTreeChanges_CachesUnchangedComponent()
IncrementalGenerator_WhenComposableDefinitionChanges_InvalidatesOnlyDependentCaller()
IncrementalGenerator_OnIdenticalRerun_CachesComposableRegistry()
```

Rename every `[Fact]` and `[Theory]` in both files. Do not change source fixtures, assertions, or generator configuration.

- [ ] **Step 2: Verify the whole test suite naming convention**

Run:

```bash
perl -ne 'if (/\[(?:Fact|Theory)\]/) {$pending=1; next} if ($pending && /public\s+(?:async\s+)?(?:void|Task|ValueTask)\s+([A-Za-z0-9_]+)\s*\(/) { print "$ARGV:$1\n" unless $1 =~ /^[^_]+_[^_]+_[^_]+$/; $pending=0 }' tests/**/*.cs
```

Expected: no output.

- [ ] **Step 3: Run generator tests**

Run:

```bash
dotnet test tests/BlazorCompose.Compiler.Tests/BlazorCompose.Compiler.Tests.csproj --no-build --filter FullyQualifiedName~GeneratorTests
```

Expected: all selected tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/BlazorCompose.Compiler.Tests/GeneratorTests.cs tests/BlazorCompose.Compiler.Tests/IncrementalGeneratorTests.cs
git commit -m "test: standardize generator test names"
```

### Task 5: Validate the Complete Migration

**Files:**
- Verify: `README.md`
- Verify: `tests/**/*.cs`

**Interfaces:**
- Consumes: Tasks 1-4.
- Produces: A warning-clean build and passing in-solution test suite with no legacy test names.

- [ ] **Step 1: Restore**

Run:

```bash
dotnet restore BlazorCompose.slnx
```

Expected: restore succeeds.

- [ ] **Step 2: Build**

Run:

```bash
dotnet build BlazorCompose.slnx --no-restore
```

Expected: build succeeds with no repository-owned warnings.

- [ ] **Step 3: Run all in-solution tests**

Run:

```bash
dotnet test BlazorCompose.slnx --no-build
```

Expected: all tests pass.

- [ ] **Step 4: Confirm no mock framework was introduced**

Run:

```bash
rg 'Moq|NSubstitute|Substitute\.For|new Mock<' . --glob '*.{cs,csproj,props,targets}'
```

Expected: no output.

- [ ] **Step 5: Confirm only intended files changed**

Run:

```bash
git --no-pager status --short
git --no-pager diff --check
```

Expected: only the planned documentation and test files are changed, and `diff --check` reports no errors.

- [ ] **Step 6: Commit validation adjustments if required**

If validation required a correction, commit only that correction:

```bash
git add README.md tests
git commit -m "test: complete test convention migration"
```
