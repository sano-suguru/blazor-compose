# Task 8 Report: Pack the Single Distribution

## Status

Completed.

## Commit

- SHA: `f180611`
- Subject: `build: package runtime and compiler together`

## What changed

1. `src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj`
   - Made Runtime the package owner with `PackageId` `BlazorCompose` and version `0.1.0-dev`.
   - Enabled packing and trimming metadata on Runtime.
   - Added a pack-specific target that rebuilds `BlazorCompose.Compiler`, copies `BlazorCompose.Compiler.dll` into Runtime-owned staging under `src/BlazorCompose.Runtime/obj/compiler-pack/analyzers/dotnet/cs`, and packs that staged analyzer.
   - Set the compiler `ProjectReference` to avoid normal build coupling while keeping pack-time orchestration explicit.

2. `Directory.Packages.props`
   - Added the local package version entry:
     - `BlazorCompose` → `0.1.0-dev`

3. `eng/verify-package.sh`
   - Added a package verification script that:
     - creates a unique extraction directory only under `artifacts/verify-package`
     - cleans up only that exact directory via a guarded `trap`
     - verifies the package-wide DLL set is exactly:
       - `analyzers/dotnet/cs/BlazorCompose.Compiler.dll`
       - `lib/net10.0/BlazorCompose.Runtime.dll`
     - fails if any `Microsoft.CodeAnalysis*.dll` is present

4. `tests/BlazorCompose.TrimTests/PackageContentsTests.cs`
   - Added real nupkg validation tests that:
     - open the produced `.nupkg` with `ZipArchive`
     - verify only the expected two DLLs are packed
     - verify no `Microsoft.CodeAnalysis*.dll` is packed
     - prove repeated pack runs do not keep stale staged compiler output
     - prove `dotnet pack --no-build` regenerates the staged compiler analyzer when it is missing

5. `AGENTS.md`
   - Added validated commands for pack, package verification, and the package-content test project.

## Self-review

- Requested read-only code review after implementation and after follow-up fixes.
- Final reviewer assessment: **Ready to merge: Yes**
- Final reviewer noted no blocking issues and confirmed plan alignment, package correctness, stale-output resistance, and real-package verification.

## Verification run

Executed successfully:

```bash
dotnet restore BlazorCompose.slnx
dotnet build BlazorCompose.slnx --no-restore
dotnet test BlazorCompose.slnx --no-build
dotnet test tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj --filter FullyQualifiedName~PackageContentsTests
dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release -o artifacts/package
bash eng/verify-package.sh artifacts/package/BlazorCompose.0.1.0-dev.nupkg
dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release --no-build -o artifacts/package
bash eng/verify-package.sh artifacts/package/BlazorCompose.0.1.0-dev.nupkg
unzip -Z1 artifacts/package/BlazorCompose.0.1.0-dev.nupkg | LC_ALL=C sort
```

## Pack verification summary

Verified actual final nupkg contents:

```text
BlazorCompose.nuspec
[Content_Types].xml
_rels/.rels
analyzers/dotnet/cs/BlazorCompose.Compiler.dll
lib/net10.0/BlazorCompose.Runtime.dll
package/services/metadata/core-properties/8923e591d1894fddba1a5087dd8c9d94.psmdcp
```

Expected DLL layout confirmed:

```text
analyzers/dotnet/cs/BlazorCompose.Compiler.dll
lib/net10.0/BlazorCompose.Runtime.dll
```

Rejected-content check:

- No `Microsoft.CodeAnalysis*.dll` was packed.

Repeated-run validation:

- Normal `dotnet pack` succeeded.
- A second `dotnet pack --no-build` also succeeded.
- `PackageContentsTests` verified repeated pack behavior and staged analyzer regeneration.

## Concerns

1. `dotnet pack` emits a non-blocking NuGet warning that the package is missing a readme.
   - This does not violate Task 8 requirements, but it remains a packaging quality follow-up item.

2. `tests/BlazorCompose.TrimTests` remains outside `BlazorCompose.slnx` by repository design at this stage.
   - I updated `AGENTS.md` with the validated command so the package-content gate is documented and reproducible.

## Packaging review follow-up

