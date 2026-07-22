namespace BlazorCompose;

/// <summary>
/// A design-time-only marker for a node in a <see cref="ComposeComponentBase.Body"/> expression.
/// </summary>
/// <remarks>
/// <see cref="View"/> is inert syntax analyzed by the BlazorCompose source generator, not a runtime UI
/// value. It carries no state and is never rendered directly; the generator translates the
/// <c>Body</c> expression that produces it into a <c>RenderBody</c> override. Instances observed at
/// runtime are always the default value and must not be inspected or acted upon.
/// </remarks>
public readonly struct View;
