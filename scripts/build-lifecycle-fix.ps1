$ErrorActionPreference = 'Stop'
$Workspace = Split-Path -Parent $PSScriptRoot
$env:NUGET_PACKAGES = 'E:\Program\Caches\NuGet\packages'
$env:NUGET_HTTP_CACHE_PATH = 'E:\Program\Caches\NuGet\http'
$env:DOTNET_CLI_HOME = 'E:\Program\Development\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& dotnet build (Join-Path $Workspace 'src\WrathLifecycleFix\WrathLifecycleFix.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle fix build failed' }
$Package = Join-Path $Workspace 'dist\WrathLifecycleFix'
New-Item -ItemType Directory -Force -Path $Package | Out-Null
Copy-Item -LiteralPath (Join-Path $Workspace 'src\WrathLifecycleFix\bin\Release\net481\WrathLifecycleFix.dll') -Destination $Package
Copy-Item -LiteralPath (Join-Path $Workspace 'src\WrathLifecycleFix\Info.json') -Destination $Package
Copy-Item -LiteralPath (Join-Path $Workspace 'docs\lifecycle-fix.md') -Destination (Join-Path $Package 'README.md')
$Version = (Get-Content -LiteralPath (Join-Path $Package 'Info.json') -Raw | ConvertFrom-Json).Version
$Archive = Join-Path $Workspace "dist\WrathLifecycleFix-$Version.zip"
Compress-Archive -Path $Package -DestinationPath $Archive -Force
Write-Output $Archive
