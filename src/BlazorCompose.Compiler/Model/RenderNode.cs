namespace BlazorCompose.Compiler;

/// <summary>
/// Discriminated union of statically sequenceable UI nodes extracted from a <c>Body</c> expression.
/// All fields contain only immutable templates and primitive values so that instances are immutable and value-equal.
/// No syntax nodes, symbols, semantic models, or absolute TextSpan offsets are stored here.
/// </summary>
internal abstract record RenderNode;

/// <summary>Represents a <c>Text(expr)</c> call that emits an HTML <c>span</c>.</summary>
internal sealed record TextNode(ExpressionTemplate ContentExpression) : RenderNode;

/// <summary>Represents a <c>Button(label, handler)</c> call that emits an HTML <c>button</c>.</summary>
internal sealed record ButtonNode(
    ExpressionTemplate LabelExpression,
    ExpressionTemplate HandlerExpression) : RenderNode;

/// <summary>Represents a <c>VStack(children…)</c> call that emits an HTML <c>div</c> wrapper.</summary>
internal sealed record VStackNode(EquatableArray<RenderNode> Children) : RenderNode;

/// <summary>Represents an <c>If(condition, then, otherwise)</c> call with an optional else branch.</summary>
internal sealed record IfNode(ExpressionTemplate ConditionExpression, RenderNode Then, RenderNode? Otherwise) : RenderNode;

internal sealed record LocalBinding(
    string TypeName,
    string Name,
    ExpressionTemplate Initializer);

internal sealed record ExpansionNode(
    EquatableArray<LocalBinding> Locals,
    RenderNode Body) : RenderNode;

/// <summary>
/// Represents a <c>ForEach(source, key, content)</c> call. Emits a keyed <c>foreach</c> region:
/// the content template occupies one static sequence space reused every iteration, and
/// <see cref="LoopVariableName"/> is the generated iteration variable that content/key expressions
/// were substituted onto.
/// </summary>
internal sealed record ForEachNode(
    ExpressionTemplate Source,
    ExpressionTemplate Key,
    RenderNode Content,
    string LoopVariableName) : RenderNode;
