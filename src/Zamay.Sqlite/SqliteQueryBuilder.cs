using System.Collections.Immutable;

namespace Zamay.Sqlite;

/// <summary>SQL is generated only from verified schema identifiers. Values are always parameters.</summary>
public static class SqliteQueryBuilder
{
    public static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        if (identifier.Contains('\0')) throw new ArgumentException("NUL in identifier.", nameof(identifier));
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
    public static SqlQuery BuildPage(SqliteObject table, PageRequest request, SqliteInspectionOptions options)
    {
        options.Validate();
        if (request.PageIndex < 0) throw new ArgumentOutOfRangeException(nameof(request.PageIndex));
        var available = table.Columns.Where(c => c.Hidden != 1).ToArray();
        var visible = request.Columns.IsDefaultOrEmpty ? available.Take(options.MaxColumns).ToImmutableArray()
            : request.Columns.Distinct(StringComparer.Ordinal).Select(n => available.FirstOrDefault(c => c.Name == n)
                ?? throw new ArgumentException("Unknown column: " + n)).ToImmutableArray();
        if (visible.Length == 0 || visible.Length > options.MaxColumns) throw new ArgumentOutOfRangeException(nameof(request.Columns));
        // Additional key previews are private row locators, never unbounded raw TEXT/BLOB values.
        var columns = visible.AddRange(available.Where(c => c.PrimaryKeyOrdinal > 0 && !visible.Contains(c)).Take(options.MaxColumns));
        var order = request.OrderColumn ?? (request.Latest ? table.SuggestedOrder : null);
        if (request.Latest && order is null) throw new InvalidOperationException("Cannot determine deterministic latest-row ordering. Choose an order column.");
        if (order is not null && !available.Any(c => c.Name == order) && order != table.SuggestedOrder) throw new ArgumentException("Unknown order column.");
        var parameters = ImmutableArray.CreateBuilder<KeyValuePair<string, object?>>();
        parameters.Add(new("@text", options.TextPreviewLength + 1L)); parameters.Add(new("@blob", options.BlobPreviewLength));
        parameters.Add(new("@take", options.PageSize + 1L)); parameters.Add(new("@skip", checked((long)request.PageIndex * options.PageSize)));
        var projection = new List<string>();
        foreach (var column in columns)
        {
            var q = QuoteIdentifier(column.Name);
            if (options.SensitiveData?.IsSensitive(column.Name) == true) { projection.Add("NULL, 'redacted', NULL"); continue; }
            projection.Add($"CASE typeof({q}) WHEN 'blob' THEN substr({q},1,@blob) WHEN 'text' THEN substr({q},1,@text) ELSE {q} END, typeof({q}), CASE WHEN typeof({q}) IN ('text','blob') THEN length({q}) ELSE NULL END");
        }
        var sql = "SELECT " + string.Join(", ", projection) + " FROM main." + QuoteIdentifier(table.Name);
        var filters = request.AdditionalFilters.IsDefault ? ImmutableArray<SqliteFilter>.Empty : request.AdditionalFilters;
        if (request.Filter is { } single) filters = filters.Insert(0, single);
        if (filters.Length > 2000) throw new ArgumentOutOfRangeException(nameof(request.AdditionalFilters));
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (!available.Any(c => c.Name == filter.Column)) throw new ArgumentException("Unknown filter column.");
            var parameter = i == 0 ? "@value" : "@value" + i;
            var op = filter.Operator switch
            {
                FilterOperator.Equal => "= " + parameter,
                FilterOperator.NotEqual => "!= " + parameter,
                FilterOperator.Less => "< " + parameter,
                FilterOperator.LessOrEqual => "<= " + parameter,
                FilterOperator.Greater => "> " + parameter,
                FilterOperator.GreaterOrEqual => ">= " + parameter,
                FilterOperator.Contains => "LIKE " + parameter + " ESCAPE '\\'",
                FilterOperator.IsNull => "IS NULL",
                FilterOperator.IsNotNull => "IS NOT NULL",
                _ => throw new ArgumentOutOfRangeException(nameof(filter.Operator))
            };
            sql += (i == 0 ? " WHERE " : " AND ") + QuoteIdentifier(filter.Column) + " " + op;
            if (filter.Operator is not FilterOperator.IsNull and not FilterOperator.IsNotNull)
            {
                object? value = filter.Value;
                if (filter.Operator == FilterOperator.Contains) value = "%" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!
                    .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                parameters.Add(new(parameter, value));
            }
        }
        string? ordering = null;
        if (order is not null)
        {
            var direction = request.Latest || request.Descending ? " DESC" : " ASC";
            var keys = available.Where(c => c.PrimaryKeyOrdinal > 0).OrderBy(c => c.PrimaryKeyOrdinal).Select(c => c.Name).Where(n => n != order).ToList();
            if (keys.Count == 0 && table.SuggestedOrder is { } fallback && fallback != order) keys.Add(fallback);
            var orderNames = new[] { order }.Concat(keys).ToArray();
            ordering = string.Join(", ", orderNames.Select(n => n + direction));
            sql += " ORDER BY " + string.Join(", ", orderNames.Select(n => QuoteIdentifier(n) + direction));
        }
        sql += " LIMIT @take OFFSET @skip";
        return new(sql, parameters.ToImmutable(), columns, ordering, visible.Length);
    }
}
