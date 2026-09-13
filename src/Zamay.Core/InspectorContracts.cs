using System.Collections.Concurrent;
using System.Reflection;

namespace Zamay.Core;

/// <summary>Inspectors return model nodes; recurse through context to preserve budgets and cycle detection.
/// Registrations are immutable and must be thread-safe. Higher priority wins.</summary>
public interface IValueInspector
{
    int Priority { get; }
    bool CanHandle(Type type);
    DisplayNode Inspect(object value, InspectionContext context);
}
public sealed class InspectionContext
{
    private readonly InspectionSession session;
    internal InspectionContext(InspectionSession session, int depth, string path) { this.session = session; Depth = depth; Path = path; }
    public int Depth { get; }
    public string Path { get; }
    public InspectionOptions Options => session.Options;
    public CancellationToken CancellationToken => session.Token;
    public bool CanInspectChild => session.CanVisit(Depth + 1);
    public DisplayNode InspectChild(object? value, string name) => session.Visit(value, Depth + 1, Path + "." + name);
}
public sealed class TypeInspectorResolver
{
    private sealed record Resolution(IValueInspector? Inspector);
    private readonly IValueInspector[] inspectors;
    private readonly ConcurrentDictionary<Type, Lazy<Resolution>> cache = new();
    public TypeInspectorResolver(IEnumerable<IValueInspector> inspectors) => this.inspectors = inspectors.OrderByDescending(i => i.Priority).ToArray();
    public int CachedTypeCount => cache.Count;
    public IValueInspector? Resolve(Type type) => cache.GetOrAdd(type, t => new(() => new(inspectors.FirstOrDefault(i => i.CanHandle(t))), true)).Value.Inspector;
}
public sealed record ReflectedMember(string Name, bool IsField, bool Unsupported, Func<object, object?> Read);
public sealed class ReflectionMetadataCache
{
    private readonly ConcurrentDictionary<Type, Lazy<ReflectedMember[]>> cache = new();
    public int CachedTypeCount => cache.Count;
    internal ReflectedMember[] Get(Type type) => cache.GetOrAdd(type, t => new(() => Create(t), true)).Value;
    private static ReflectedMember[] Create(Type type)
    {
        return type.GetMembers(BindingFlags.Instance | BindingFlags.Public).OrderBy(m => m.MetadataToken)
            .Select<MemberInfo, ReflectedMember?>(m => m switch
            {
                PropertyInfo p when p.GetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0 =>
                    new(p.Name, false, p.PropertyType.IsByRefLike || p.PropertyType.IsByRef || p.PropertyType.IsPointer, p.GetValue),
                FieldInfo f when !f.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)) =>
                    new(f.Name, true, f.FieldType.IsByRefLike || f.FieldType.IsPointer, f.GetValue),
                _ => null
            }).OfType<ReflectedMember>().ToArray();
    }
}
