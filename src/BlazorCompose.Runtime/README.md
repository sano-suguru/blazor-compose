# BlazorCompose

`BlazorCompose` `0.1.0-dev` is a prerelease package for the current production-foundation vertical slice.

## Included assets

- `lib/net10.0/BlazorCompose.Runtime.dll` for runtime support
- `analyzers/dotnet/cs/BlazorCompose.Compiler.dll` for compiler-time analysis and generation

## Current scope

This package exists to validate that the BlazorCompose runtime and compiler ship together as a single NuGet distribution. The broader product direction remains defined by the repository whitepaper and yellowpaper.

## Requirements

- .NET 10 / `net10.0`
- A Blazor project that references the `BlazorCompose` package
