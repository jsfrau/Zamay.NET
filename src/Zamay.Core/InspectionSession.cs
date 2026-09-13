using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Reflection;

namespace Zamay.Core;

internal sealed class InspectionSession(ObjectInspector inspector, InspectionOptions options, CancellationToken token)
{
    private readonly Dictionary<object, string> path = new(ReferenceEqualityComparer.Instance);
    private int count;
    internal InspectionOptions Options => options;
    internal CancellationToken Token => token;
    internal void ReserveRoot(object value) { count++; path.Add(value, "$"); }
    internal bool CanVisit(int depth) { token.ThrowIfCancellationRequested(); return depth <= options.MaxDepth && count < options.MaxNodes; }
    internal DisplayNode Limit(int depth) => new PlaceholderNode(depth > options.MaxDepth ? PlaceholderReason.MaxDepthReached : PlaceholderReason.NodeBudgetReached);
    internal ScalarNode Scalar(string text, string type, bool quoted = false)
    {
        text = options.SensitiveData?.Sanitize(text) ?? text;
        return new(DisplayText.Truncate(text, options.MaxStringLength), type, quoted, text.Length > options.MaxStringLength);
    }
    internal ErrorNode Error(Exception ex)
    {
        if (ex is TargetInvocationException { InnerException: { } inner }) ex = inner;
        if (!InspectionErrors.Recoverable(ex)) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        return new(ex.GetType().Name, options.ExceptionDetail == ExceptionDetailLevel.TypeOnly ? null : Scalar(ex.Message, "string").Text);
    }
    internal DisplayNode Visit(object? value, int depth, string location)
    {
        if (!CanVisit(depth)) return Limit(depth);
        count++;
        if (value is null) return new ScalarNode("null", "null");
        var type = value.GetType();
        var track = !type.IsValueType && value is not string;
        if (track && path.TryGetValue(value, out var target)) return new ReferenceNode(target);
        if (track) path.Add(value, location);
        try
        {
            var custom = inspector.Resolver.Resolve(type);
            if (custom is not null) return custom.Inspect(value, new(this, depth, location));
            if (TryScalar(value, type, out var scalar)) return scalar!;
            if (value is DbConnection connection) return Descriptor(type, "State=" + connection.State);
            if (value is Stream) return Descriptor(type, "Stream (not read)");
            if (value is ValueTask || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))) return Descriptor(type, "ValueTask (not awaited)");
            if (value is Task task) return Descriptor(type, "Task Status=" + task.Status);
            if (value is IDataReader) return Descriptor(type, "IDataReader (not consumed)");
            if (value is Delegate) return Descriptor(type, "Delegate (not invoked)");
            if (ObjectInspector.GetAsyncType(type) is not null) return Descriptor(type, "IAsyncEnumerable (not enumerated)");
            if (value is byte[] bytes) return new ScalarNode(Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, options.MaxBytePreview))), $"byte[{bytes.Length}]", false, bytes.Length > options.MaxBytePreview);
            if (value is Exception ex) return ExceptionNode(ex, depth, location);
            if (value is DataTable table) return Table(table, depth, location);
            if (value is DataSet set) return Sequence(set.Tables, type, depth, location);
            if (value is IDictionary dictionary) return Dictionary(dictionary, type, depth, location);
            if (value is IEnumerable sequence) return Sequence(sequence, type, depth, location);
            return Object(value, type, depth, location);
        }
        catch (Exception ex) when (InspectionErrors.Recoverable(ex)) { return Error(ex); }
        finally { if (track) path.Remove(value); }
    }
    private static PlaceholderNode Descriptor(Type t, string detail) => new(PlaceholderReason.Descriptor, TypeDisplayName.Of(t) + ": " + detail);
    private bool TryScalar(object value, Type type, out DisplayNode? scalar)
    {
        string? text = value switch
        {
            string s => s,
            char c => c.ToString(),
            Type t => t.FullName ?? t.Name,
            MemberInfo m => m.ToString() ?? m.Name,
            AssemblyName a => a.FullName,
            Uri u => u.OriginalString,
            Version v => v.ToString(),
            IPAddress ip => ip.ToString(),
            IPEndPoint ep => ep.ToString(),
            CultureInfo ci => ci.Name,
            DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("O", CultureInfo.InvariantCulture),
            Guid g => g.ToString(),
            TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
            BigInteger b => b.ToString(CultureInfo.InvariantCulture),
            Half h => h.ToString(CultureInfo.InvariantCulture),
            _ when type.IsEnum => value.ToString(),
            IFormattable f when type.IsPrimitive || value is decimal => f.ToString(null, CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => null
        };
        scalar = text is null ? null : Scalar(text, TypeDisplayName.Of(type), value is string or char);
        return scalar is not null;
    }
    private ObjectNode Object(object value, Type type, int depth, string location)
    {
        IEnumerable<ReflectedMember> metadata = inspector.Metadata.Get(type).Where(m => options.IncludeFields || !m.IsField);
        if (options.SortMembers) metadata = metadata.OrderBy(m => m.Name, StringComparer.Ordinal);
        var members = ImmutableArray.CreateBuilder<DisplayMember>();
        foreach (var member in metadata)
        {
            token.ThrowIfCancellationRequested();
            if (!CanVisit(depth + 1)) { members.Add(new("…", Limit(depth + 1))); break; }
            if (members.Count >= options.MaxObjectMembers) { members.Add(new("…", new PlaceholderNode(PlaceholderReason.MaxMembersReached))); break; }
            DisplayNode node;
            if (options.SensitiveData?.IsSensitive(member.Name) == true) { count++; node = new PlaceholderNode(PlaceholderReason.Redacted); }
            else if (member.Unsupported) { count++; node = new PlaceholderNode(PlaceholderReason.Unsupported, "ref / pointer / byref-like"); }
            else
            {
                try { node = Visit(member.Read(value), depth + 1, location + "." + member.Name); }
                catch (Exception ex) when (InspectionErrors.Recoverable(ex)) { count++; node = Error(ex); }
            }
            members.Add(new(member.Name, node));
        }
        return new(TypeDisplayName.Of(type), members.ToImmutable());
    }
    private CollectionNode Sequence(IEnumerable values, Type type, int depth, string location)
    {
        var items = ImmutableArray.CreateBuilder<DisplayNode>();
        int? total = values is ICollection c ? c.Count : null;
        if (!CanVisit(depth + 1)) return new(TypeDisplayName.Of(type), [Limit(depth + 1)], total);
        if (options.MaxCollectionItems == 0) return new(TypeDisplayName.Of(type), [new PlaceholderNode(PlaceholderReason.MoreItemsMayExist)], total);
        var enumerator = values.GetEnumerator();
        try
        {
            while (items.Count < options.MaxCollectionItems && CanVisit(depth + 1))
            {
                if (!enumerator.MoveNext()) return new(TypeDisplayName.Of(type), items.ToImmutable(), total);
                items.Add(Visit(enumerator.Current, depth + 1, location + "[" + items.Count + "]"));
            }
            if (total is null || total > items.Count) items.Add(CanVisit(depth + 1) ? new PlaceholderNode(PlaceholderReason.MoreItemsMayExist) : Limit(depth + 1));
        }
        catch (Exception ex) when (InspectionErrors.Recoverable(ex)) { items.Add(Error(ex)); }
        finally { (enumerator as IDisposable)?.Dispose(); }
        return new(TypeDisplayName.Of(type), items.ToImmutable(), total);
    }
    private DictionaryNode Dictionary(IDictionary values, Type type, int depth, string location)
    {
        var entries = ImmutableArray.CreateBuilder<DictionaryEntryNode>();
        if (!CanVisit(depth + 1)) return new(TypeDisplayName.Of(type), [new(Limit(depth + 1), Limit(depth + 1))]);
        var enumerator = values.GetEnumerator();
        try
        {
            while (entries.Count < options.MaxCollectionItems && CanVisit(depth + 1))
            {
                if (!enumerator.MoveNext()) return new(TypeDisplayName.Of(type), entries.ToImmutable());
                var key = enumerator.Key;
                var keyNode = Visit(key, depth + 1, location + ".key");
                DisplayNode node;
                if (!CanVisit(depth + 1)) node = Limit(depth + 1);
                else if (key is string s && options.SensitiveData?.IsSensitive(s) == true) { count++; node = new PlaceholderNode(PlaceholderReason.Redacted); }
                else node = Visit(enumerator.Value, depth + 1, location + "[" + entries.Count + "]");
                entries.Add(new(keyNode, node));
            }
            if (values.Count > entries.Count) entries.Add(new(new PlaceholderNode(PlaceholderReason.MoreItemsMayExist), Limit(depth + 1)));
        }
        finally { (enumerator as IDisposable)?.Dispose(); }
        return new(TypeDisplayName.Of(type), entries.ToImmutable());
    }
    private ObjectNode ExceptionNode(Exception ex, int depth, string location)
    {
        var members = ImmutableArray.CreateBuilder<DisplayMember>();
        if (options.ExceptionDetail != ExceptionDetailLevel.TypeOnly)
        {
            members.Add(new("Message", CanVisit(depth + 1) ? Visit(ex.Message, depth + 1, location + ".Message") : Limit(depth + 1)));
            if (options.ExceptionDetail == ExceptionDetailLevel.Full)
            {
                if (options.IncludeStackTrace) members.Add(new("StackTrace", CanVisit(depth + 1) ? Visit(ex.StackTrace, depth + 1, location + ".StackTrace") : Limit(depth + 1)));
                if (CanVisit(depth + 1)) members.Add(new("InnerException", Visit(ex.InnerException, depth + 1, location + ".InnerException")));
            }
        }
        return new(ex.GetType().Name, members.ToImmutable());
    }
    private TableNode Table(DataTable table, int depth, string location)
    {
        var columns = table.Columns.Cast<DataColumn>().Take(options.MaxObjectMembers).ToArray();
        var rows = ImmutableArray.CreateBuilder<ImmutableArray<DisplayNode>>();
        DisplayNode? notice = null;
        foreach (DataRow row in table.Rows)
        {
            if (rows.Count >= options.MaxCollectionItems) break;
            if (!CanVisit(depth + 1)) { notice = Limit(depth + 1); break; }
            var cells = ImmutableArray.CreateBuilder<DisplayNode>();
            foreach (var column in columns)
            {
                if (!CanVisit(depth + 1)) { notice = Limit(depth + 1); cells.Add(notice); break; }
                if (options.SensitiveData?.IsSensitive(column.ColumnName) == true) { count++; cells.Add(new PlaceholderNode(PlaceholderReason.Redacted)); }
                else cells.Add(Visit(row[column] is DBNull ? null : row[column], depth + 1, location + "." + column.ColumnName));
            }
            rows.Add(cells.ToImmutable());
            if (notice is not null) break;
        }
        return new(table.TableName, columns.Select(c => new TableColumn(c.ColumnName, c.DataType.Name)).ToImmutableArray(), rows.ToImmutable(),
            table.Rows.Count, table.Rows.Count - rows.Count, table.Columns.Count - columns.Length, notice);
    }
}
