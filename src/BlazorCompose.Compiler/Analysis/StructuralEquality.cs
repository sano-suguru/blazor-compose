using System.Collections.Generic;
using System.Collections.Immutable;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Shared structural equality helpers for value-equal records that carry
/// <see cref="ImmutableArray{T}"/> fields.  <see cref="ImmutableArray{T}"/> uses reference equality in
/// compiler-generated record members, which the incremental pipeline cannot rely on for caching.
/// </summary>
internal static class StructuralEquality
{
    public static bool ArrayEquals<T>(ImmutableArray<T> left, ImmutableArray<T> right)
    {
        if (left.IsDefaultOrEmpty && right.IsDefaultOrEmpty)
            return true;
        if (left.IsDefault || right.IsDefault || left.Length != right.Length)
            return false;

        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < left.Length; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
                return false;
        }

        return true;
    }

    public static int ArrayHashCode<T>(ImmutableArray<T> values)
    {
        if (values.IsDefaultOrEmpty)
            return 0;

        var hash = 17;
        foreach (var value in values)
            hash = unchecked(hash * 31 + (value?.GetHashCode() ?? 0));
        return hash;
    }
}
