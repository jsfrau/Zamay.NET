using System.Diagnostics.CodeAnalysis;
using Zamay.Core;
using Zamay.Windows;

namespace Zamay;
public static class WindowsDisplayExtensions
{
    private static readonly ObjectInspector Inspector = new();
    [RequiresUnreferencedCode("Runtime object inspection uses reflection.")]
    public static void ToMessageBox(this object? value, Control? owner = null, InspectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new(); var document = Inspector.Inspect(value, options, cancellationToken);
        DialogDispatcher.Show(() => new ZamayDialog(document, options.MaxTextOutputCharacters, cancellationToken), owner, cancellationToken);
    }
    [RequiresUnreferencedCode("Runtime object inspection uses reflection.")]
    public static async Task ToMessageBoxAsync(this object? value, Control? owner = null, InspectionOptions? options = null,
        AsyncInspectionOptions? asyncOptions = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var document = await Inspector.InspectAsync(value, options, asyncOptions, cancellationToken);
        await DialogDispatcher.ShowAsync(() => new ZamayDialog(document, options.MaxTextOutputCharacters, cancellationToken), owner, cancellationToken);
    }
}
