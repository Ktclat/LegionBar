param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetsDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
[IO.Directory]::CreateDirectory($assetsDirectory) | Out-Null
$images = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.FillEllipse([Drawing.Brushes]::Black, $size * 0.035, $size * 0.035, $size * 0.93, $size * 0.93)
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, [single]($size * 0.075))
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $points = [Drawing.PointF[]]@(
        [Drawing.PointF]::new($size * 0.20, $size * 0.54),
        [Drawing.PointF]::new($size * 0.33, $size * 0.54),
        [Drawing.PointF]::new($size * 0.43, $size * 0.28),
        [Drawing.PointF]::new($size * 0.57, $size * 0.72),
        [Drawing.PointF]::new($size * 0.67, $size * 0.46),
        [Drawing.PointF]::new($size * 0.80, $size * 0.46))
    $graphics.DrawLines($pen, $points)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $assetsDirectory 'LegionBar.png'), $stream.ToArray()) }
    $stream.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [IO.File]::Create((Join-Path $assetsDirectory 'LegionBar.ico'))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write($bytes) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output 'Generated LegionBar.ico and LegionBar.png'
