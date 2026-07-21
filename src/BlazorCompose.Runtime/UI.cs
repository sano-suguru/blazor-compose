namespace BlazorCompose;

public static class UI
{
    public static View Text(string content) => default;

    public static View Button(string label, Action onClick) => default;

    public static View VStack(params View[] children) => default;

    public static View If(bool condition, Func<View> then, Func<View>? otherwise = null) => default;
}
