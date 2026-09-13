using System.Text.RegularExpressions;

namespace Zamay.Core;

public enum ExceptionDetailLevel { TypeOnly, TypeAndMessage, Full }
/// <summary>Names are checked before getters run. Sanitization is best-effort, not secret discovery.</summary>
public interface ISensitiveDataPolicy
{
    bool IsSensitive(string name);
    string Sanitize(string value);
}
public sealed class SensitiveDataPolicy : ISensitiveDataPolicy
{
    private readonly HashSet<string> names;
    public static SensitiveDataPolicy Default { get; } = new();
    public SensitiveDataPolicy(IEnumerable<string>? additionalNames = null)
    {
        names = new(StringComparer.OrdinalIgnoreCase) { "Password", "Passwd", "Secret", "Token", "ApiKey", "Authorization", "ConnectionString" };
        if (additionalNames is not null) names.UnionWith(additionalNames);
    }
    public bool IsSensitive(string name) => names.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase));
    public string Sanitize(string value) => Regex.Replace(value,
        @"(?i)(password|passwd|secret|token|apikey|authorization|connectionstring)\s*[:=]\s*([^;\r\n]*)",
        "$1=[REDACTED]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
/// <summary>Immutable per-call configuration. Null SensitiveData disables masking explicitly.</summary>
public sealed record InspectionOptions
{
    /// <summary>Maximum child depth; the root has depth zero. Getters beyond this depth are not called.</summary>
    public int MaxDepth { get; init; } = 8;
    /// <summary>Maximum inspected source values per call. Semantic limit markers may be added beyond this budget.</summary>
    public int MaxNodes { get; init; } = 10000;
    /// <summary>Maximum items consumed from a collection; no extra MoveNext is used to probe unknown counts.</summary>
    public int MaxCollectionItems { get; init; } = 100;
    /// <summary>Maximum object members or DataTable columns inspected.</summary>
    public int MaxObjectMembers { get; init; } = 100;
    /// <summary>Maximum UTF-16 units retained in a string, without splitting a surrogate pair.</summary>
    public int MaxStringLength { get; init; } = 2000;
    /// <summary>Text output limit used by convenience methods; direct renderers accept their own limit.</summary>
    public int MaxTextOutputCharacters { get; init; } = 100000;
    /// <summary>Maximum bytes copied into a diagnostic hexadecimal preview.</summary>
    public int MaxBytePreview { get; init; } = 64;
    /// <summary>Includes public instance fields in addition to properties when enabled.</summary>
    public bool IncludeFields { get; init; }
    /// <summary>Uses ordinal member-name order instead of metadata order when enabled.</summary>
    public bool SortMembers { get; init; }
    /// <summary>Policy checked before reading named sensitive values. Null explicitly disables masking.</summary>
    public ISensitiveDataPolicy? SensitiveData { get; init; } = SensitiveDataPolicy.Default;
    /// <summary>Controls exception message and inner-exception inspection.</summary>
    public ExceptionDetailLevel ExceptionDetail { get; init; } = ExceptionDetailLevel.TypeAndMessage;
    /// <summary>Includes the stack trace only when ExceptionDetail is Full.</summary>
    public bool IncludeStackTrace { get; init; }
    public void Validate()
    {
        foreach (var (name, value) in new[] { (nameof(MaxDepth),MaxDepth),(nameof(MaxNodes),MaxNodes),
            (nameof(MaxCollectionItems),MaxCollectionItems),(nameof(MaxObjectMembers),MaxObjectMembers),
            (nameof(MaxStringLength),MaxStringLength),(nameof(MaxTextOutputCharacters),MaxTextOutputCharacters),(nameof(MaxBytePreview),MaxBytePreview) })
            if (value < (name == nameof(MaxNodes) ? 1 : 0)) throw new ArgumentOutOfRangeException(name);
        if (!Enum.IsDefined(ExceptionDetail)) throw new ArgumentOutOfRangeException(nameof(ExceptionDetail));
    }
}
/// <summary>Async sources remain descriptors unless enumeration is explicitly enabled. Cancellation is cooperative.</summary>
public sealed record AsyncInspectionOptions
{
    /// <summary>Explicitly permits bounded enumeration of a root IAsyncEnumerable; nested sources remain descriptors.</summary>
    public bool EnumerateAsyncEnumerable { get; init; }
    /// <summary>Async item limit, combined with the ordinary collection and node budgets.</summary>
    public int MaxItems { get; init; } = 100;
    public void Validate() { if (MaxItems < 0) throw new ArgumentOutOfRangeException(nameof(MaxItems)); }
}
internal static class InspectionErrors
{
    internal static bool Recoverable(Exception e) => e is not OutOfMemoryException and not StackOverflowException
        and not AccessViolationException and not OperationCanceledException;
}
public static class DisplayText
{
    public static string Truncate(string text, int limit)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (text.Length <= limit) return text;
        if (limit > 0 && char.IsHighSurrogate(text[limit - 1])) limit--;
        return text[..limit];
    }
    /// <summary>Preserves Unicode while escaping controls, quotes and terminal escape sequences.</summary>
    public static string Escape(string text)
    {
        var b = new System.Text.StringBuilder();
        foreach (var c in text) b.Append(c switch
        {
            '\\' => "\\\\",
            '"' => "\\\"",
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ when char.IsControl(c) => "\\u" + ((int)c).ToString("X4"),
            _ => c.ToString()
        });
        return b.ToString();
    }
}

