namespace BlazorCompose;

/// <summary>
/// Design-time factory syntax for composing a <see cref="ComposeComponentBase.Body"/> expression.
/// </summary>
/// <remarks>
/// Every member is inert design-time syntax: the BlazorCompose source generator analyzes calls to
/// these members and emits the equivalent <c>RenderTreeBuilder</c> instructions into the component's
/// generated <c>RenderBody</c>. The members are never meant to run — at runtime they perform no work
/// and return only a default value (the default <see cref="View"/>, or a default builder such as
/// <see cref="ComponentView{TComponent}"/> that itself yields a default <see cref="View"/>), so they
/// must not be invoked directly.
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

    /// <summary>Design-time syntax for a keyed list: one <paramref name="content"/> template per item.</summary>
    /// <typeparam name="T">The element type of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence rendered one template per element.</param>
    /// <param name="key">Selects a value identifying each item; drives Blazor's keyed diffing.</param>
    /// <param name="content">Produces the template for one item.</param>
    /// <returns>Inert design-time syntax; always the default <see cref="View"/> at runtime.</returns>
    public static View ForEach<T>(
        System.Collections.Generic.IEnumerable<T> source,
        System.Func<T, object?> key,
        System.Func<T, View> content) => default;

    /// <summary>Design-time syntax for embedding an existing Blazor component into the compose tree.</summary>
    /// <typeparam name="TComponent">The Blazor component type to render.</typeparam>
    /// <returns>Inert design-time syntax; always the default value at runtime.</returns>
    public static ComponentView<TComponent> Component<TComponent>()
        where TComponent : Microsoft.AspNetCore.Components.IComponent => default;
}

/// <summary>
/// Inert design-time builder for a <see cref="UI.Component{TComponent}"/> call. The source generator
/// reads the <see cref="Param{TValue}"/> chain statically and emits <c>OpenComponent</c>/
/// <c>AddComponentParameter</c> instructions; instances are never constructed or evaluated at runtime.
/// </summary>
/// <typeparam name="TComponent">The Blazor component type being configured.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "ComponentView<TComponent> is inert design-time syntax with no state to compare; " +
        "it is read by the source generator and never constructed, compared, or persisted at runtime.")]
public readonly struct ComponentView<TComponent>
    where TComponent : Microsoft.AspNetCore.Components.IComponent
{
    /// <summary>Design-time syntax binding a component parameter selected by <paramref name="selector"/>.</summary>
    /// <typeparam name="TValue">The parameter's value type, inferred from the selected property.</typeparam>
    /// <param name="selector">Selects the target parameter property, e.g. <c>c =&gt; c.Items</c>.</param>
    /// <param name="value">The value bound to the selected parameter.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Param<TValue>(System.Func<TComponent, TValue> selector, TValue value) => this;

    /// <summary>Converts the inert builder to the marker <see cref="View"/> so it composes as a child.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2225:Operator overloads have named alternates",
        Justification = "This mirrors the other UI factory methods that return View directly; a named " +
            "alternate would suggest the conversion does real work, but it is inert design-time syntax " +
            "read by the source generator and always yields the default View at runtime.")]
    public static implicit operator View(ComponentView<TComponent> _) => default;
}
