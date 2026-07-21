namespace BlazorCompose.Compiler;

/// <summary>Represents a discovered Compose component extracted from user source.</summary>
internal sealed record ComponentModel(
    string HintName,
    string ClassName,
    string? Namespace);
