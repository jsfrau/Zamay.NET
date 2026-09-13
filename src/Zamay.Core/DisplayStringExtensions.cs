using TextRenderer = Zamay.Core.TextRenderer;
using System.Diagnostics.CodeAnalysis;
using Zamay.Core;

namespace Zamay;

public static class DisplayStringExtensions
{
    private static readonly ObjectInspector Inspector = new();
    [RequiresUnreferencedCode("Runtime object inspection uses reflection; preserve inspected members when trimming.")]
    public static string ToDisplayString(this object? value, InspectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        return new TextRenderer().RenderToString(Inspector.Inspect(value, options, cancellationToken), options.MaxTextOutputCharacters, cancellationToken);
    }
}

