$ErrorActionPreference = 'Stop'
$Workspace = Split-Path -Parent $PSScriptRoot
$env:NUGET_PACKAGES = 'E:\Program\Caches\NuGet\packages'
$env:NUGET_HTTP_CACHE_PATH = 'E:\Program\Caches\NuGet\http'
$env:DOTNET_CLI_HOME = 'E:\Program\Development\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& dotnet build (Join-Path $Workspace 'tests\WrathDeferredNpcViews.Checks\WrathDeferredNpcViews.Checks.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Deferred NPC views checks build failed' }
$CheckLog = Join-Path $Workspace 'analysis\deferred-npc-views-checks.txt'
& (Join-Path $Workspace 'tests\WrathDeferredNpcViews.Checks\bin\Release\net481\WrathDeferredNpcViews.Checks.exe') | Tee-Object -FilePath $CheckLog
if ($LASTEXITCODE -ne 0) { throw 'Deferred NPC views checks failed' }
$Package = Join-Path $Workspace 'dist\WrathDeferredNpcViews'
New-Item -ItemType Directory -Force -Path $Package | Out-Null
Copy-Item -LiteralPath (Join-Path $Workspace 'src\WrathDeferredNpcViews\bin\Release\net481\WrathDeferredNpcViews.dll') -Destination $Package
Copy-Item -LiteralPath (Join-Path $Workspace 'src\WrathDeferredNpcViews\Info.json') -Destination $Package
Copy-Item -LiteralPath (Join-Path $Workspace 'docs\deferred-npc-views.md') -Destination (Join-Path $Package 'README.md')
$Archive = Join-Path $Workspace 'dist\WrathDeferredNpcViews-0.1.0.zip'
Compress-Archive -Path $Package -DestinationPath $Archive -Force
Write-Output $Archive
