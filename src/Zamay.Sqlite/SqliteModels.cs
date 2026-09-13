using System.Collections.Immutable;
using Zamay.Core;

namespace Zamay.Sqlite;

public sealed record SqliteInspectionOptions
{
    public int PageSize { get; init; } = 50;
    public int MaxColumns { get; init; } = 64;
    public int MaxSchemaObjects { get; init; } = 500;
    public int TextPreviewLength { get; init; } = 2000;
    public int BlobPreviewLength { get; init; } = 64;
    public int CommandTimeoutSeconds { get; init; } = 10;
    public bool IncludeSystemObjects { get; init; }
    public bool AutomaticCounts { get; init; }
    public ISensitiveDataPolicy? SensitiveData { get; init; } = SensitiveDataPolicy.Default;
    public void Validate()
    {
        foreach (var (name, value) in new[] { (nameof(PageSize),PageSize),(nameof(MaxColumns),MaxColumns),(nameof(MaxSchemaObjects),MaxSchemaObjects),
            (nameof(TextPreviewLength),TextPreviewLength),(nameof(BlobPreviewLength),BlobPreviewLength),(nameof(CommandTimeoutSeconds),CommandTimeoutSeconds) })
            if (value <= 0) throw new ArgumentOutOfRangeException(name);
    }
}
public sealed record SqliteColumn(string Name, string DeclaredType, bool NotNull, int PrimaryKeyOrdinal, int Hidden);
public sealed record SqliteIndex(string Name, bool Unique, string Origin);
public sealed record SqliteObject(string Name, string Kind, string Definition, ImmutableArray<SqliteColumn> Columns,
    ImmutableArray<SqliteIndex> Indexes, bool WithoutRowId, string? SuggestedOrder, bool SchemaTruncated = false);
public sealed record SchemaSnapshot(ImmutableArray<SqliteObject> Objects, bool Truncated);
public enum FilterOperator { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual, Contains, IsNull, IsNotNull }
public sealed record SqliteFilter(string Column, FilterOperator Operator, object? Value = null);
public sealed record PageRequest
{
    public int PageIndex { get; init; }
    public ImmutableArray<string> Columns { get; init; } = [];
    public bool Latest { get; init; }
    public string? OrderColumn { get; init; }
    public bool Descending { get; init; }
    public SqliteFilter? Filter { get; init; }
    public ImmutableArray<SqliteFilter> AdditionalFilters { get; init; } = [];
}
public sealed record QueryDiagnostics(string Operation, TimeSpan Elapsed, int ReturnedRows, bool Truncated, string? Ordering, int PageSize);
public sealed record PageResult(TableNode Table, QueryDiagnostics Diagnostics, bool HasMore)
{
    internal SqliteInspectorSession? Owner { get; init; }
    internal string SourceName { get; init; } = string.Empty;
    internal ImmutableArray<ImmutableDictionary<string, object?>?> RecordKeys { get; init; } = [];
}
public sealed record SqlQuery(string Text, ImmutableArray<KeyValuePair<string, object?>> Parameters, ImmutableArray<SqliteColumn> Columns, string? Ordering, int VisibleColumnCount);
