using System.Diagnostics;
using System.IO.Compression;

namespace BlazorCompose.TrimTests;

public sealed class PackageContentsTests
{
    [Fact]
    public void RepeatedPackProducesRuntimeAndFreshCompilerAssetsWithoutRoslynAssemblies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageOutputRoot = Path.Combine(repositoryRoot, "artifacts", "package-tests", nameof(RepeatedPackProducesRuntimeAndFreshCompilerAssetsWithoutRoslynAssemblies));
        var firstPackageOutputDirectory = Path.Combine(packageOutputRoot, "first");
        var secondPackageOutputDirectory = Path.Combine(packageOutputRoot, "second");
        var secondPackagePath = Path.Combine(secondPackageOutputDirectory, "BlazorCompose.0.1.0-dev.nupkg");
        var runtimeProjectPath = Path.Combine(repositoryRoot, "src", "BlazorCompose.Runtime", "BlazorCompose.Runtime.csproj");
        var verificationScriptPath = Path.Combine(repositoryRoot, "eng", "verify-package.sh");
        var compilerOutputPath = GetPackCompilerAssemblyPath(repositoryRoot);

        if (Directory.Exists(packageOutputRoot))
        {
            Directory.Delete(packageOutputRoot, recursive: true);
        }

        Directory.CreateDirectory(firstPackageOutputDirectory);
        Directory.CreateDirectory(secondPackageOutputDirectory);

        RunDotNetPack(runtimeProjectPath, firstPackageOutputDirectory, repositoryRoot);

        Directory.CreateDirectory(Path.GetDirectoryName(compilerOutputPath)!);
        var staleCompilerBytes = "stale-compiler-output"u8.ToArray();

        try
        {
            File.WriteAllBytes(compilerOutputPath, staleCompilerBytes);

            RunDotNetPack(runtimeProjectPath, secondPackageOutputDirectory, repositoryRoot);
        }
        finally
        {
            if (File.Exists(compilerOutputPath))
            {
                File.Delete(compilerOutputPath);
            }
        }

        RunBash(verificationScriptPath, secondPackagePath, repositoryRoot);

        using var packageStream = File.OpenRead(secondPackagePath);
        using var packageArchive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        var packagedDllPaths = packageArchive.Entries
            .Where(static entry => entry.FullName.EndsWith(".dll", StringComparison.Ordinal))
            .Select(static entry => entry.FullName)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "analyzers/dotnet/cs/BlazorCompose.Compiler.dll",
                "lib/net10.0/BlazorCompose.Runtime.dll"
            ],
            packagedDllPaths);

        Assert.DoesNotContain(
            packageArchive.Entries,
            static entry => Path.GetFileName(entry.FullName).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                && entry.FullName.EndsWith(".dll", StringComparison.Ordinal));

        var compilerEntry = packageArchive.GetEntry("analyzers/dotnet/cs/BlazorCompose.Compiler.dll");

        Assert.NotNull(compilerEntry);

        var packagedCompilerBytes = ReadAllBytes(compilerEntry);

        Assert.Equal((byte)'M', packagedCompilerBytes[0]);
        Assert.Equal((byte)'Z', packagedCompilerBytes[1]);
        Assert.False(packagedCompilerBytes.AsSpan().SequenceEqual(staleCompilerBytes));
    }

    [Fact]
    public void PackWithoutBuildRegeneratesMissingCompilerOutput()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageOutputDirectory = Path.Combine(repositoryRoot, "artifacts", "package-tests", nameof(PackWithoutBuildRegeneratesMissingCompilerOutput));
        var packagePath = Path.Combine(packageOutputDirectory, "BlazorCompose.0.1.0-dev.nupkg");
        var runtimeProjectPath = Path.Combine(repositoryRoot, "src", "BlazorCompose.Runtime", "BlazorCompose.Runtime.csproj");
        var verificationScriptPath = Path.Combine(repositoryRoot, "eng", "verify-package.sh");
        var compilerOutputPath = GetPackCompilerAssemblyPath(repositoryRoot);

        if (Directory.Exists(packageOutputDirectory))
        {
            Directory.Delete(packageOutputDirectory, recursive: true);
        }

        Directory.CreateDirectory(packageOutputDirectory);

        RunDotNetBuild(runtimeProjectPath, configuration: "Release", repositoryRoot);

        if (File.Exists(compilerOutputPath))
        {
            File.Delete(compilerOutputPath);
        }

        RunDotNetPack(runtimeProjectPath, packageOutputDirectory, repositoryRoot, noBuild: true);
        RunBash(verificationScriptPath, packagePath, repositoryRoot);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BlazorCompose.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static string GetPackCompilerAssemblyPath(string repositoryRoot)
        => Path.Combine(
            repositoryRoot,
            "src",
            "BlazorCompose.Runtime",
            "obj",
            "compiler-pack",
            "analyzers",
            "dotnet",
            "cs",
            "BlazorCompose.Compiler.dll");

    private static void RunDotNetPack(string projectPath, string packageOutputDirectory, string workingDirectory)
        => RunDotNetPack(projectPath, packageOutputDirectory, workingDirectory, noBuild: false);

    private static void RunDotNetPack(string projectPath, string packageOutputDirectory, string workingDirectory, bool noBuild)
    {
        RunProcess(
            fileName: "dotnet",
            arguments: $"pack \"{projectPath}\" -c Release{(noBuild ? " --no-build" : string.Empty)} -o \"{packageOutputDirectory}\"",
            workingDirectory);
    }

    private static void RunDotNetBuild(string projectPath, string configuration, string workingDirectory)
    {
        RunProcess(
            fileName: "dotnet",
            arguments: $"build \"{projectPath}\" -c {configuration}",
            workingDirectory);
    }

    private static void RunBash(string scriptPath, string packagePath, string workingDirectory)
    {
        RunProcess(
            fileName: "bash",
            arguments: $"\"{scriptPath}\" \"{packagePath}\"",
            workingDirectory);
    }

    private static void RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            throw new InvalidOperationException($"Could not start process '{fileName}'.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();

        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();

        Assert.True(
            process.ExitCode == 0,
            $"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{standardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{standardError}");
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memoryStream = new MemoryStream();

        stream.CopyTo(memoryStream);

        return memoryStream.ToArray();
    }
}
