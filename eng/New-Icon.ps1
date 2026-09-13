# Recreates icon.png from the same simple geometry as eng/icon.svg.
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(256,256)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$light = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#e7edf3'),13)
$accent = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#59d1ba'),13)
try {
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#142638'))
    $graphics.DrawLines($light,[Drawing.Point[]]@([Drawing.Point]::new(83,63),[Drawing.Point]::new(58,63),[Drawing.Point]::new(58,193),[Drawing.Point]::new(83,193)))
    $graphics.DrawLines($light,[Drawing.Point[]]@([Drawing.Point]::new(173,63),[Drawing.Point]::new(198,63),[Drawing.Point]::new(198,193),[Drawing.Point]::new(173,193)))
    $graphics.DrawEllipse($accent,90,84,64,64)
    $accent.Width=15
    $accent.StartCap=$accent.EndCap=[Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($accent,145,140,174,171)
    $bitmap.Save((Join-Path $PSScriptRoot '../icon.png'),[Drawing.Imaging.ImageFormat]::Png)
} finally { $accent.Dispose();$light.Dispose();$graphics.Dispose();$bitmap.Dispose() }
