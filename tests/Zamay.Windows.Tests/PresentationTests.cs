using System.Collections.Immutable;
using Zamay.Core;
using Zamay.Presentation;
using Zamay.Windows;
using Xunit;

namespace Zamay.Windows.Tests;
public sealed class PresentationTests
{
    [Fact] public void ChoosesText() => Assert.Equal(DisplayViewKind.Text, DocumentPresentation.SelectView(new ScalarNode("42")));
    [Fact] public void ChoosesTree() => Assert.Equal(DisplayViewKind.Tree, DocumentPresentation.SelectView(new ObjectNode("O", [])));
    [Fact] public void ChoosesTable() => Assert.Equal(DisplayViewKind.Table, DocumentPresentation.SelectView(new TableNode("T", [], [])));
    [Fact]
    public void MapsAndSearchesMaterializedDocument()
    { var node = new ObjectNode("Person", [new("Name", new ScalarNode("Алексей")), new("Child", new ObjectNode("Child", [new("Age", new ScalarNode("5"))]))]); var tree = DocumentPresentation.Map(node); Assert.Equal(2, tree.Children.Length); Assert.Single(DocumentPresentation.Search(tree, "Алексей")); Assert.Single(DocumentPresentation.Search(tree, "Age")); }
    [Fact]
    public void RecordDetailsContainsColumnTypeValue()
    { var table = new TableNode("T", [new("Id", "INTEGER")], [ImmutableArray.Create<DisplayNode>(new ScalarNode("42", "integer"))]); var details = DocumentPresentation.RecordDetails(table, 0); Assert.Equal(3, details.Columns.Length); Assert.Equal("Id", Assert.IsType<ScalarNode>(details.Rows[0][0]).Text); Assert.Equal("42", Assert.IsType<ScalarNode>(details.Rows[0][2]).Text); }
    [Fact]
    public async Task StaControlsSmoke()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new ZamayDialog(new(new ObjectNode("Sample", [new("Text", new ScalarNode("Привет"))])));
                dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-20000, -20000); dialog.ShowInTaskbar = false; dialog.Show(); Application.DoEvents();
                using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(AppContext.BaseDirectory, "ui-smoke.png")); dialog.Close();
                Assert.Equal(DisplayViewKind.Tree, dialog.ViewKind); Assert.True(dialog.MinimumSize.Width >= 600);
                using var grid = ZamayDialog.TableView(new("T", [new("Id", "int")], [ImmutableArray.Create<DisplayNode>(new ScalarNode("1"))]));
                grid.CreateControl(); Assert.Single(grid.Rows.Cast<DataGridViewRow>()); completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); thread.Join();
    }
    [Fact] public async Task DispatcherPropagatesFactoryException() => await Assert.ThrowsAsync<InvalidOperationException>(() => DialogDispatcher.ShowAsync(() => throw new InvalidOperationException("UI failure")));
    [Fact]
    public async Task DispatcherPreCancellationDoesNotCreateForm()
    { using var cts = new CancellationTokenSource(); cts.Cancel(); var called = false; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DialogDispatcher.ShowAsync(() => { called = true; return new Form(); }, cancellationToken: cts.Token)); Assert.False(called); }
    [Fact]
    public async Task InvalidOwnerRejected()
    { await Task.Run(async () => { using var owner = new Control(); await Assert.ThrowsAsync<ArgumentException>(() => DialogDispatcher.ShowAsync(() => new Form(), owner)); }); }
    [Fact]
    public async Task SqliteBrowserStaSmokeLoadsSchemaAndRows()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"); connection.Open();
                using (var command = connection.CreateCommand()) { command.CommandText = "CREATE TABLE Users(Id INTEGER PRIMARY KEY,Name TEXT); INSERT INTO Users VALUES(1,'Алексей')"; command.ExecuteNonQuery(); }
                using var dialog = new Zamay.Windows.Sqlite.SqliteBrowserDialog(new(connection));
                dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-20000, -20000); dialog.ShowInTaskbar = false; dialog.Show();
                static IEnumerable<Control> Descendants(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
                void PumpUntil(Func<bool> condition)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(5); }
                    Assert.True(condition(), "UI operation did not complete: " + string.Join(" | ",Descendants(dialog).OfType<Label>().Select(l=>l.Text)));
                }
                var tree = Descendants(dialog).OfType<TreeView>().Single();
                PumpUntil(() => tree.Enabled && tree.Nodes.Count > 0 && tree.Nodes[0].Nodes.Count == 1);
                tree.SelectedNode = tree.Nodes[0].Nodes[0];
                var latest = Descendants(dialog).OfType<Button>().Single(b => b.Text == "Latest"); latest.PerformClick();
                PumpUntil(() => latest.Enabled && Descendants(dialog).OfType<DataGridView>().Any());
                var grid = Descendants(dialog).OfType<DataGridView>().Single(); Assert.Equal("1", grid.Rows[0].Cells[0].Value);
                using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(AppContext.BaseDirectory, "sqlite-ui-smoke.png"));
                dialog.Close(); Assert.Equal(System.Data.ConnectionState.Open, connection.State); completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await completion.Task.WaitAsync(TimeSpan.FromSeconds(30)); thread.Join();
    }
}
