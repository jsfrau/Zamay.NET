using TextRenderer = Zamay.Core.TextRenderer;
using Zamay.Core;
using Zamay.Presentation;

namespace Zamay.Windows;

/// <summary>Resizable modal document viewer. Must be constructed on an STA thread.</summary>
public sealed class ZamayDialog : Form
{
    private readonly TextBox search = new() { Width = 220, PlaceholderText = "Search; Enter = next" };
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly Panel detail = new() { Dock = DockStyle.Fill };
    private readonly string text;
    private int searchIndex;
    public DisplayViewKind ViewKind { get; }
    public ZamayDialog(DisplayDocument document, int maxTextCharacters = 100000, CancellationToken cancellationToken = default)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("STA thread required.");
        Text = "Zamay"; Size = new(1000, 700); MinimumSize = new(600, 400);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        text = new TextRenderer().RenderToString(document, maxTextCharacters, cancellationToken);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var main = new TabPage("Inspector"); var textTab = new TabPage("Text");
        tabs.TabPages.AddRange([main, textTab]); textTab.Controls.Add(TextView(text));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new(4) };
        var copy = new Button { Text = "Copy" }; copy.Click += (_, _) => { try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException ex) { ShowError(ex); } };
        var expand = new Button { Text = "Expand all", AutoSize = true }; expand.Click += (_, _) => tree.ExpandAll();
        toolbar.Controls.AddRange([copy, search, expand]); Controls.Add(tabs); Controls.Add(toolbar);
        ViewKind = DocumentPresentation.SelectView(document.Root);
        if (ViewKind == DisplayViewKind.Tree)
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Width = 900, SplitterDistance = 400 };
            split.Panel1.Controls.Add(tree); split.Panel2.Controls.Add(detail); main.Controls.Add(split);
            tree.Nodes.Add(ToTree(DocumentPresentation.Map(document.Root))); tree.Nodes[0].Expand();
            tree.AfterSelect += (_, e) => { if (e.Node?.Tag is DisplayNode node) ShowDetail(node); };
            tree.SelectedNode = tree.Nodes[0];
        }
        else main.Controls.Add(document.Root is TableNode table ? TableView(table) : TextView(text));
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (ViewKind != DisplayViewKind.Tree)
            {
                var box = textTab.Controls.OfType<TextBox>().First(); var index = box.Text.IndexOf(search.Text, Math.Min(searchIndex, box.Text.Length), StringComparison.OrdinalIgnoreCase);
                if (index < 0) { searchIndex = 0; return; }
                tabs.SelectedTab = textTab; box.Select(index, search.Text.Length); box.ScrollToCaret(); searchIndex = index + Math.Max(1, search.Text.Length); return;
            }
            var matches = All(tree.Nodes).Where(n => (n.Text + " " + DocumentPresentation.Summary((DisplayNode)n.Tag!)).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 0) { tree.SelectedNode = matches[searchIndex++ % matches.Length]; tree.SelectedNode.EnsureVisible(); }
        };
    }
    private static IEnumerable<TreeNode> All(TreeNodeCollection nodes) { foreach (TreeNode node in nodes) { yield return node; foreach (var child in All(node.Nodes)) yield return child; } }
    private static TreeNode ToTree(DisplayTreeItem item) => new(item.Label + ": " + DocumentPresentation.Summary(item.Node), item.Children.Select(ToTree).ToArray()) { Tag = item.Node };
    private void ShowDetail(DisplayNode node)
    {
        foreach (Control c in detail.Controls.Cast<Control>().ToArray()) c.Dispose();
        detail.Controls.Add(node is TableNode t ? TableView(t) : TextView(new TextRenderer().RenderToString(new(node))));
    }
    private void ShowError(Exception ex) { using var dialog = new ZamayDialog(new(new ErrorNode(ex.GetType().Name, ex.Message))); dialog.ShowDialog(this); }
    public static TextBox TextView(string text) => new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = text.ReplaceLineEndings("\r\n"), Font = SystemFonts.MessageBoxFont };
    public static DataGridView TableView(TableNode table)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        for (var i = 0; i < table.Columns.Length; i++) { grid.Columns.Add("c" + i, DisplayText.Escape(table.Columns[i].Name)); grid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable; }
        if (grid.Columns.Count > 0) foreach (var row in table.Rows) grid.Rows.Add(row.Select(n => (object)new TextRenderer().RenderToString(new(n), 5000)).ToArray());
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex < 0) return; using var dialog = new ZamayDialog(new(DocumentPresentation.RecordDetails(table, e.RowIndex))); dialog.ShowDialog(grid.FindForm()); };
        return grid;
    }
}
