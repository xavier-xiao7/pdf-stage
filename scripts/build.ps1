param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$src = Join-Path $repoRoot 'src'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'dist/PdfStage' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Build on Windows x64 with .NET Framework 4.8 or newer installed.' }
$dependency = & (Join-Path $PSScriptRoot 'get-pdfium.ps1')
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' | ForEach-Object FullName)
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/codepage:65001', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', "/win32manifest:$src/app.manifest", "/win32icon:$src/app.ico", "/out:$OutputDirectory/PdfStage.exe") + $sourceFiles
& $compiler $compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $dependency 'bin/pdfium.dll') -Destination (Join-Path $OutputDirectory 'pdfium.dll') -Force
$licenses = Join-Path $OutputDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
Get-ChildItem -LiteralPath (Join-Path $dependency 'licenses') -File | Copy-Item -Destination $licenses -Force
Copy-Item -LiteralPath (Join-Path $dependency 'LICENSE') -Destination (Join-Path $licenses 'PDFium-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'examples/Demo.pdf') -Destination (Join-Path $OutputDirectory 'Demo.pdf') -Force
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'docs') -Filter '*.txt' | Copy-Item -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md'),(Join-Path $repoRoot 'CHANGELOG.md') -Destination $OutputDirectory -Force
$portableSource = Join-Path $OutputDirectory 'source'
New-Item -ItemType Directory -Force -Path $portableSource | Out-Null
Get-ChildItem -LiteralPath $src -File | Copy-Item -Destination $portableSource -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'rebuild-portable.ps1') -Destination (Join-Path $portableSource 'build.ps1') -Force
Write-Host "Built: $OutputDirectory/PdfStage.exe"
