# Safety and budgets

Zamay is a diagnostic snapshot tool, not a serializer or sandbox. Getters and enumeration run application code. Use process isolation when inspecting untrusted executable behavior.

| Option | Default | Meaning |
| --- | ---: | --- |
| MaxDepth | 8 | Root depth is zero; getters beyond the limit are skipped |
| MaxNodes | 10000 | Source values inspected; service markers can exceed this count |
| MaxCollectionItems | 100 | Maximum synchronous items consumed |
| MaxObjectMembers | 100 | Object members or DataTable columns |
| MaxStringLength | 2000 | UTF-16 units retained without splitting a pair |
| MaxTextOutputCharacters | 100000 | Convenience text output budget |
| MaxBytePreview | 64 | Bytes shown in a hexadecimal preview |

MaxNodes must be positive. Other object budgets can be zero but not negative. Reflection uses public instance properties and optionally public fields. Indexers, pointer/ref/byref-like values are skipped. Metadata order is the default; SortMembers uses ordinal names. A sensitive name is checked before its getter. Recoverable getter errors become ErrorNode; cancellation and process-corruption failures are not inspection errors.

Known collection counts are used when cheap. Unknown enumerables are not probed with an extra MoveNext after the limit, so MoreItemsMayExist is deliberately conservative. Enumerators are disposed. A blocking getter or MoveNext cannot be interrupted by CancellationToken. Large explicit depth limits can exhaust the stack; keep limits bounded.

Stream, Task, ValueTask, IDataReader, Delegate, and synchronous inspection of IAsyncEnumerable produce descriptors. Async inspection only consumes a root IAsyncEnumerable after explicit opt-in, and uses await using for disposal. Sources must cooperate with cancellation.

SensitiveDataPolicy performs name-based masking and best-effort string redaction. It is not a guarantee that a free-form string contains no secret or personal data. Disabling the policy is an explicit caller decision. ConnectionString is never read for a DbConnection descriptor. Custom policies and inspectors must be thread-safe and must enforce their own node content limits.

TextRenderer escapes control characters, ESC, quotes, and backslashes while retaining Unicode. It streams to a caller-owned TextWriter. The text output cap can end in an ellipsis and does not promise parseable syntax. Rendering a document does not repeat traversal.

RequiresUnreferencedCode marks reflection entry points. Preserve runtime members yourself when trimming or construct the document directly. The async generic adapter uses MakeGenericMethod; arbitrary async source types are not guaranteed under Native AOT. No AOT certification is claimed.
