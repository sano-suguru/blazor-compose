namespace BlazorCompose;

/// <summary>
/// Design-time factory syntax for composing a <see cref="ComposeComponentBase.Body"/> expression.
/// </summary>
/// <remarks>
/// Every member is inert design-time syntax: the BlazorCompose source generator analyzes calls to
/// these methods and emits the equivalent <c>RenderTreeBuilder</c> instructions into the component's
/// generated <c>RenderBody</c>. The methods are never meant to run — at runtime they return the
/// default <see cref="View"/> and perform no work, so they must not be invoked directly.
/// </remarks>
public static class UI
{
    /// <summary>Design-time syntax for a text node rendered as an HTML <c>span</c>.</summary>
    /// <param name="content">The text content to render.</param>
    /// <returns>Inert design-time syntax; always the default <see cref="View"/> at runtime.</returns>
    public static View Text(string content) => default;

    /// <summary>Design-time syntax for a clickable node rendered as an HTML <c>button</c>.</summary>
    /// <param name="label">The button label text.</param>
    /// <param name="onClick">The handler invoked when the button is clicked.</param>
    /// <returns>Inert design-time syntax; always the default <see cref="View"/> at runtime.</returns>
    public static View Button(string label, Action onClick) => default;

    /// <summary>Design-time syntax for a vertical stack rendered as an HTML <c>div</c> wrapper.</summary>
    /// <param name="children">The child nodes stacked in source order.</param>
    /// <returns>Inert design-time syntax; always the default <see cref="View"/> at runtime.</returns>
    public static View VStack(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for conditional rendering with an optional else branch.</summary>
    /// <param name="condition">The condition selecting which branch is rendered.</param>
    /// <param name="then">The branch rendered when <paramref name="condition"/> is <see langword="true"/>.</param>
    /// <param name="otherwise">The optional branch rendered when <paramref name="condition"/> is <see langword="false"/>.</param>
    /// <returns>Inert design-time syntax; always the default <see cref="View"/> at runtime.</returns>
    public static View If(bool condition, Func<View> then, Func<View>? otherwise = null) => default;
}
