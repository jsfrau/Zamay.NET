using System.Collections.Immutable;
using Zamay.Sqlite;

namespace Zamay.Sqlite;

/// <summary>Single-consumer controller. Failed/cancelled page loads do not commit navigation state.</summary>
public sealed class SqliteBrowserController(SqliteInspectorSession session)
{
    public SchemaSnapshot? Schema { get; private set; }
    public SqliteObject? Selected { get; private set; }
    public PageRequest Request { get; private set; } = new();
    public PageResult? Page { get; private set; }
    public SqliteInspectorSession Session => session;
    public async Task InitializeAsync(CancellationToken token = default) => Schema = await session.LoadSchemaAsync(token).ConfigureAwait(false);
    public void Select(string name)
    {
        Selected = Schema?.Objects.FirstOrDefault(t => t.Name == name) ?? throw new ArgumentException("Unknown object.", nameof(name));
        Request = new(); Page = null;
    }
    public async Task LoadAsync(PageRequest request, CancellationToken token = default)
    {
        var selected = Selected ?? throw new InvalidOperationException("Select a table first.");
        var result = await session.ReadPageAsync(selected.Name, request, token).ConfigureAwait(false);
        Request = request; Page = result;
    }
    public Task BrowseAsync(ImmutableArray<string> columns, string? order = null, bool latest = false, CancellationToken token = default)
        => LoadAsync(new() { Columns = columns, OrderColumn = order, Latest = latest }, token);
    public Task NextAsync(CancellationToken token = default) => Page?.HasMore == true ? LoadAsync(Request with { PageIndex = checked(Request.PageIndex + 1) }, token) : Task.CompletedTask;
    public Task PreviousAsync(CancellationToken token = default) => Request.PageIndex > 0 ? LoadAsync(Request with { PageIndex = Request.PageIndex - 1 }, token) : Task.CompletedTask;
    public Task<long> CountAsync(bool refresh = false, CancellationToken token = default) => session.CountAsync(Selected?.Name ?? throw new InvalidOperationException("Select a table first."), refresh, token);
}
