using System.Collections;
using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using System.Net;
using System.Numerics;
using Zamay.Core;
using Zamay;
using Xunit;

namespace Zamay.Core.Tests;
public sealed class InspectionTests
{
    [Fact] public void AnonymousTypeHasReadableName() => Assert.Equal("Anonymous object", new ObjectInspector().Inspect(new { Id = 1 }).Root.TypeName);
    private readonly ObjectInspector inspector = new();
    private DisplayNode Inspect(object? value, InspectionOptions? options = null) => inspector.Inspect(value, options).Root;
    [Fact] public void NullIsScalar() => Assert.Equal("null", Assert.IsType<ScalarNode>(Inspect(null)).Text);
    [Theory]
    [InlineData(42, "42")]
    [InlineData(true, "true")]
    [InlineData(1.5, "1.5")]
    public void PrimitiveIsScalar(object value, string text) => Assert.Equal(text, Assert.IsType<ScalarNode>(Inspect(value)).Text);
    [Fact] public void EnumIsNamed() => Assert.Equal("Friday", Assert.IsType<ScalarNode>(Inspect(DayOfWeek.Friday)).Text);
    [Fact] public void CyrillicPreserved() => Assert.Equal("\"Привет, мир!\"", "Привет, мир!".ToDisplayString());
    [Fact]
    public void TerminalControlsEscaped()
    {
        var text = "\u001b[31m\r\n\t\0".ToDisplayString();
        Assert.DoesNotContain('\u001b', text); Assert.Contains("\\u001B", text); Assert.Contains("\\r\\n\\t\\u0000", text);
    }
    [Fact]
    public void TruncationPreservesSurrogates()
    { var scalar = Assert.IsType<ScalarNode>(Inspect("A😀B", new() { MaxStringLength = 2 })); Assert.Equal("A", scalar.Text); Assert.True(scalar.Truncated); }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RendererBudgetIsStrictAndSurrogateSafe(int limit)
    {
        var text = new TextRenderer().RenderToString(new(new ScalarNode("😀😀😀")), limit);
        Assert.True(text.Length <= limit);
        for (int i = 0; i < text.Length; i++) if (char.IsHighSurrogate(text[i])) Assert.True(++i < text.Length && char.IsLowSurrogate(text[i]));
    }
    [Fact] public void DepthStopsBeforeGetter() { var value = new Counting(); var o = Assert.IsType<ObjectNode>(Inspect(value, new() { MaxDepth = 0 })); Assert.Equal(0, value.Calls); Assert.Equal(PlaceholderReason.MaxDepthReached, Assert.IsType<PlaceholderNode>(o.Members[0].Value).Reason); }
    [Fact] public void NodeBudgetStopsBeforeGetter() { var value = new Counting(); Inspect(value, new() { MaxNodes = 1 }); Assert.Equal(0, value.Calls); }
    [Fact] public void MemberBudgetStopsBeforeGetter() { var value = new Counting(); Inspect(value, new() { MaxObjectMembers = 0 }); Assert.Equal(0, value.Calls); }
    [Fact]
    public void CollectionLimitDoesNotOverEnumerate()
    { var value = new CountingEnumerable(); var result = Assert.IsType<CollectionNode>(Inspect(value, new() { MaxCollectionItems = 3 })); Assert.Equal(3, value.Moves); Assert.Equal(4, result.Items.Length); Assert.True(value.Disposed); }
    [Fact] public void ZeroLimitDoesNotEnumerate() { var value = new CountingEnumerable(); Inspect(value, new() { MaxCollectionItems = 0 }); Assert.Equal(0, value.Starts); }
    [Fact]
    public void CycleDetectedOnPath()
    { var node = new Link(); node.Next = node; var result = Assert.IsType<ObjectNode>(Inspect(node)); Assert.IsType<ReferenceNode>(result.Members.Single().Value); }
    [Fact]
    public void SharedReferenceNotCycle()
    { var shared = new { X = 1 }; var root = Assert.IsType<ObjectNode>(Inspect(new { First = shared, Second = shared })); Assert.All(root.Members, m => Assert.IsType<ObjectNode>(m.Value)); }
    [Fact]
    public void SensitiveGetterNeverCalled()
    { var value = new Sensitive(); var root = Assert.IsType<ObjectNode>(Inspect(value)); Assert.False(value.Called); Assert.Equal(PlaceholderReason.Redacted, Assert.IsType<PlaceholderNode>(root.Members[0].Value).Reason); }
    [Fact]
    public void MaskingCanBeDisabled()
    { var value = new Sensitive(); Inspect(value, new() { SensitiveData = null }); Assert.True(value.Called); }
    [Fact] public void AdditionalSensitiveNames() { var root = Assert.IsType<ObjectNode>(Inspect(new { PrivateValue = 42 }, new() { SensitiveData = new SensitiveDataPolicy(["PrivateValue"]) })); Assert.IsType<PlaceholderNode>(root.Members[0].Value); }
    [Fact] public void GetterExceptionBecomesError() { var root = Assert.IsType<ObjectNode>(Inspect(new Throwing())); Assert.Equal("failure", Assert.IsType<ErrorNode>(root.Members[0].Value).Message); }
    [Fact]
    public void ExceptionPolicyControlsMessage()
    { var ex = new InvalidOperationException("Password=abc"); Assert.Empty(Assert.IsType<ObjectNode>(Inspect(ex, new() { ExceptionDetail = ExceptionDetailLevel.TypeOnly })).Members); var text = ex.ToDisplayString(); Assert.Contains("REDACTED", text); Assert.DoesNotContain("abc", text); }
    [Fact] public void FullExceptionIncludesInner() { var e = new Exception("outer", new Exception("inner")); Assert.Contains("inner", e.ToDisplayString(new() { ExceptionDetail = ExceptionDetailLevel.Full })); }
    [Fact] public void FieldsOptIn() { Assert.Empty(Assert.IsType<ObjectNode>(Inspect(new Fields())).Members); Assert.Single(Assert.IsType<ObjectNode>(Inspect(new Fields(), new() { IncludeFields = true })).Members); }
    [Fact] public void IndexerSkipped() => Assert.Empty(Assert.IsType<ObjectNode>(Inspect(new Indexer())).Members);
    [Fact] public void RefLikeSkipped() => Assert.IsType<PlaceholderNode>(Assert.IsType<ObjectNode>(Inspect(new RefLike())).Members[0].Value);
    [Fact] public void StreamNotRead() { using var stream = new ForbiddenStream(); Assert.IsType<PlaceholderNode>(Inspect(stream)); Assert.Equal(0, stream.Reads); }
    [Fact] public void TaskNotAwaited() { var source = new TaskCompletionSource<int>(); var result = Assert.IsType<PlaceholderNode>(Inspect(source.Task)); Assert.Contains("WaitingForActivation", result.Detail); Assert.False(source.Task.IsCompleted); }
    [Fact] public void ReaderNotConsumed() { var t = new DataTable(); t.Columns.Add("X"); t.Rows.Add("one"); using var r = t.CreateDataReader(); Assert.IsType<PlaceholderNode>(Inspect(r)); Assert.True(r.Read()); Assert.Equal("one", r.GetString(0)); }
    [Fact] public void DelegateNotInvoked() { var called = false; Assert.IsType<PlaceholderNode>(Inspect((Action)(() => called = true))); Assert.False(called); }
    [Fact] public void SyncAsyncEnumerableNotConsumed() { var source = new AsyncSource(); Assert.IsType<PlaceholderNode>(Inspect(source)); Assert.Equal(0, source.Moves); }
    [Fact] public async Task AsyncEnumerationRequiresOptIn() { var source = new AsyncSource(); Assert.IsType<PlaceholderNode>((await inspector.InspectAsync(source)).Root); Assert.Equal(0, source.Moves); }
    [Fact]
    public async Task AsyncEnumerationBoundedAndDisposed()
    { var source = new AsyncSource(); var result = await inspector.InspectAsync(source, asyncOptions: new() { EnumerateAsyncEnumerable = true, MaxItems = 3 }); Assert.Equal(3, source.Moves); Assert.True(source.Disposed); Assert.Equal(4, Assert.IsType<CollectionNode>(result.Root).Items.Length); }
    [Fact] public async Task AsyncCancellationPropagates() { using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.InspectAsync(new AsyncSource(), cancellationToken: cts.Token)); }
    [Fact] public void TraversalCancellationPropagates() { using var cts = new CancellationTokenSource(); cts.Cancel(); Assert.ThrowsAny<OperationCanceledException>(() => inspector.Inspect(new { X = 1 }, cancellationToken: cts.Token)); }
    [Fact] public void RenderingCancellationPropagates() { using var cts = new CancellationTokenSource(); cts.Cancel(); Assert.ThrowsAny<OperationCanceledException>(() => new TextRenderer().RenderToString(new(new ScalarNode("x")), cancellationToken: cts.Token)); }
    [Fact]
    public void CustomInspectorPriorityAndResolverCache()
    { var low = new Custom(1); var high = new Custom(10); var engine = new ObjectInspector([low, high]); for (int i = 0; i < 5; i++) Assert.Equal("10", Assert.IsType<ScalarNode>(engine.Inspect(new Link()).Root).Text); Assert.Equal(1, high.Checks); Assert.Equal(0, low.Checks); Assert.Equal(1, engine.Resolver.CachedTypeCount); }
    [Fact] public void MetadataReusedAcrossCalls() { Inspect(new Link()); var n = inspector.Metadata.CachedTypeCount; Inspect(new Link()); Assert.Equal(n, inspector.Metadata.CachedTypeCount); Assert.Equal(1, n); }
    [Fact] public void CustomTextWriterUsedAndNotDisposed() { using var writer = new TrackingWriter(); 42.ToConsole(writer); Assert.Equal("42", writer.ToString()); Assert.False(writer.WasDisposed); }
    [Fact] public async Task AsyncConsoleUsesWriter() { using var writer = new StringWriter(); await 42.ToConsoleAsync(writer); Assert.Equal("42", writer.ToString()); }
    public static IEnumerable<object[]> DiagnosticScalars() => new object[] { new Uri("https://example.com/путь"), new Version(1,2), BigInteger.Pow(2,100), (Half)1.5f,
        IPAddress.Loopback, new IPEndPoint(IPAddress.Loopback,80), CultureInfo.GetCultureInfo("ru-RU"), typeof(string), typeof(string).GetMethod("ToUpper", Type.EmptyTypes)!,
        typeof(string).GetProperty("Length")!, typeof(Fields).GetField("Value")!, typeof(string).Assembly.GetName(), Guid.NewGuid(), DateTime.UtcNow, DateOnly.FromDateTime(DateTime.Today), TimeOnly.MinValue }.Select(x => new[] { x });
    [Theory][MemberData(nameof(DiagnosticScalars))] public void StandardDiagnosticTypesAreScalars(object value) => Assert.IsType<ScalarNode>(Inspect(value));
    [Fact] public void TableIsStructuredAndRedacts() { var t = new DataTable("Users"); t.Columns.Add("Id", typeof(int)); t.Columns.Add("Password"); t.Rows.Add(1, "abc"); var node = Assert.IsType<TableNode>(Inspect(t)); Assert.Equal(1, node.TotalRowCount); Assert.IsType<PlaceholderNode>(node.Rows[0][1]); }
    [Fact] public void DataSetContainsTables() { var set = new DataSet(); set.Tables.Add(new DataTable("A")); Assert.IsType<TableNode>(Assert.IsType<CollectionNode>(Inspect(set)).Items[0]); }
    [Fact] public void DictionaryMasksSensitiveKeys() { var node = Assert.IsType<DictionaryNode>(Inspect(new Dictionary<string, string> { ["Token"] = "hidden" })); Assert.IsType<PlaceholderNode>(node.Entries[0].Value); }
    [Fact] public void BytesHaveBoundedHex() { var node = Assert.IsType<ScalarNode>(Inspect(new byte[100], new() { MaxBytePreview = 3 })); Assert.Equal(6, node.Text.Length); Assert.True(node.Truncated); }
    [Fact] public void SortingOptional() { var value = new { Zebra = 1, Alpha = 2 }; Assert.Equal("Zebra", Assert.IsType<ObjectNode>(Inspect(value)).Members[0].Name); Assert.Equal("Alpha", Assert.IsType<ObjectNode>(Inspect(value, new() { SortMembers = true })).Members[0].Name); }
    [Fact] public void InvalidOptionsThrow() { Assert.Throws<ArgumentOutOfRangeException>(() => Inspect(1, new() { MaxNodes = 0 })); Assert.Throws<ArgumentOutOfRangeException>(() => Inspect(1, new() { MaxDepth = -1 })); }
    [Fact] public void RenderingDoesNotReinspect() { var value = new Counting(); var document = inspector.Inspect(value); new TextRenderer().RenderToString(document); new TextRenderer().RenderToString(document); Assert.Equal(1, value.Calls); }
    [Fact] public async Task SessionsAreIsolated() { var values = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => inspector.Inspect(new Link())))); Assert.All(values, d => Assert.IsType<ObjectNode>(d.Root)); }
    [Fact] public void ValueTaskNotAwaited() { var source = new TaskCompletionSource<int>(); Assert.IsType<PlaceholderNode>(Inspect(new ValueTask<int>(source.Task))); Assert.False(source.Task.IsCompleted); }
    [Fact] public void NodeBudgetStopsEnumeration() { var source = new CountingEnumerable(); var node = Assert.IsType<CollectionNode>(Inspect(source, new() { MaxNodes = 2 })); Assert.Equal(1, source.Moves); Assert.Equal(PlaceholderReason.NodeBudgetReached, Assert.IsType<PlaceholderNode>(node.Items[1]).Reason); }
    [Fact] public async Task AsyncNodeBudgetStopsEnumeration() { var source = new AsyncSource(); await inspector.InspectAsync(source, new() { MaxNodes = 2 }, new() { EnumerateAsyncEnumerable = true }); Assert.Equal(1, source.Moves); Assert.True(source.Disposed); }
    private sealed class Counting { public int Calls; public int Value { get { Calls++; return 42; } } }
    private sealed class Sensitive { public string Password { get { Called = true; throw new InvalidOperationException(); } } public bool Called; }
    private sealed class Throwing { public int Value => throw new InvalidOperationException("failure"); }
    private sealed class Link { public Link? Next { get; set; } }
    public sealed class Fields { public int Value = 1; }
    private sealed class Indexer { public int this[int index] => throw new InvalidOperationException(); }
    private sealed class RefLike { public Span<int> Values => throw new InvalidOperationException(); }
    private sealed class Custom(int priority) : IValueInspector { public int Checks; public int Priority => priority; public bool CanHandle(Type t) { Checks++; return true; } public DisplayNode Inspect(object value, InspectionContext context) => new ScalarNode(priority.ToString()); }
    private sealed class TrackingWriter : StringWriter { public bool WasDisposed; protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); } }
    private sealed class ForbiddenStream : MemoryStream { public int Reads; public override int Read(byte[] buffer, int offset, int count) { Reads++; throw new InvalidOperationException(); } }
    private sealed class CountingEnumerable : IEnumerable
    {
        public int Moves, Starts; public bool Disposed;
        public IEnumerator GetEnumerator() { Starts++; return Enumerate().GetEnumerator(); }
        private IEnumerable<int> Enumerate() { try { while (true) { Moves++; yield return 1; } } finally { Disposed = true; } }
    }
    private sealed class AsyncSource : IAsyncEnumerable<int>
    {
        public int Moves; public bool Disposed;
        public async IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        { try { while (true) { cancellationToken.ThrowIfCancellationRequested(); await Task.Yield(); Moves++; yield return Moves; } } finally { Disposed = true; } }
    }
}
