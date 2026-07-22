# Test Conventions Design

## Scope

Adopt Microsoft's recommended three-part test naming convention across the
existing test suite and document a behavior-oriented testing policy for future
work.

This change covers:

- Renaming existing xUnit test methods without changing their behavior.
- Documenting naming and test-design conventions in `README.md`.
- Preserving tests for compiler output shape where the generated representation
  is an architectural contract.

It does not reorganize test projects, introduce a mocking framework, or rewrite
otherwise valid tests solely to change their style.

## Naming Convention

Test methods use:

```text
SubjectOrMethod_Scenario_ExpectedBehavior
```

Each segment must communicate:

1. The method, capability, or operation under test.
2. The relevant input, state, or action.
3. The observable expected result.

For integration tests that do not map naturally to one method, the first
segment names the capability:

```csharp
Counter_ClickingIncrement_RerendersWithIncrementedCount()
Generator_NonPartialComponent_ReportsBC1001()
```

Names should avoid redundant suffixes such as `Test` and should remain
understandable in test-runner output without reading the implementation.

## Testing Style

Tests primarily verify observable behavior and state:

- Returned values and exceptions.
- Component state and rendered DOM.
- Compiler diagnostics and locations.
- Generated code that must compile.
- Package contents and trimmed assembly metadata.

Real implementations and collaborators are preferred when they remain fast,
deterministic, and locally controlled. This includes Roslyn compilations and
drivers, Blazor rendering through bUnit, `RenderTreeBuilder`, package creation,
and metadata inspection.

Test doubles are reserved for boundaries that are slow, nondeterministic, or
outside the process, such as remote services, wall-clock time, or randomness.
Interaction assertions are used only when the interaction itself is part of the
public contract.

## Compiler-Specific Structural Tests

Generated source text, sequence numbers, incremental-generator cache behavior,
and diagnostic spans are architectural contracts in this repository. Tests may
therefore inspect these details directly.

Where practical, structural assertions should be paired with an outcome-level
assertion, such as compiling the generated output or exercising the generated
component through Blazor. Structural assertions should not be removed merely
because they are white-box tests.

## Test Organization

Test classes and files may correspond to production types when that creates a
clear, cohesive test subject. A one-to-one production-file-to-test-file mapping
is not required.

Tests should instead be grouped by capability or responsibility. A production
file does not require a dedicated test file when its behavior is already
covered through a higher-level public contract.

## Migration

All existing `[Fact]` and `[Theory]` methods will be renamed in one change to
avoid mixed conventions. Renaming must not alter test bodies or production
behavior.

The migration is complete when:

- Every test method follows the three-part underscore convention.
- No test behavior or coverage is intentionally removed.
- The solution builds and all in-solution tests pass.
- `README.md` documents the naming and behavior-testing policy.
