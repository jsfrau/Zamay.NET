using System.Data;
using Microsoft.Data.Sqlite;
using Zamay;

namespace Zamay.Demo.WinForms;
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
            CREATE TABLE Users(Id INTEGER PRIMARY KEY,Name TEXT,CreatedAt TEXT,Password TEXT);
            CREATE TABLE Logs(Id INTEGER PRIMARY KEY,Message TEXT);
            CREATE TABLE Files(Id INTEGER PRIMARY KEY,Content BLOB);
            INSERT INTO Users VALUES(1,'Алексей','2026-09-13','secret'),(2,'Мария','2026-09-13','secret');
            INSERT INTO Logs VALUES(1,'Приложение запущено'),(2,'Добавлен пользователь');
            INSERT INTO Files VALUES(1,zeroblob(100000));
            CREATE VIEW Names AS SELECT Name FROM Users;
            """;
            command.ExecuteNonQuery();
        }
        using var form = new Form { Text = "Zamay demo", Size = new(550, 220), AutoScaleMode = AutoScaleMode.Dpi };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(20) };
        var objectButton = new Button { Text = "Object tree", AutoSize = true };
        objectButton.Click += (_, _) => new { Id = 42, Name = "Алексей", Password = "secret", Orders = new[] { new { Id = 1, Total = 12.5m } } }.ToMessageBox(form);
        var tableButton = new Button { Text = "Table", AutoSize = true };
        tableButton.Click += (_, _) => { var table = new DataTable("Users"); table.Columns.Add("Id", typeof(int)); table.Columns.Add("Name"); table.Rows.Add(42, "Алексей"); table.ToMessageBox(form); };
        var dbButton = new Button { Text = "SQLite browser", AutoSize = true }; dbButton.Click += (_, _) => connection.ToMessageBox(form);
        buttons.Controls.AddRange([objectButton, tableButton, dbButton]); form.Controls.Add(buttons); Application.Run(form);
    }
}
