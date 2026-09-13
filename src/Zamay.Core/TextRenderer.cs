namespace Zamay.Core;

/// <summary>Streams escaped text to a caller-owned writer, with a UTF-16-safe output budget.</summary>
public sealed class TextRenderer
{
    public string RenderToString(DisplayDocument document, int maxCharacters = 100000, CancellationToken cancellationToken = default)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Render(document, writer, maxCharacters, cancellationToken);
        return writer.ToString();
    }
    public void Render(DisplayDocument document, TextWriter writer, int maxCharacters = 100000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(writer);
        if (maxCharacters < 0) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        var output = new Output(writer, maxCharacters, cancellationToken);
        WriteNode(document.Root, output, 0);
        output.Finish();
    }
    private static void WriteNode(DisplayNode node, Output output, int indent)
    {
        if (output.Full) return;
        void Write(string s) => output.Write(s);
        void Child(string label, DisplayNode child)
        {
            Write("\n" + new string(' ', Math.Min(indent + 2, 256)) + DisplayText.Escape(label) + ": ");
            WriteNode(child, output, indent + 2);
        }
        switch (node)
        {
            case ScalarNode s:
                Write((s.Quoted ? "\"" : "") + DisplayText.Escape(s.Text) + (s.Quoted ? "\"" : "") + (s.Truncated ? " … [Truncated]" : "")); break;
            case PlaceholderNode p: Write("[" + p.Reason + (p.Detail.Length > 0 ? ": " + DisplayText.Escape(p.Detail) : "") + "]"); break;
            case ReferenceNode r: Write("[Cycle → " + DisplayText.Escape(r.TargetPath) + "]"); break;
            case ErrorNode e: Write("[Error " + DisplayText.Escape(e.ErrorType) + (e.Message is null ? "" : ": " + DisplayText.Escape(e.Message)) + "]"); break;
            case ObjectNode o:
                Write(DisplayText.Escape(o.TypeName) + " {");
                foreach (var m in o.Members) { if (output.Full) break; Child(m.Name, m.Value); }
                Write("\n" + new string(' ', Math.Min(indent, 256)) + "}"); break;
            case CollectionNode c:
                Write(DisplayText.Escape(c.TypeName) + " [");
                for (int i = 0; i < c.Items.Length && !output.Full; i++) Child("[" + i + "]", c.Items[i]);
                Write("\n" + new string(' ', Math.Min(indent, 256)) + "]"); break;
            case DictionaryNode d:
                Write(DisplayText.Escape(d.TypeName) + " {");
                foreach (var entry in d.Entries) { if (output.Full) break; Child("Key", entry.Key); Child("Value", entry.Value); }
                Write("\n" + new string(' ', Math.Min(indent, 256)) + "}"); break;
            case TableNode t:
                Write("Table " + DisplayText.Escape(t.Name) + " | " + string.Join(" | ", t.Columns.Select(c => DisplayText.Escape(c.Name))));
                foreach (var row in t.Rows)
                {
                    if (output.Full) break;
                    Write("\n" + new string(' ', Math.Min(indent + 2, 256)));
                    for (var i = 0; i < row.Length && !output.Full; i++) { if (i > 0) Write(" | "); WriteNode(row[i], output, indent + 2); }
                }
                Write($"\n[Rows: {t.Rows.Length}; total: {t.TotalRowCount?.ToString() ?? "unknown"}; omitted rows: {t.OmittedRows?.ToString() ?? "unknown"}; omitted columns: {t.OmittedColumns}]");
                if (t.Notice is not null) WriteNode(t.Notice, output, indent); break;
        }
    }
    private sealed class Output(TextWriter writer, int limit, CancellationToken token)
    {
        private int written;
        private bool truncated;
        public bool Full { get { token.ThrowIfCancellationRequested(); return truncated; } }
        public void Write(string text)
        {
            token.ThrowIfCancellationRequested();
            if (truncated) return;
            if (text.Length <= limit - written) { writer.Write(text); written += text.Length; return; }
            var part = DisplayText.Truncate(text, Math.Max(0, limit - written - 1));
            writer.Write(part); written += part.Length;
            truncated = true;
        }
        public void Finish() { if (truncated && written < limit) writer.Write('…'); }
    }
}
