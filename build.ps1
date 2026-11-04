$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$appDir = Join-Path $projectRoot 'app'
New-Item -ItemType Directory -Path $appDir,(Join-Path $projectRoot 'work') -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(64,64)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::Transparent)
$back = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#202326'))
$graphics.FillEllipse($back,1,1,62,62)
$pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#72DDC2'),5)
$pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
$graphics.DrawArc($pen,12,12,40,40,-90,270)
$white = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F4F6F7'))
$graphics.FillEllipse($white,27,27,10,10)
$png = [IO.MemoryStream]::new()
$bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
$iconPath = Join-Path $projectRoot 'work\app.ico'
$ico = [IO.BinaryWriter]::new([IO.File]::Create($iconPath))
$ico.Write([uint16]0); $ico.Write([uint16]1); $ico.Write([uint16]1)
$ico.Write([byte]64); $ico.Write([byte]64); $ico.Write([byte]0); $ico.Write([byte]0)
$ico.Write([uint16]1); $ico.Write([uint16]32); $ico.Write([uint32]$png.Length); $ico.Write([uint32]22); $ico.Write($png.ToArray()); $ico.Dispose()
$png.Dispose(); $pen.Dispose(); $back.Dispose(); $white.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
$refs = @('System.dll','System.Core.dll','Microsoft.CSharp.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /langversion:5 /utf8output ('/win32icon:' + $iconPath) ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')) ('/out:' + (Join-Path $appDir 'CodexQuotaFloat.exe')) $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$test = Start-Process -FilePath (Join-Path $appDir 'CodexQuotaFloat.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $appDir 'test-results.txt')
if ($test.ExitCode -ne 0) { throw 'Tests failed' }
Get-FileHash -LiteralPath (Join-Path $appDir 'CodexQuotaFloat.exe') -Algorithm SHA256
