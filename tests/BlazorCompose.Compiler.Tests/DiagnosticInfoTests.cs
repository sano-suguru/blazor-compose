using System.Collections.Immutable;
using System.Globalization;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class DiagnosticInfoTests
{
    [Fact]
    public void ToDiagnostic_ResolvesDescriptorFromId()
    {
        var info = DiagnosticInfo.Create("BC3002", Location.None, []);

        var diagnostic = info.ToDiagnostic();

        Assert.Equal("BC3002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }
}
