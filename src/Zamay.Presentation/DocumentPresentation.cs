using TextRenderer = Zamay.Core.TextRenderer;
using System.Collections.Immutable;
using Zamay.Core;

namespace Zamay.Presentation;

public enum DisplayViewKind { Text, Tree, Table }
public sealed record DisplayTreeItem(string Label, DisplayNode Node, ImmutableArray<DisplayTreeItem> Children);
/// <summary>Pure document mapping: no getters, data sources or platform controls.</summary>
public static class DocumentPresentation
{
    public static DisplayViewKind SelectView(DisplayNode node) => node switch { TableNode => DisplayViewKind.Table, ObjectNode or CollectionNode or DictionaryNode => DisplayViewKind.Tree, _ => DisplayViewKind.Text };
    public static DisplayTreeItem Map(DisplayNode node, string label = "Root")
    {
        IEnumerable<DisplayTreeItem> children = node switch
        {
            ObjectNode o => o.Members.Select(m => Map(m.Value, m.Name)),
            CollectionNode c => c.Items.Select((v, i) => Map(v, "[" + i + "]")),
            DictionaryNode d => d.Entries.Select((e, i) => new DisplayTreeItem("[" + i + "]", e.Value, [Map(e.Key, "Key"), Map(e.Value, "Value")])),
            _ => []
        };
        return new(DisplayText.Escape(label), node, children.ToImmutableArray());
    }
    public static IEnumerable<DisplayTreeItem> Search(DisplayTreeItem item, string text)
    {
        if (item.Label.Contains(text, StringComparison.OrdinalIgnoreCase) || Summary(item.Node).Contains(text, StringComparison.OrdinalIgnoreCase)) yield return item;
        foreach (var child in item.Children) foreach (var match in Search(child, text)) yield return match;
    }
    public static string Summary(DisplayNode node) => node is ObjectNode or CollectionNode or DictionaryNode or TableNode
        ? DisplayText.Escape(node.TypeName) : new TextRenderer().RenderToString(new(node), 500);
    public static TableNode RecordDetails(TableNode table, int row)
    {
        if (row < 0 || row >= table.Rows.Length) throw new ArgumentOutOfRangeException(nameof(row));
        return new("Record details (loaded projection)", [new("Column", "string"), new("Type", "string"), new("Value", "value")],
            table.Rows[row].Select((cell, i) => ImmutableArray.Create<DisplayNode>(new ScalarNode(table.Columns[i].Name),
                new ScalarNode(table.Columns[i].DataType + " / " + cell.TypeName), cell)).ToImmutableArray());
    }
}

