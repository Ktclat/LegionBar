param([switch]$SelfContained, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$outputDirectory = Join-Path $projectDirectory 'artifacts\portable'
$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }
Push-Location $projectDirectory
try {
    if (-not $NoRestore) {
        dotnet restore LegionBar.csproj -r win-x64 --locked-mode -p:SelfContained=$selfContainedValue -p:EnableRuntimePackDownload=$selfContainedValue
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    }
    dotnet publish LegionBar.csproj --no-restore -c Release -r win-x64 --self-contained $selfContainedValue -p:EnableRuntimePackDownload=$selfContainedValue -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $outputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    foreach ($name in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath $name -Destination $outputDirectory -Force }
    Copy-Item -LiteralPath 'docs\ICON-DISCLAIMER.md' -Destination $outputDirectory -Force
    Copy-Item -Path 'licenses\*' -Destination $outputDirectory -Force
    $digest = (Get-FileHash -LiteralPath (Join-Path $outputDirectory 'LegionBar.exe') -Algorithm SHA256).Hash
    "$digest  LegionBar.exe" | Set-Content -LiteralPath (Join-Path $outputDirectory 'SHA256SUMS.txt') -Encoding ascii
    Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath (Join-Path $projectDirectory 'artifacts\LegionBar-win-x64.zip') -Force
    Write-Output "Build complete: $outputDirectory"
} finally { Pop-Location }
