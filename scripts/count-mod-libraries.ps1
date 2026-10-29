param(
    [string]$GameRoot = 'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure',
    [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameRoot 'Wrath_Data\Managed\UnityModManager\dnlib.dll')
$rows = New-Object System.Collections.Generic.List[object]
$modsDir = Join-Path $GameRoot 'Mods'
foreach ($dir in Get-ChildItem $modsDir -Directory) {
    $infoPath = Join-Path $dir.FullName 'Info.json'
    if (-not (Test-Path $infoPath)) { continue }
    $info = Get-Content $infoPath -Raw | ConvertFrom-Json
    $asmName = $info.AssemblyName
    if ([string]::IsNullOrWhiteSpace($asmName)) { $asmName = $info.Id + '.dll' }
    $asmPath = Join-Path $dir.FullName $asmName
    if (-not (Test-Path $asmPath)) { continue }
    $module = $null
    try {
        $module = [dnlib.DotNet.ModuleDefMD]::Load($asmPath)
        $types = @($module.GetTypes())
        $bpCore = 0; $ttwCore = 0; $codex = 0; $kingmaker = 0
        foreach ($t in $types) {
            $n = $t.FullName
            if ($n.StartsWith('BlueprintCore.')) { $bpCore++ }
            elseif ($n.StartsWith('TabletopTweaks.Core.')) { $ttwCore++ }
            elseif ($n.StartsWith('CodexLib.')) { $codex++ }
            elseif ($n.StartsWith('Kingmaker.')) { $kingmaker++ }
        }
        $rows.Add([pscustomobject]@{
            Id = $info.Id
            SizeKB = [math]::Round((Get-Item $asmPath).Length / 1KB, 0)
            Types = $types.Count
            BlueprintCore = $bpCore
            TTWCore = $ttwCore
            CodexLib = $codex
            Kingmaker = $kingmaker
        })
    }
    finally { if ($module) { $module.Dispose() } }
}
$rows | Sort-Object Types -Descending | Format-Table -AutoSize
$sum = [pscustomobject]@{
    Mods = $rows.Count
    TotalTypes = ($rows | Measure-Object Types -Sum).Sum
    TotalBlueprintCoreTypes = ($rows | Measure-Object BlueprintCore -Sum).Sum
    TotalTTWCoreTypes = ($rows | Measure-Object TTWCore -Sum).Sum
    TotalCodexLibTypes = ($rows | Measure-Object CodexLib -Sum).Sum
    TotalDllBytes = ($rows | Measure-Object SizeKB -Sum).Sum * 1KB
}
$sum | Format-List
if ($OutFile) { $rows | Sort-Object Types -Descending | Export-Csv -Path $OutFile -NoTypeInformation -Encoding UTF8 }