### Status

Completed.

### What changed

1. `src/BlazorCompose.Runtime/README.md`
   - Added an English NuGet package README limited to the current production-foundation vertical slice.

2. `src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj`
   - Added package metadata for `Authors`, `Description`, and `PackageReadmeFile`.
   - Packed `README.md` into the nupkg root so `dotnet pack` emits no readme warning.

3. `eng/verify-package.sh`
   - Verifies the exact payload-root file set:
     - `analyzers/dotnet/cs/BlazorCompose.Compiler.dll`
     - `lib/net10.0/BlazorCompose.Runtime.dll`
   - Rejects any extra files under `lib`, `analyzers`, `build`, `buildTransitive`, `contentFiles`, `tools`, or `runtimes`.
   - Allows only standard NuGet container metadata plus `README.md`.
   - Validates nuspec `id`, `version`, `readme`, and absence of dependency entries.

4. `tests/BlazorCompose.TrimTests/PackageContentsTests.cs`
   - Added an actual nupkg contents test covering payload paths, allowed metadata files, readme presence, nuspec metadata, and absence of dependency entries.
   - Renamed the staging-regeneration coverage to `PackWithoutBuildRegeneratesMissingStagedCompilerAnalyzer`.
   - Renamed the stale-output coverage to `RepeatedPackRebuildsCompilerAnalyzerInsteadOfPackingStaleStagedOutput`.

### Code review

- Requested a read-only review of the final diff.
- Reviewer result: no significant issues found; ready to merge.

### Exact validation results

Executed with pristine output:

```bash
dotnet restore BlazorCompose.slnx
```

Result:

```text
Determining projects to restore...
All projects are up-to-date for restore.
```

```bash
dotnet build BlazorCompose.slnx --no-restore
```

Result:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```bash
dotnet test BlazorCompose.slnx --no-build
```

Result:

```text
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 62 ms - BlazorCompose.Runtime.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 1 s - BlazorCompose.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 4 s - BlazorCompose.Compiler.Tests.dll (net10.0)
```

```bash
dotnet test tests/BlazorCompose.TrimTests/BlazorCompose.TrimTests.csproj --filter FullyQualifiedName~PackageContentsTests
```

Result:

```text
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 12 s - BlazorCompose.TrimTests.dll (net10.0)
```

```bash
dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release -o artifacts/package
bash eng/verify-package.sh artifacts/package/BlazorCompose.0.1.0-dev.nupkg
dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release --no-build -o artifacts/package
bash eng/verify-package.sh artifacts/package/BlazorCompose.0.1.0-dev.nupkg
```

Result:

```text
Successfully created package '/Users/sanosuguru/dev/dotnet-compose/.worktrees/production-foundation/artifacts/package/BlazorCompose.0.1.0-dev.nupkg'.
Verified package contents: artifacts/package/BlazorCompose.0.1.0-dev.nupkg
BlazorCompose.Compiler -> /Users/sanosuguru/dev/dotnet-compose/.worktrees/production-foundation/src/BlazorCompose.Compiler/bin/Release/netstandard2.0/BlazorCompose.Compiler.dll
Successfully created package '/Users/sanosuguru/dev/dotnet-compose/.worktrees/production-foundation/artifacts/package/BlazorCompose.0.1.0-dev.nupkg'.
Verified package contents: artifacts/package/BlazorCompose.0.1.0-dev.nupkg
```

Observed package entries after both pack runs:

```text
BlazorCompose.nuspec
README.md
[Content_Types].xml
_rels/.rels
analyzers/dotnet/cs/BlazorCompose.Compiler.dll
lib/net10.0/BlazorCompose.Runtime.dll
package/services/metadata/core-properties/*.psmdcp
```

Observed nuspec metadata after both pack runs:

```text
id=BlazorCompose
version=0.1.0-dev
readme=README.md
dependencies=<group targetFramework="net10.0" />
frameworkReference=Microsoft.AspNetCore.App
```

### Concerns

1. `tests/BlazorCompose.TrimTests` remains outside `BlazorCompose.slnx` by repository design at this stage.
   - The separate validated command remains necessary for the package-content gate.
