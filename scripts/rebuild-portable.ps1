# This script is copied to source/build.ps1 inside the portable release.
$ErrorActionPreference = 'Stop'
$output = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$sourceFiles = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/codepage:65001', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', "/win32manifest:$PSScriptRoot/app.manifest", "/win32icon:$PSScriptRoot/app.ico", "/out:$output/PdfStage.exe") + $sourceFiles
& $compiler $compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed. Close the running application before rebuilding.' }
Write-Host "Built: $output/PdfStage.exe"
