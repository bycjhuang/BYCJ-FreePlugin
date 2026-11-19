# Build RefOverlay.exe with the built-in .NET Framework 4.x compiler (no SDK required)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

& $csc /nologo /target:winexe /optimize+ /out:"$root\RefOverlay.exe" `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "$root\RefOverlay.cs"

if ($LASTEXITCODE -ne 0) { throw "compile failed: $LASTEXITCODE" }
Write-Host ("OK -> " + $root + "\RefOverlay.exe") -ForegroundColor Green
