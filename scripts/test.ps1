$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
$testDirectory = Join-Path $repoRoot '.cache/tests'
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$node = Get-Command node -ErrorAction Stop
& $node.Source (Join-Path $repoRoot 'tests/make-fixtures.js') $testDirectory
if ($LASTEXITCODE -ne 0) { throw 'PDF test fixture generation failed.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'dist/PdfStage/pdfium.dll') -Destination $testDirectory -Force
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$common = @('/nologo', '/target:exe', '/platform:x64', '/utf8output', '/codepage:65001', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
$coreExe = Join-Path $testDirectory 'Tests.exe'
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' | ForEach-Object FullName) + (Join-Path $repoRoot 'tests/Tests.cs')
& $compiler ($common + @('/main:PdfStage.Tests', "/out:$coreExe") + $sourceFiles)
if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed.' }
$menuExe = Join-Path $testDirectory 'MenuRegression.exe'
& $compiler ($common + @("/out:$menuExe", (Join-Path $repoRoot 'tests/MenuRegression.cs')))
if ($LASTEXITCODE -ne 0) { throw 'Menu regression compilation failed.' }
Write-Host 'Interactive tests will open windows, move the pointer and send keyboard input.'
& $coreExe $testDirectory 2>&1 | Tee-Object -FilePath (Join-Path $testDirectory 'core-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed. See .cache/tests/core-results.txt.' }
& $menuExe (Join-Path $repoRoot 'dist/PdfStage/PdfStage.exe') (Join-Path $repoRoot 'examples/Demo.pdf') 2>&1 | Tee-Object -FilePath (Join-Path $testDirectory 'menu-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Menu regression failed. See .cache/tests/menu-results.txt.' }
Write-Host 'All core and menu regression checks passed.'
