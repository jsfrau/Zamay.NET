using System.Collections.Concurrent;

namespace Zamay.Core;

internal static class TypeDisplayName
{
    private static readonly ConcurrentDictionary<Type, string> Cache = new();
    internal static string Of(Type type) => Cache.GetOrAdd(type, static t =>
    {
        if (t.Name.Contains("AnonymousType", StringComparison.Ordinal)) return "Anonymous object";
        if (t.Name.StartsWith('<')) return "Enumerable";
        if (t.IsArray) return Of(t.GetElementType()!) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
        if (!t.IsGenericType) return t.Name;
        return t.Name.Split('`')[0] + "<" + string.Join(", ", t.GetGenericArguments().Select(Of)) + ">";
    });
}
