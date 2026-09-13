using System.Data;
using Microsoft.Data.Sqlite;
using Zamay.Core;
using Zamay;
using Zamay.Presentation;
using Xunit;

namespace Zamay.Sqlite.Tests;
public sealed class SqliteTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "Zamay-" + Guid.NewGuid() + ".db");
    private readonly SqliteConnection connection;
    public SqliteTests()
    {
        connection = new("Data Source=" + path + ";Pooling=False"); connection.Open();
        Execute("""
        CREATE TABLE Users(Id INTEGER PRIMARY KEY, Name TEXT, Password TEXT);
        CREATE TABLE Logs(Message TEXT);
        CREATE TABLE TableWithBlob(Id INTEGER PRIMARY KEY, Payload BLOB, LargeText TEXT, Nullable TEXT);
        CREATE TABLE "weird""name"("quoted""column" TEXT);
        CREATE TABLE Empty(Id INTEGER PRIMARY KEY);
        CREATE TABLE NoRowId(Key TEXT PRIMARY KEY, Value TEXT) WITHOUT ROWID;
        CREATE TABLE DescPk(Id INTEGER PRIMARY KEY DESC, Value TEXT);
        CREATE TABLE Shadowed(rowid TEXT, _rowid_ TEXT, oid TEXT);
        CREATE VIEW UserNames AS SELECT Name FROM Users;
        CREATE INDEX IX_Users_Name ON Users(Name);
        INSERT INTO "weird""name" VALUES('safe');
        INSERT INTO NoRowId VALUES('key','value');
        INSERT INTO Logs VALUES('first'),('second');
        """);
        using var transaction = connection.BeginTransaction();
        for (int i = 1; i <= 123; i++)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO Users VALUES(@id,@name,'hidden')";
            command.Parameters.AddWithValue("@id", i); command.Parameters.AddWithValue("@name", "User " + i); command.ExecuteNonQuery();
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = "INSERT INTO TableWithBlob VALUES(1,@blob,@text,NULL)";
            command.Parameters.AddWithValue("@blob", new byte[100000]); command.Parameters.AddWithValue("@text", new string('Ж', 10000)); command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    private void Execute(string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private async Task<SqliteInspectorSession> Session(SqliteInspectionOptions? options = null) { var s = new SqliteInspectorSession(connection, options); await s.LoadSchemaAsync(); return s; }
    [Fact] public async Task ClosedConnectionNotOpened() { using var closed = new SqliteConnection("Data Source=:memory:"); await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteInspectorSession(closed).LoadSchemaAsync()); Assert.Equal(ConnectionState.Closed, closed.State); }
    [Fact] public async Task ConnectionRemainsOpen() { var s = await Session(); await s.ReadPageAsync("Users"); Assert.Equal(ConnectionState.Open, connection.State); Execute("SELECT 1"); }
    [Fact] public void ConnectionDescriptorNeverLeaksConnectionString() { var text = connection.ToDisplayString(new() { SensitiveData = null }); Assert.DoesNotContain(path, text); Assert.DoesNotContain("Data Source", text); }
    [Fact]
    public async Task SchemaIncludesColumnsPkViewsIndexes()
    { var snapshot = await new SqliteInspectorSession(connection).LoadSchemaAsync(); var users = Assert.Single(snapshot.Objects, t => t.Name == "Users"); Assert.Equal(1, users.Columns[0].PrimaryKeyOrdinal); Assert.Contains(users.Indexes, i => i.Name == "IX_Users_Name"); Assert.Contains(snapshot.Objects, t => t.Kind == "view"); Assert.DoesNotContain(snapshot.Objects, t => t.Name.StartsWith("sqlite_")); }
    [Fact]
    public async Task CountIsOnDemandAndCached()
    { var s = await Session(); Assert.Null(s.GetCachedCount("Users")); Assert.Equal("Schema", s.LastOperation!.Operation); Assert.Equal(123, await s.CountAsync("Users")); Execute("INSERT INTO Users VALUES(200,'extra','hidden')"); Assert.Equal(123, await s.CountAsync("Users")); Assert.Equal(124, await s.CountAsync("Users", true)); }
    [Fact] public async Task AutomaticCountOptIn() { var s = await Session(new() { AutomaticCounts = true }); Assert.Equal(123, s.GetCachedCount("Users")); }
    [Fact] public async Task LatestUsesIntegerPrimaryKey() { var s = await Session(); var page = await s.ReadPageAsync("Users", new() { Latest = true }); Assert.Equal("123", Assert.IsType<ScalarNode>(page.Table.Rows[0][0]).Text); Assert.Equal("Id DESC", page.Diagnostics.Ordering); Assert.Equal(50, page.Table.Rows.Length); Assert.True(page.HasMore); }
    [Fact] public async Task RowIdFallback() { var s = await Session(); var page = await s.ReadPageAsync("Logs", new() { Latest = true }); Assert.Equal("second", Assert.IsType<ScalarNode>(page.Table.Rows[0][0]).Text); Assert.Equal("rowid DESC", page.Diagnostics.Ordering); }
    [Theory]
    [InlineData("NoRowId")]
    [InlineData("UserNames")]
    [InlineData("Shadowed")]
    public async Task NoFalseLatest(string name) { var s = await Session(); await Assert.ThrowsAsync<InvalidOperationException>(() => s.ReadPageAsync(name, new() { Latest = true })); }
    [Fact] public async Task IntegerPkDescIsNotRowIdAlias() { var s = new SqliteInspectorSession(connection); var snapshot = await s.LoadSchemaAsync(); Assert.Equal("rowid", snapshot.Objects.Single(t => t.Name == "DescPk").SuggestedOrder); }
    [Fact] public async Task ExplicitOrderWorksWithoutRowId() { var s = await Session(); var p = await s.ReadPageAsync("NoRowId", new() { Latest = true, OrderColumn = "Key" }); Assert.Equal("Key DESC", p.Diagnostics.Ordering); }
    [Fact] public async Task PagingAndProjection() { var s = await Session(new() { PageSize = 10 }); var page = await s.ReadPageAsync("Users", new() { Columns = ["Id"], OrderColumn = "Id", PageIndex = 1 }); Assert.Single(page.Table.Columns); Assert.Equal("11", Assert.IsType<ScalarNode>(page.Table.Rows[0][0]).Text); Assert.Equal(2, page.Table.OmittedColumns); }
    [Fact] public async Task QuotedIdentifiersExecute() { var s = await Session(); var p = await s.ReadPageAsync("weird\"name", new() { Columns = ["quoted\"column"], Filter = new("quoted\"column", FilterOperator.Equal, "safe") }); Assert.Equal("safe", Assert.IsType<ScalarNode>(p.Table.Rows[0][0]).Text); }
    [Fact] public async Task InjectionValueIsParameterized() { var s = await Session(); var p = await s.ReadPageAsync("Users", new() { Filter = new("Name", FilterOperator.Equal, "' OR 1=1 --") }); Assert.Empty(p.Table.Rows); Assert.Equal(123, await s.CountAsync("Users")); }
    [Fact] public async Task UnknownIdentifierRejected() { var s = await Session(); await Assert.ThrowsAsync<ArgumentException>(() => s.ReadPageAsync("Users; DROP TABLE Users")); await Assert.ThrowsAsync<ArgumentException>(() => s.ReadPageAsync("Users", new() { Columns = ["Id; DROP TABLE Users"] })); }
    [Fact] public async Task ContainsEscapesWildcards() { Execute("INSERT INTO Logs VALUES('100%_done')"); var s = await Session(); var p = await s.ReadPageAsync("Logs", new() { Filter = new("Message", FilterOperator.Contains, "%_") }); Assert.Single(p.Table.Rows); }
    [Fact] public async Task PreviewBoundsBlobAndText() { var s = await Session(new() { BlobPreviewLength = 12, TextPreviewLength = 17 }); var p = await s.ReadPageAsync("TableWithBlob"); var b = Assert.IsType<ScalarNode>(p.Table.Rows[0][1]); var t = Assert.IsType<ScalarNode>(p.Table.Rows[0][2]); Assert.Equal(24, b.Text.Length); Assert.Contains("100000", b.TypeName); Assert.True(b.Truncated); Assert.Equal(17, t.Text.Length); Assert.True(t.Truncated); Assert.Equal("null", Assert.IsType<ScalarNode>(p.Table.Rows[0][3]).Text); }
    [Fact] public async Task ExpandedPreviewExplicit() { var s = await Session(new() { TextPreviewLength = 10 }); var p = await s.ReadExpandedPageAsync("TableWithBlob", new(), 20, 128); Assert.Equal(20, Assert.IsType<ScalarNode>(p.Table.Rows[0][2]).Text.Length); Assert.Equal(10, s.Options.TextPreviewLength); }
    [Fact] public async Task EmptyAndNullFilter() { var s = await Session(); Assert.Empty((await s.ReadPageAsync("Empty")).Table.Rows); Assert.Single((await s.ReadPageAsync("TableWithBlob", new() { Filter = new("Nullable", FilterOperator.IsNull) })).Table.Rows); }
    [Fact] public async Task SensitiveColumnNotMaterialized() { var s = await Session(); var p = await s.ReadPageAsync("Users"); Assert.IsType<PlaceholderNode>(p.Table.Rows[0][2]); Assert.DoesNotContain("hidden", new TextRenderer().RenderToString(new(p.Table))); }
    [Fact] public async Task CancellationOnAllOperations() { var s = await Session(); using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.LoadSchemaAsync(cts.Token)); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.CountAsync("Users", cancellationToken: cts.Token)); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.ReadPageAsync("Users", cancellationToken: cts.Token)); }
    [Fact] public async Task ReadOnlyConnectionSupported() { using var ro = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly;Pooling=False"); ro.Open(); var s = new SqliteInspectorSession(ro); await s.LoadSchemaAsync(); Assert.Equal(123, await s.CountAsync("Users")); Assert.Equal(ConnectionState.Open, ro.State); }
    [Fact] public async Task CallerTransactionUnchanged() { using var tx = connection.BeginTransaction(); var s = new SqliteInspectorSession(connection, transaction: tx); await s.LoadSchemaAsync(); await s.ReadPageAsync("Users"); Assert.Same(connection, tx.Connection); tx.Rollback(); }
    [Fact] public async Task ControllerNavigationAndFailureState() { var s = await Session(new() { PageSize = 10 }); var c = new SqliteBrowserController(s); await c.InitializeAsync(); c.Select("Users"); await c.BrowseAsync(["Id"], "Id", true); await c.NextAsync(); Assert.Equal(1, c.Request.PageIndex); Assert.Equal("113", Assert.IsType<ScalarNode>(c.Page!.Table.Rows[0][0]).Text); await c.PreviousAsync(); Assert.Equal(0, c.Request.PageIndex); await Assert.ThrowsAsync<ArgumentException>(() => c.LoadAsync(new() { Columns = ["bad"] })); Assert.True(c.Request.Latest); c.Select("Logs"); Assert.Null(c.Page); Assert.Equal(0, c.Request.PageIndex); }
    [Fact] public void QuoteEscapesDoubleQuote() => Assert.Equal("\"a\"\"b\"", SqliteQueryBuilder.QuoteIdentifier("a\"b"));
    [Fact] public void InvalidOptionsThrow() => Assert.Throws<ArgumentOutOfRangeException>(() => new SqliteInspectorSession(connection, new() { PageSize = 0 }));
    [Fact] public async Task SqlHasExplicitProjectionAndParameters() { var s = new SqliteInspectorSession(connection); var snapshot = await s.LoadSchemaAsync(); var query = SqliteQueryBuilder.BuildPage(snapshot.Objects.Single(t => t.Name == "Users"), new() { Columns = ["Id"], Filter = new("Name", FilterOperator.Equal, "sensitive-value") }, new()); Assert.DoesNotContain("SELECT *", query.Text); Assert.DoesNotContain("sensitive-value", query.Text); Assert.Contains(query.Parameters, p => p.Key == "@value" && Equals(p.Value, "sensitive-value")); }
    [Fact]
    public async Task MainSchemaCannotBeShadowedByTemp()
    {
        Execute("CREATE TEMP TABLE Users(Fake TEXT); INSERT INTO temp.Users VALUES('wrong')");
        var session = await Session();
        Assert.Equal(123, await session.CountAsync("Users"));
        var page = await session.ReadPageAsync("Users", new() { Latest = true });
        Assert.Equal("123", Assert.IsType<ScalarNode>(page.Table.Rows[0][0]).Text);
    }
    [Fact]
    public async Task RecordReloadUsesHiddenPrimaryKeyAndAllColumns()
    {
        var session = await Session(new() { MaxColumns = 1 });
        var page = await session.ReadPageAsync("Users", new() { Columns = ["Name"], Latest = true });
        Assert.Single(page.Table.Columns);
        Execute("UPDATE Users SET Name='changed' WHERE Id=123");
        var record = await session.ReadRecordAsync(page, 0);
        Assert.Equal(3, record.Columns.Length);
        Assert.Equal("123", Assert.IsType<ScalarNode>(record.Rows[0][0]).Text);
        Assert.Equal("changed", Assert.IsType<ScalarNode>(record.Rows[0][1]).Text);
        Assert.IsType<PlaceholderNode>(record.Rows[0][2]);
    }
    [Fact]
    public async Task CompositePrimaryKeyLookupIsParameterized()
    {
        Execute("CREATE TABLE Composite(A TEXT, B INTEGER, Value TEXT, PRIMARY KEY(A,B)) WITHOUT ROWID; INSERT INTO Composite VALUES('key',2,'found')");
        var session = await Session();
        var record = await session.ReadRecordByPrimaryKeyAsync("Composite", new Dictionary<string, object?> { ["A"] = "key", ["B"] = 2 });
        Assert.Equal("found", Assert.IsType<ScalarNode>(record.Rows[0][2]).Text);
        await Assert.ThrowsAsync<ArgumentException>(() => session.ReadRecordByPrimaryKeyAsync("Composite", new Dictionary<string, object?> { ["A"] = "key" }));
    }
    [Fact]
    public async Task RecordHandlesAreSessionBound()
    {
        var a = await Session(); var b = await Session(); var page = await a.ReadPageAsync("Users");
        await Assert.ThrowsAsync<ArgumentException>(() => b.ReadRecordAsync(page, 0));
    }
    [Fact]
    public async Task TruncatedKeyCannotReloadWrongRecord()
    {
        Execute("CREATE TABLE TextKey(Id TEXT PRIMARY KEY, Value TEXT); INSERT INTO TextKey VALUES('very-long-key','value')");
        var session = await Session(new() { TextPreviewLength = 3 }); var page = await session.ReadPageAsync("TextKey");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadRecordAsync(page, 0));
    }
    [Fact]
    public async Task DeletedRecordReported()
    {
        var session = await Session(); var page = await session.ReadPageAsync("Users", new() { Latest = true });
        Execute("DELETE FROM Users WHERE Id=123"); Assert.Empty((await session.ReadRecordAsync(page, 0)).Rows);
    }
    [Fact]
    public async Task SchemaLimitIsExplicit()
    { var snapshot = await new SqliteInspectorSession(connection, new() { MaxSchemaObjects = 2 }).LoadSchemaAsync(); Assert.Equal(2, snapshot.Objects.Length); Assert.True(snapshot.Truncated); }
    public void Dispose() { connection.Dispose(); File.Delete(path); }
}
