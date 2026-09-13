using System.Collections.Immutable;

namespace Zamay.Core;

/// <summary>A materialized diagnostic snapshot. Rendering never revisits the source object.</summary>
public sealed record DisplayDocument(DisplayNode Root);
public abstract record DisplayNode(string TypeName);
public sealed record ScalarNode(string Text, string ScalarType = "", bool Quoted = false, bool Truncated = false) : DisplayNode(ScalarType);
public sealed record DisplayMember(string Name, DisplayNode Value);
public sealed record ObjectNode(string ObjectType, ImmutableArray<DisplayMember> Members) : DisplayNode(ObjectType);
public sealed record CollectionNode(string CollectionType, ImmutableArray<DisplayNode> Items, int? TotalCount = null) : DisplayNode(CollectionType);
public sealed record DictionaryEntryNode(DisplayNode Key, DisplayNode Value);
public sealed record DictionaryNode(string DictionaryType, ImmutableArray<DictionaryEntryNode> Entries) : DisplayNode(DictionaryType);
public sealed record TableColumn(string Name, string DataType);
public sealed record TableNode(string Name, ImmutableArray<TableColumn> Columns, ImmutableArray<ImmutableArray<DisplayNode>> Rows,
    long? TotalRowCount = null, long? OmittedRows = null, int OmittedColumns = 0, DisplayNode? Notice = null) : DisplayNode("Table");
public sealed record ErrorNode(string ErrorType, string? Message = null) : DisplayNode(ErrorType);
public sealed record ReferenceNode(string TargetPath) : DisplayNode("Reference");
public enum PlaceholderReason { MaxDepthReached, NodeBudgetReached, MoreItemsMayExist, MaxMembersReached, Redacted, Unsupported, Descriptor }
public sealed record PlaceholderNode(PlaceholderReason Reason, string Detail = "") : DisplayNode("Placeholder");
