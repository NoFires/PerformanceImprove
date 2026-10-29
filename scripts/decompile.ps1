param(
    [string]$GameRoot = 'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure',
    [string]$ILSpy = 'E:\Program\ReverseEngineering\ILSpyCmd\ilspycmd.exe'
)
$ErrorActionPreference = 'Stop'
$Workspace = Split-Path -Parent $PSScriptRoot
$Managed = Join-Path $GameRoot 'Wrath_Data\Managed'
$Targets = @(
    @{ Name='Assembly-CSharp'; Path=(Join-Path $Managed 'Assembly-CSharp.dll') },
    @{ Name='Assembly-CSharp-firstpass'; Path=(Join-Path $Managed 'Assembly-CSharp-firstpass.dll') },
    @{ Name='Owlcat.Runtime.Core'; Path=(Join-Path $Managed 'Owlcat.Runtime.Core.dll') },
    @{ Name='Owlcat.Runtime.Visual'; Path=(Join-Path $Managed 'Owlcat.Runtime.Visual.dll') },
    @{ Name='ManyModsPerformanceFix'; Path=(Join-Path $GameRoot 'Mods\!ManyModsPerformanceFix\!ManyModsPerformanceFix.dll') },
    @{ Name='ModTimer'; Path=(Join-Path $GameRoot 'Mods\!!ModTimer\!!ModTimer.dll') },
    @{ Name='BubbleTweaks'; Path=(Join-Path $GameRoot 'Mods\BubbleTweaks\BubbleTweaks.dll') },
    @{ Name='WrathPatches'; Path=(Join-Path $GameRoot 'Mods\WrathPatches\WrathPatches.dll') }
)
foreach ($Target in $Targets) {
    if (!(Test-Path -LiteralPath $Target.Path)) { Write-Warning "Missing $($Target.Path)"; continue }
    $Output = Join-Path $Workspace ('analysis\decompiled\' + $Target.Name)
    & $ILSpy -p -o $Output -r $Managed $Target.Path
    if ($LASTEXITCODE -ne 0) { throw "Decompilation failed: $($Target.Name)" }
    Write-Output "Decompiled $($Target.Name)"
}
