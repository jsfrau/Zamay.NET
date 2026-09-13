using Microsoft.Data.Sqlite;
using Zamay.Sqlite;
using Zamay.Windows;
using Zamay.Windows.Sqlite;

namespace Zamay;
public static class SqliteWindowsExtensions
{
    /// <summary>Opens a lazy browser for a caller-owned, already-open Microsoft.Data.Sqlite connection.</summary>
    public static void ToMessageBox(this SqliteConnection connection, Control? owner = null, SqliteInspectionOptions? options = null,
        SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var session = new SqliteInspectorSession(connection, options, transaction);
        DialogDispatcher.Show(() => new SqliteBrowserDialog(session), owner, cancellationToken);
    }
    public static Task ToMessageBoxAsync(this SqliteConnection connection, Control? owner = null, SqliteInspectionOptions? options = null,
        SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        var session = new SqliteInspectorSession(connection, options, transaction);
        return DialogDispatcher.ShowAsync(() => new SqliteBrowserDialog(session), owner, cancellationToken);
    }
}
