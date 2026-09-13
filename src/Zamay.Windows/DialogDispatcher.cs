namespace Zamay.Windows;

/// <summary>Routes to a live owner/UI context, or owns a dedicated foreground STA thread until dialog disposal.
/// An owner must have an existing handle. Exceptions propagate to the caller.</summary>
public static class DialogDispatcher
{
    public static void Show(Func<Form> factory, Control? owner = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (owner is not null)
        {
            ValidateOwner(owner);
            if (owner.InvokeRequired) { owner.Invoke(() => Show(factory, owner, cancellationToken)); return; }
        }
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) { Run(factory, owner, cancellationToken); return; }
        ShowAsync(factory, null, cancellationToken).GetAwaiter().GetResult();
    }
    public static Task ShowAsync(Func<Form> factory, Control? owner = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Execute()
        {
            try { Run(factory, owner, cancellationToken); completion.TrySetResult(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }
        if (owner is not null) { ValidateOwner(owner); owner.BeginInvoke(Execute); }
        else if (SynchronizationContext.Current is WindowsFormsSynchronizationContext context) context.Post(_ => Execute(), null);
        else { var thread = new Thread(Execute) { Name = "Zamay dialog", IsBackground = false }; thread.SetApartmentState(ApartmentState.STA); thread.Start(); }
        return completion.Task;
    }
    private static void ValidateOwner(Control owner)
    {
        if (owner.IsDisposed || !owner.IsHandleCreated) throw new ArgumentException("Owner must be live and have a handle.", nameof(owner));
    }
    private static void Run(Func<Form> factory, Control? owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var dialog = factory();
        // A UI timer closes on the owning thread, avoiding cross-thread disposal races.
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) => { if (token.IsCancellationRequested) dialog.Close(); };
        timer.Start();
        dialog.ShowDialog(owner);
        token.ThrowIfCancellationRequested();
    }
}
