using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace BlazorCompose.TrimTests;

/// <summary>
/// Inspects the trimmed output of <c>BlazorCompose.TrimTestApp</c> using System.Reflection.Metadata
/// to verify that the trimmer behaves according to the architecture's expectations:
/// - Generated <c>RenderBody</c> must be retained (it is rooted by <c>BuildRenderTree</c>).
/// - The <c>Body</c> getter should be trimmed from both derived and base types (no runtime caller).
/// - All unreferenced inert factory methods in <c>BlazorCompose.UI</c> should be trimmed.
/// </summary>
public sealed class TrimmedOutputTests
{
    private static readonly string? TrimOutputDirectory =
        Environment.GetEnvironmentVariable("BLAZORCOMPOSE_TRIM_OUTPUT");

    [Fact]
    public void RenderBodyMethodIsRetainedInTrimmedAppAssembly()
    {
        EnsureOutputDirectoryExists();

        var appAssemblyPath = Path.Combine(TrimOutputDirectory!, "BlazorCompose.TrimTestApp.dll");
        Assert.True(File.Exists(appAssemblyPath), $"App assembly not found at: {appAssemblyPath}");

        var methods = GetMethodNames(appAssemblyPath, "TrimCounter", expectedNamespace: "");
        Assert.Contains("RenderBody", methods);
    }

    [Fact]
    public void BodyGetterIsAbsentFromTrimmedAppAssembly()
    {
        EnsureOutputDirectoryExists();

        var appAssemblyPath = Path.Combine(TrimOutputDirectory!, "BlazorCompose.TrimTestApp.dll");
        Assert.True(File.Exists(appAssemblyPath), $"App assembly not found at: {appAssemblyPath}");

        var methods = GetMethodNames(appAssemblyPath, "TrimCounter", expectedNamespace: "");

        // The Body getter should be trimmed since RenderBody (generated) doesn't call it
        // and no other code in the app invokes it.
        Assert.DoesNotContain("get_Body", methods);
    }

    [Fact]
    public void PrivateComposableMethodIsAbsentFromTrimmedAppAssembly()
    {
        EnsureOutputDirectoryExists();

        var appAssemblyPath = Path.Combine(
            TrimOutputDirectory!,
            "BlazorCompose.TrimTestApp.dll");
        var methods = GetMethodNames(
            appAssemblyPath,
            "TrimCounter",
            expectedNamespace: "");

        Assert.DoesNotContain("CountLabel", methods);
    }

    [Fact]
    public void BaseBodyGetterIsAbsentFromTrimmedRuntimeAssembly()
    {
        EnsureOutputDirectoryExists();

        var runtimeAssemblyPath = Path.Combine(TrimOutputDirectory!, "BlazorCompose.Runtime.dll");
        Assert.True(File.Exists(runtimeAssemblyPath), $"Runtime assembly not found at: {runtimeAssemblyPath}");

        var methods = GetMethodNames(runtimeAssemblyPath, "ComposeComponentBase", expectedNamespace: "BlazorCompose");

        // The abstract Body getter in the base class should also be trimmed — no runtime call path.
        Assert.DoesNotContain("get_Body", methods);
    }

    [Fact]
    public void AllInertFactoryMethodsAreAbsentFromTrimmedRuntimeAssembly()
    {
        EnsureOutputDirectoryExists();

        var runtimeAssemblyPath = Path.Combine(TrimOutputDirectory!, "BlazorCompose.Runtime.dll");
        Assert.True(File.Exists(runtimeAssemblyPath), $"Runtime assembly not found at: {runtimeAssemblyPath}");

        var methods = GetMethodNames(runtimeAssemblyPath, "UI", expectedNamespace: "BlazorCompose");

        // All four initial inert factories are unreachable at runtime — the source generator
        // inlines their semantics into RenderBody via direct RenderTreeBuilder calls.
        Assert.DoesNotContain("Text", methods);
        Assert.DoesNotContain("Button", methods);
        Assert.DoesNotContain("VStack", methods);
        Assert.DoesNotContain("If", methods);
    }

    [Fact]
    public void ComposeComponentBaseRetainsBuildRenderTree()
    {
        EnsureOutputDirectoryExists();

        var runtimeAssemblyPath = Path.Combine(TrimOutputDirectory!, "BlazorCompose.Runtime.dll");
        Assert.True(File.Exists(runtimeAssemblyPath), $"Runtime assembly not found at: {runtimeAssemblyPath}");

        var methods = GetMethodNames(runtimeAssemblyPath, "ComposeComponentBase", expectedNamespace: "BlazorCompose");

        // BuildRenderTree is the root that keeps the rendering chain alive.
        Assert.Contains("BuildRenderTree", methods);
    }

    /// <summary>
    /// Gets method definition names (metadata-level) for a given type in the assembly.
    /// Matches type by both namespace and short name to avoid ambiguity.
    /// A method removed by the trimmer will not have a MethodDef row at all.
    /// </summary>
    /// <param name="assemblyPath">Path to the PE assembly to inspect.</param>
    /// <param name="typeName">Short type name to match.</param>
    /// <param name="expectedNamespace">
    /// Expected namespace for the type. Use empty string for global/anonymous namespace (top-level statements).
    /// </param>
    private static HashSet<string> GetMethodNames(string assemblyPath, string typeName, string expectedNamespace)
    {
        using var fileStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(fileStream);
        var metadataReader = peReader.GetMetadataReader();

        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            var name = metadataReader.GetString(typeDef.Name);
            var ns = metadataReader.GetString(typeDef.Namespace);

            if (!string.Equals(name, typeName, StringComparison.Ordinal) ||
                !string.Equals(ns, expectedNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                var methodName = metadataReader.GetString(methodDef.Name);
                result.Add(methodName);
            }
        }

        return result;
    }

    /// <summary>
    /// Asserts that the trim output directory is set and exists. This is an architecture gate —
    /// missing output is a hard failure, not a skippable condition.
    /// </summary>
    private static void EnsureOutputDirectoryExists()
    {
        Assert.False(
            string.IsNullOrEmpty(TrimOutputDirectory),
            "BLAZORCOMPOSE_TRIM_OUTPUT environment variable is not set. " +
            "This is an architecture gate: publish the TrimTestApp first, then run tests " +
            "with the variable pointing to the publish directory.");

        Assert.True(
            Directory.Exists(TrimOutputDirectory),
            $"BLAZORCOMPOSE_TRIM_OUTPUT points to a directory that does not exist: {TrimOutputDirectory}");
    }
}
