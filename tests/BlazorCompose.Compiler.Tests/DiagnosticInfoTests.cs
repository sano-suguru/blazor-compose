using System.Linq;
using System.Reflection;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class DiagnosticInfoTests
{
    // A [Fact] that iterates all descriptors internally, rather than a [Theory] with a
    // DiagnosticDescriptor member-data parameter, to avoid xUnit1045 (non-serializable theory data)
    // under TreatWarningsAsErrors.
    [Fact]
    public void ToDiagnostic_ForEveryDescriptor_RoundTripsToSameDescriptor()
    {
        var descriptors = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToArray();

        Assert.NotEmpty(descriptors);
        foreach (var descriptor in descriptors)
        {
            var diagnostic = DiagnosticInfo.Create(descriptor, Location.None, []).ToDiagnostic();
            Assert.Equal(descriptor.Id, diagnostic.Id);
            Assert.Same(descriptor, diagnostic.Descriptor);
        }
    }

    [Fact]
    public void IsError_ForWarningDescriptor_IsFalse()
    {
        var info = DiagnosticInfo.Create(DiagnosticDescriptors.BC3002, Location.None, []);

        Assert.False(info.IsError);
    }

    [Theory]
    [InlineData("BC3005")]
    [InlineData("BC3006")]
    public void ById_ResolvesNewComponentDiagnostics_AsErrors(string id)
    {
        var descriptor = BlazorCompose.Compiler.Diagnostics.DiagnosticDescriptors.ById(id);

        Assert.Equal(id, descriptor.Id);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }
}
