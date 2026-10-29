# Static metadata census for installed UMM mods (no code execution).
# Uses dnlib shipped with UnityModManager to read assemblies read-only.
param(
    [string]$GameRoot = 'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure',
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'
$dnlib = Join-Path $GameRoot 'Wrath_Data\Managed\UnityModManager\dnlib.dll'
Add-Type -Path $dnlib

function Get-AllTypes($module) {
    return @($module.GetTypes())
}

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
        $methods = 0
        $harmonyClasses = 0
        $harmonyMethods = 0
        $typeIdTypes = 0
        foreach ($t in $types) {
            $methods += $t.Methods.Count
            foreach ($ca in $t.CustomAttributes) {
                $name = $ca.TypeFullName
                if ($name -like '*HarmonyPatch*') { $harmonyClasses++; break }
            }
            foreach ($m in $t.Methods) {
                foreach ($ca in $m.CustomAttributes) {
                    if ($ca.TypeFullName -like '*HarmonyPatch*') { $harmonyMethods++; break }
                }
            }
            foreach ($ca in $t.CustomAttributes) {
                if ($ca.TypeFullName -like '*TypeIdAttribute*') { $typeIdTypes++; break }
            }
        }
        $rows.Add([pscustomobject]@{
            Id = $info.Id
            Assembly = $asmName
            SizeKB = [math]::Round((Get-Item $asmPath).Length / 1KB, 0)
            Types = $types.Count
            Methods = $methods
            HarmonyClasses = $harmonyClasses
            HarmonyMethods = $harmonyMethods
            TypeIdTypes = $typeIdTypes
        })
    }
    catch {
        Write-Warning ("{0}: {1}" -f $dir.Name, $_.Exception.Message)
    }
    finally {
        if ($module) { $module.Dispose() }
    }
}

$rows | Sort-Object Types -Descending | Format-Table -AutoSize
"" 
$summary = [pscustomobject]@{
    Mods = $rows.Count
    TotalTypes = ($rows | Measure-Object Types -Sum).Sum
    TotalMethods = ($rows | Measure-Object Methods -Sum).Sum
    TotalHarmonyClasses = ($rows | Measure-Object HarmonyClasses -Sum).Sum
    TotalHarmonyMethods = ($rows | Measure-Object HarmonyMethods -Sum).Sum
    TotalTypeIdTypes = ($rows | Measure-Object TypeIdTypes -Sum).Sum
}
$summary | Format-List
if ($OutFile) {
    $rows | Sort-Object Types -Descending | Export-Csv -Path $OutFile -NoTypeInformation -Encoding UTF8
    Write-Host "Wrote $OutFile"
}
