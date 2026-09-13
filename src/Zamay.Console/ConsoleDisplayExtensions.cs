using TextRenderer = Zamay.Core.TextRenderer;
using System.Diagnostics.CodeAnalysis;
using Zamay.Core;

namespace Zamay;

public static class ConsoleDisplayExtensions
{
    private static readonly ObjectInspector Inspector = new();
    /// <summary>Writes to Console.Out or a caller-owned writer. The writer is never disposed.</summary>
    [RequiresUnreferencedCode("Runtime object inspection uses reflection.")]
    public static void ToConsole(this object? value, TextWriter? writer = null, InspectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        new TextRenderer().Render(Inspector.Inspect(value, options, cancellationToken), writer ?? System.Console.Out, options.MaxTextOutputCharacters, cancellationToken);
    }
    [RequiresUnreferencedCode("Runtime object inspection uses reflection.")]
    public static async Task ToConsoleAsync(this object? value, TextWriter? writer = null, InspectionOptions? options = null,
        AsyncInspectionOptions? asyncOptions = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var document = await Inspector.InspectAsync(value, options, asyncOptions, cancellationToken).ConfigureAwait(false);
        new TextRenderer().Render(document, writer ?? System.Console.Out, options.MaxTextOutputCharacters, cancellationToken);
    }
}

