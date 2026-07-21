using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace BlazorCompose.TrimTests;

/// <summary>
/// Inspects the trimmed output of <c>BlazorCompose.TrimTestApp</c> using System.Reflection.Metadata
/// to verify that the trimmer behaves according to the architecture's expectations:
/// - Generated <c>RenderBody</c> must be retained (it is rooted by <c>BuildRenderTree</c>).
/// - The abstract <c>Body</c> getter should be trimmed (no runtime caller).
/// - Unreferenced inert factory methods in <c>BlazorCompose.UI</c> should be trimmed.
/// </summary>
public sealed class TrimmedOutputTests
{
    private static readonly string? TrimOutputDirectory =
        Environment.GetEnvironmentVariable("BLAZORCOMPOSE_TRIM_OUTPUT");

    [Fact]
    public void RenderBodyMethodIsRetainedInTrimmedAppAssembly()
    {
        SkipIfOutputDirectoryNotSet();

        var appAssemblyPath = Path.Combine(TrimOutputDirectory!, "BlazorCompose.TrimTestApp.dll");
        Assert.True(File.Exists(appAssemblyPath), $"App assembly not found at: {appAssemblyPath}");

        var methods = GetMethodNames(appAssemblyPath, "TrimCounter");
        Assert.Contains("RenderBody", methods);
    }

    [Fact]
    public void BodyGetterIsAbsentFromTrimmedAppAssembly()
    {
        SkipIfOutputDirectoryNotSet();

        var appAssemblyPath = Path.Combine(TrimOutputDirectory!, "BlazorCompose.TrimTestApp.dll");
        Assert.True(File.Exists(appAssemblyPath), $"App assembly not found at: {appAssemblyPath}");

        var methods = GetMethodNames(appAssemblyPath, "TrimCounter");

        // The Body getter should be trimmed since RenderBody (generated) doesn't call it
        // and no other code in the app invokes it.
        Assert.DoesNotContain("get_Body", methods);
    }

    [Fact]
    public void UnreferencedFactoryMethodsAreAbsentFromTrimmedRuntimeAssembly()
    {
        SkipIfOutputDirectoryNotSet();

        var runtimeAssemblyPath = Path.Combine(TrimOutputDirectory!, "BlazorCompose.Runtime.dll");
        Assert.True(File.Exists(runtimeAssemblyPath), $"Runtime assembly not found at: {runtimeAssemblyPath}");

        var methods = GetMethodNames(runtimeAssemblyPath, "UI");

        // The If factory is not referenced by TrimCounter's Body expression or the generated
        // RenderBody, so it should be removed by the trimmer.
        Assert.DoesNotContain("If", methods);
    }

    [Fact]
    public void ComposeComponentBaseRetainsBuildRenderTree()
    {
        SkipIfOutputDirectoryNotSet();

        var runtimeAssemblyPath = Path.Combine(TrimOutputDirectory!, "BlazorCompose.Runtime.dll");
        Assert.True(File.Exists(runtimeAssemblyPath), $"Runtime assembly not found at: {runtimeAssemblyPath}");

        var methods = GetMethodNames(runtimeAssemblyPath, "ComposeComponentBase");

        // BuildRenderTree is the root that keeps the rendering chain alive.
        Assert.Contains("BuildRenderTree", methods);
    }

    /// <summary>
    /// Gets method definition names (metadata-level, not body-only) for a given type in the assembly.
    /// This reads the type's MethodDef table entries, which are present even if bodies were emptied.
    /// A method removed by the trimmer will not have a MethodDef row at all.
    /// </summary>
    private static HashSet<string> GetMethodNames(string assemblyPath, string typeName)
    {
        using var fileStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(fileStream);
        var metadataReader = peReader.GetMetadataReader();

        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            var name = metadataReader.GetString(typeDef.Name);

            if (!string.Equals(name, typeName, StringComparison.Ordinal))
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

    private static void SkipIfOutputDirectoryNotSet()
    {
        if (string.IsNullOrEmpty(TrimOutputDirectory))
        {
            // In xunit v2, runtime skip is not natively supported.
            // Fail explicitly so the caller knows the env var is required.
            Assert.Fail(
                "BLAZORCOMPOSE_TRIM_OUTPUT environment variable is not set. " +
                "Publish the TrimTestApp first, then run tests with the variable pointing to the publish directory.");
        }
    }
}
