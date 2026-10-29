$ErrorActionPreference = 'Stop'
$Workspace = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path (Join-Path $Workspace 'analysis') | Out-Null
$env:NUGET_PACKAGES = 'E:\Program\Caches\NuGet\packages'
$env:NUGET_HTTP_CACHE_PATH = 'E:\Program\Caches\NuGet\http'
$env:DOTNET_CLI_HOME = 'E:\Program\Development\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
foreach ($CheckName in @('WrathLifecycleFix.Checks', 'WrathDeferredNpcViews.Checks', 'PerformanceImprove.Checks', 'PerformanceImprove.MerchantChecks', 'PerformanceImprove.DiagnosticsChecks', 'PerformanceImprove.LoadChecks')) {
    $Project = Join-Path $Workspace "tests\$CheckName\$CheckName.csproj"
    & dotnet build $Project -c Release --nologo -p:UsePerformanceImprove=true
    if ($LASTEXITCODE -ne 0) { throw "$CheckName build failed" }
    $Output = if ($CheckName.StartsWith('PerformanceImprove.')) { 'bin\Release\net481' } else { 'bin\PerformanceImprove\Release\net481' }
    $Executable = Join-Path $Workspace "tests\$CheckName\$Output\$CheckName.exe"
    & $Executable | Tee-Object -FilePath (Join-Path $Workspace "analysis\performance-improve-$CheckName.txt")
    if ($LASTEXITCODE -ne 0) { throw "$CheckName failed" }
}
$Package = Join-Path $Workspace 'dist\PerformanceImprove'
New-Item -ItemType Directory -Force -Path $Package | Out-Null
Copy-Item -LiteralPath (Join-Path $Workspace 'src\PerformanceImprove\bin\Release\net481\PerformanceImprove.dll') -Destination $Package
Copy-Item -LiteralPath (Join-Path $Workspace 'src\PerformanceImprove\Info.json') -Destination $Package
Copy-Item -LiteralPath (Join-Path $Workspace 'docs\performance-improve.md') -Destination (Join-Path $Package 'README.md')
if (Test-Path -LiteralPath (Join-Path $Workspace 'docs\merchant-sale-cleanup.md')) {
    Copy-Item -LiteralPath (Join-Path $Workspace 'docs\merchant-sale-cleanup.md') -Destination (Join-Path $Package 'merchant-sale-cleanup.md')
}
if (Test-Path -LiteralPath (Join-Path $Workspace 'docs\load-cleanup.md')) {
    Copy-Item -LiteralPath (Join-Path $Workspace 'docs\load-cleanup.md') -Destination (Join-Path $Package 'load-cleanup.md')
}
$Version = (Get-Content -LiteralPath (Join-Path $Package 'Info.json') -Raw | ConvertFrom-Json).Version
$Archive = Join-Path $Workspace "dist\PerformanceImprove-$Version.zip"
Compress-Archive -Path $Package -DestinationPath $Archive -Force
Write-Output $Archive
