# Builds bin\Androidzy.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# No SDK, Visual Studio or NuGet needed.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1            (output: bin\Androidzy.exe)
#   powershell -ExecutionPolicy Bypass -File build.ps1 -OutDir X  (output: X\Androidzy.exe)
param([string]$OutDir)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$bin  = if ($OutDir) { $OutDir } else { Join-Path $root 'bin' }
$obj  = Join-Path $root 'obj'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
New-Item -ItemType Directory -Force $bin, $obj | Out-Null

# app icon: green rounded square with a play triangle, drawn once into obj\
$ico = Join-Path $obj 'Androidzy.ico'
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 16, 24, 32, 48, 64, 128, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $r = [single]($s * 0.22); $d = $r * 2; $w = $s - 1
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0, 0, $d, $d, 180, 90);           $path.AddArc($w - $d, 0, $d, $d, 270, 90)
        $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90)
        $path.CloseFigure()
        $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 36, 214, 128)), ([System.Drawing.Color]::FromArgb(255, 8, 120, 92)), 45
        $g.FillPath($brush, $path)
        $tri = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ($s * 0.38), ($s * 0.28)),
            (New-Object System.Drawing.PointF ($s * 0.38), ($s * 0.72)),
            (New-Object System.Drawing.PointF ($s * 0.74), ($s * 0.50)))
        $g.FillPolygon([System.Drawing.Brushes]::White, $tri)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        ,@{ Size = $s; Data = $ms.ToArray() }
    }
    $fs = [System.IO.File]::Create($ico); $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$p.Data.Length); $bw.Write([uint32]$offset)
        $offset += $p.Data.Length
    }
    foreach ($p in $pngs) { $bw.Write($p.Data) }
    $bw.Close(); $fs.Close()
}

# x64: the launcher inspects 64-bit emulator processes, which a 32-bit process cannot do (csc defaults to 32-bit preferred)
$files = Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$bin\Androidzy.exe" "/win32icon:$ico" "/win32manifest:$src\app.manifest" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll `
    /r:System.Xml.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    $files
if ($LASTEXITCODE -ne 0) { throw "compile failed" }
"Built $bin\Androidzy.exe"
