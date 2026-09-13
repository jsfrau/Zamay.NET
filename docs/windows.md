# Windows inspector

Target net8.0-windows and set UseWindowsForms=true. The same Zamay package exposes ToMessageBox and ToMessageBoxAsync in namespace Zamay. The dialogs are Zamay.Windows.ZamayDialog and Zamay.Windows.Sqlite.SqliteBrowserDialog.

An object document maps to a tree with a detail pane. TableNode maps to a read-only DataGridView. Scalar data uses a text view. Search, Copy, Expand all, scrolling, resizing, and a text tab are available. A grid row double-click shows the loaded projection without re-reading the object or database.

Direct Form construction requires STA. The convenience dispatcher accepts a live Control owner with an existing handle and marshals to its thread. Without an owner, async calls use an existing WindowsFormsSynchronizationContext or a dedicated foreground STA thread. A synchronous MTA call waits for that thread. The thread completes only after the dialog has closed and been disposed; exceptions propagate.

Do not synchronously wait on a worker from a UI thread if that worker calls into the same owner. Keep the owner alive until the operation completes. Cancellation closes the dialog on its UI thread; the database browser waits for its in-flight operation before closing. Browser updates explicitly marshal to the form after database awaits.

The library does not change process-wide DPI policy. Configure it in the application's startup, for example with ApplicationConfiguration.Initialize. The shipped smoke tests show forms outside the visible desktop, load schema and Latest rows, inspect grid data, and close them. They are not a substitute for testing every owner lifetime, DPI, theme, clipboard, and accessibility configuration.
