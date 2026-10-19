$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1')
$appDirectory = Join-Path $repoRoot 'dist/PdfStage'
$fileVersion = [Version](Get-Item -LiteralPath (Join-Path $appDirectory 'PdfStage.exe')).VersionInfo.FileVersion
$version = '{0}.{1}.{2}' -f $fileVersion.Major,$fileVersion.Minor,$fileVersion.Build
$zip = Join-Path $repoRoot ("dist/PdfStage-$version-Windows-x64.zip")
Compress-Archive -LiteralPath $appDirectory -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zip + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($zip) + "`n", [Text.Encoding]::ASCII)
Write-Host "Packaged: $zip"
Write-Host "SHA-256: $hash"
