$ErrorActionPreference = 'Stop'
$Workspace = Split-Path -Parent $PSScriptRoot
$GameRoot = 'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure'
$Managed = Join-Path $GameRoot 'Wrath_Data\Managed'
$ILSpy = 'E:\Program\ReverseEngineering\ILSpyCmd\ilspycmd.exe'
$Inventory = Get-Content -LiteralPath (Join-Path $Workspace 'analysis\inventory.json') -Raw | ConvertFrom-Json
$Results = @()
$LogRoot = Join-Path $Workspace 'analysis\logs\decompile'
New-Item -ItemType Directory -Force -Path $LogRoot | Out-Null
foreach ($Mod in $Inventory.mods) {
    $Directory = Join-Path $GameRoot ('Mods\' + $Mod.folder)
    $Name = $Mod.AssemblyName
    if (!$Name) { $Name = $Mod.Id + '.dll' }
    $InputDll = Join-Path $Directory $Name
    if (!(Test-Path -LiteralPath $InputDll)) {
        $Results += @{ Folder=$Mod.folder; Status='missing-primary-dll'; Assembly=$InputDll }
        continue
    }
    $OutputName = switch ($Mod.folder) {
        '!!ModTimer' { 'ModTimer' }
        '!ManyModsPerformanceFix' { 'ManyModsPerformanceFix' }
        default { $Mod.folder }
    }
    $Output = Join-Path $Workspace ('analysis\decompiled\' + $OutputName)
    if ((Test-Path -LiteralPath $Output) -and (Get-ChildItem -LiteralPath $Output -Filter '*.csproj')) {
        $Results += @{ Folder=$Mod.folder; Status='existing'; Assembly=$InputDll; Output=$OutputName }
        continue
    }
    & $ILSpy -p -o $Output -r $Managed $InputDll *> (Join-Path $LogRoot ($OutputName + '.txt'))
    $Status = if ($LASTEXITCODE -eq 0) { 'decompiled' } else { 'failed' }
    $Results += @{ Folder=$Mod.folder; Status=$Status; Assembly=$InputDll; Output=$OutputName }
    Write-Output ($Status + ': ' + $Mod.folder)
}
$Results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Workspace 'analysis\mod-decompilation-manifest.json') -Encoding utf8
