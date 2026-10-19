$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $repoRoot '.cache/pdfium-8100'
$archive = Join-Path $repoRoot '.cache/pdfium-win-x64-8100.tgz'
$expectedHash = '51621bcc1d9be9ea448f4b84b0516862b928e3968d3fca899af1a6d6e2848d6d'
$downloadUrl = 'https://github.com/bblanchon/pdfium-binaries/releases/download/chromium/8100/pdfium-win-x64.tgz'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
if (!(Test-Path -LiteralPath $archive)) {
    Write-Host 'Downloading pinned PDFium chromium/8100...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $archive
}
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) { throw "PDFium checksum mismatch. Remove the cached archive and retry: $archive" }
& tar.exe -xzf $archive -C $cache
if ($LASTEXITCODE -ne 0) { throw 'PDFium extraction failed. Windows tar.exe is required.' }
if (!(Test-Path -LiteralPath (Join-Path $cache 'bin/pdfium.dll'))) { throw 'The PDFium archive does not contain the expected DLL.' }
Write-Output $cache
