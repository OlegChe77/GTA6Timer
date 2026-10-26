# Сборка GTA6Timer.exe: своя иконка (без логотипа Rockstar) + компиляция csc (.NET Framework 4).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Add-Type -AssemblyName System.Drawing

# app.ico: скруглённый квадрат с закатным градиентом и «VI», PNG 256x256 внутри ICO
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56
$path.AddArc(8, 8, $r, $r, 180, 90); $path.AddArc(248 - $r, 8, $r, $r, 270, 90)
$path.AddArc(248 - $r, 248 - $r, $r, $r, 0, 90); $path.AddArc(8, 248 - $r, $r, $r, 90, 90); $path.CloseFigure()
$br = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 0, 0, 256, 256), ([System.Drawing.Color]::FromArgb(255, 70, 40, 160)), ([System.Drawing.Color]::FromArgb(255, 255, 150, 60)), 90
$blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
$blend.Colors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(255, 60, 40, 150), [System.Drawing.Color]::FromArgb(255, 230, 50, 140), [System.Drawing.Color]::FromArgb(255, 255, 170, 70))
$blend.Positions = [single[]]@(0, 0.55, 1)
$br.InterpolationColors = $blend
$g.FillPath($br, $path)
$font = New-Object System.Drawing.Font 'Segoe UI Black', 120, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
$g.DrawString('VI', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 6, 256, 256), $sf)
$g.Dispose()
$ms = New-Object IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
$png = $ms.ToArray()
$fs = [IO.File]::Create("$root\app.ico")
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
$bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)
$bw.Write($png); $bw.Close()

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/win32icon:$root\app.ico" "/out:$root\GTA6Timer.exe" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "$root\src\GTA6Timer.cs"
if ($LASTEXITCODE -ne 0) { throw "Ошибка компиляции" }
"OK: $root\GTA6Timer.exe"
