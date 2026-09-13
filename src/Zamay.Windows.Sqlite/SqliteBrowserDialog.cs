using TextRenderer = Zamay.Core.TextRenderer;
using System.Collections.Immutable;
using Zamay.Core;
using Zamay.Presentation;
using Zamay.Sqlite;
using Zamay.Windows;

namespace Zamay.Windows.Sqlite;

/// <summary>Lazy database browser. Schema loads on opening; row data and exact counts load only on explicit commands.</summary>
public sealed class SqliteBrowserDialog : Form
{
    private readonly SqliteBrowserController controller;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private bool busy;
    private readonly TreeView objects = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly CheckedListBox columns = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly ComboBox order = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Panel data = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 60, AutoEllipsis = true };
    private readonly TextBox schema = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly TextBox filterValue = new() { Width = 100 };
    private readonly ComboBox filterColumn = new() { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox filterOperator = new() { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
    public SqliteBrowserDialog(SqliteInspectorSession session)
    {
        controller = new(session);
        Text = "Zamay — SQLite"; Size = new(1200, 800); MinimumSize = new(850, 550); AutoScaleMode = AutoScaleMode.Dpi;
        var split = new SplitContainer { Dock = DockStyle.Fill, Width = 1100, SplitterDistance = 260 };
        var left = new TabControl { Dock = DockStyle.Fill }; var dbTab = new TabPage("Database"); var colTab = new TabPage("Columns");
        dbTab.Controls.Add(objects); colTab.Controls.Add(columns); left.TabPages.AddRange([dbTab, colTab]); split.Panel1.Controls.Add(left);
        var right = new TabControl { Dock = DockStyle.Fill }; var rowsTab = new TabPage("Rows"); var schemaTab = new TabPage("Schema");
        rowsTab.Controls.Add(data); schemaTab.Controls.Add(schema); right.TabPages.AddRange([rowsTab, schemaTab]); split.Panel2.Controls.Add(right);
        AddAction("Refresh schema", async token => { await controller.InitializeAsync(token); PopulateObjects(); });
        AddAction("Count (exact)", async token => { var n = await controller.CountAsync(true, token); SetStatus($"{controller.Selected!.Name}: {n} rows (exact; cached until refresh)"); });
        AddAction("Browse", token => Browse(false, token)); AddAction("Latest", token => Browse(true, token));
        AddAction("Previous", async token => { await controller.PreviousAsync(token); ShowPage(); });
        AddAction("Next", async token => { await controller.NextAsync(token); ShowPage(); });
        AddAction("Record details", ShowRecordAsync);
        AddAction("More preview ×2", async token =>
        {
            if (controller.Selected is null) return;
            var extended = session.Options with { TextPreviewLength = checked(session.Options.TextPreviewLength * 2), BlobPreviewLength = checked(session.Options.BlobPreviewLength * 2) };
            // Reuse ownership and connection through a caller-independent preview operation on the same session.
            var result = await session.ReadExpandedPageAsync(controller.Selected.Name, controller.Request, extended.TextPreviewLength, extended.BlobPreviewLength, token);
            OpenDocument(new(result.Table));
        });
        var cancel = new Button { Text = "Cancel" }; cancel.Click += (_, _) => operation?.Cancel();
        var cancellationBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true }; cancellationBar.Controls.Add(cancel);
        actions.Controls.Add(new Label { Text = "Order", AutoSize = true }); actions.Controls.Add(order);
        filterOperator.Items.AddRange(Enum.GetNames<FilterOperator>()); filterOperator.SelectedIndex = 0;
        actions.Controls.AddRange([new Label { Text = "Filter", AutoSize = true }, filterColumn, filterOperator, filterValue]);
        Controls.Add(split); Controls.Add(actions); Controls.Add(status); Controls.Add(cancellationBar);
        objects.AfterSelect += (_, e) =>
        {
            if (busy || e.Node?.Tag is not SqliteObject table) return;
            controller.Select(table.Name); columns.Items.Clear(); order.Items.Clear(); order.Items.Add("(none)"); filterColumn.Items.Clear(); filterColumn.Items.Add("(none)");
            foreach (var column in table.Columns.Where(c => c.Hidden != 1)) { columns.Items.Add(column.Name, columns.Items.Count < session.Options.MaxColumns); order.Items.Add(column.Name); filterColumn.Items.Add(column.Name); }
            if (table.SuggestedOrder is { } suggested && !order.Items.Contains(suggested)) order.Items.Add(suggested);
            order.SelectedItem = table.SuggestedOrder ?? "(none)"; filterColumn.SelectedIndex = 0;
            schema.Text = DisplayText.Escape(table.Name) + "\r\n" + table.Definition + "\r\n\r\n" + string.Join("\r\n", table.Columns.Select(c => $"{c.Name}    {c.DeclaredType}    PK={c.PrimaryKeyOrdinal}    NOT NULL={c.NotNull}"))
                + "\r\nIndexes:\r\n" + string.Join("\r\n", table.Indexes.Select(i => $"{i.Name}: unique={i.Unique}, origin={i.Origin}"));
            status.Text = $"Count: {session.GetCachedCount(table.Name)?.ToString() ?? "not loaded"}. Select columns, then Browse or Latest. Exact count may be expensive.";
            foreach (Control control in data.Controls.Cast<Control>().ToArray()) control.Dispose();
        };
        Shown += async (_, _) => await Perform(async token => { await controller.InitializeAsync(token); PopulateObjects(); });
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; lifetime.Cancel(); operation?.Cancel(); } };
        FormClosed += (_, _) => lifetime.Cancel();
    }
    private void AddAction(string text, Func<CancellationToken, Task> action)
    { var button = new Button { Text = text, AutoSize = true }; button.Click += async (_, _) => await Perform(action); actions.Controls.Add(button); }
    private async Task Perform(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        busy = true; actions.Enabled = false; objects.Enabled = false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); operation = cts;
        try { await action(cts.Token); }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { SetStatus("Cancelled."); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException and not StackOverflowException)
        { SetStatus(new TextRenderer().RenderToString(new(new ErrorNode(ex.GetType().Name, controller.Session.Options.SensitiveData?.Sanitize(ex.Message) ?? ex.Message)), 2000)); }
        finally { OnUi(() => { operation = null; busy = false; actions.Enabled = true; objects.Enabled = true; if (lifetime.IsCancellationRequested) Close(); }); }
    }
    private void PopulateObjects()
    {
        if (InvokeRequired) { Invoke(PopulateObjects); return; }
        objects.Nodes.Clear(); var tables = objects.Nodes.Add("Tables"); var views = objects.Nodes.Add("Views");
        foreach (var obj in controller.Schema!.Objects) (obj.Kind == "view" ? views : tables).Nodes.Add(new TreeNode(DisplayText.Escape(obj.Name)) { Tag = obj });
        objects.ExpandAll(); status.Text = controller.Schema.Truncated ? "Schema truncated by MaxSchemaObjects." : "Select a table or view. Counts and data are not loaded.";
    }
    private async Task Browse(bool latest, CancellationToken token)
    {
        var selectedColumns = columns.CheckedItems.Cast<string>().ToImmutableArray();
        if (selectedColumns.Length == 0) throw new InvalidOperationException("Select at least one column.");
        var orderName = order.SelectedIndex <= 0 ? null : (string?)order.SelectedItem;
        SqliteFilter? filter = filterColumn.SelectedIndex <= 0 ? null : new((string)filterColumn.SelectedItem!, Enum.Parse<FilterOperator>((string)filterOperator.SelectedItem!), filterValue.Text);
        await controller.LoadAsync(new() { Columns = selectedColumns, Latest = latest, OrderColumn = orderName, Filter = filter }, token); ShowPage();
    }
    private void ShowPage()
    {
        if (InvokeRequired) { Invoke(ShowPage); return; }
        if (controller.Page is not { } page) return;
        foreach (Control control in data.Controls.Cast<Control>().ToArray()) control.Dispose();
        data.Controls.Add(ZamayDialog.TableView(page.Table));
        status.Text = $"Page {controller.Request.PageIndex + 1}; {page.Diagnostics.ReturnedRows} rows; {page.Diagnostics.Elapsed.TotalMilliseconds:F1} ms; "
            + (page.Diagnostics.Ordering is null ? "Unordered browse" : "Ordered by " + page.Diagnostics.Ordering)
            + $"; page size={page.Diagnostics.PageSize}; truncated={page.Diagnostics.Truncated}; count={controller.Session.GetCachedCount(controller.Selected!.Name)?.ToString() ?? "not loaded"}";
    }
    private async Task ShowRecordAsync(CancellationToken token)
    {
        var grid = data.Controls.OfType<DataGridView>().FirstOrDefault();
        if (grid?.CurrentRow is not { } row || controller.Page is null) return;
        TableNode table;
        try { table = await controller.Session.ReadRecordAsync(controller.Page, row.Index, token); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No complete", StringComparison.Ordinal))
        {
            SetStatus(ex.Message);
            OpenDocument(new(DocumentPresentation.RecordDetails(controller.Page.Table, row.Index))); return;
        }
        if (table.Rows.Length == 0) { SetStatus("Record no longer exists."); return; }
        OpenDocument(new(DocumentPresentation.RecordDetails(table, 0)));
    }
    private void OnUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) Invoke(action); else action();
    }
    private void SetStatus(string text) => OnUi(() => status.Text = text);
    private void OpenDocument(DisplayDocument document) => OnUi(() => { using var dialog = new ZamayDialog(document); dialog.ShowDialog(this); });
    protected override void Dispose(bool disposing) { if (disposing) lifetime.Dispose(); base.Dispose(disposing); }
}
