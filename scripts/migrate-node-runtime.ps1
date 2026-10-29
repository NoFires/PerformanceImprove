$ErrorActionPreference = 'Stop'
$source = 'E:\Program\node'
$target = 'E:\Program\Development\node'
if ([IO.Path]::GetFullPath($source) -ne 'E:\Program\node' -or
    [IO.Path]::GetFullPath($target) -ne 'E:\Program\Development\node') { throw 'Unexpected migration paths' }
if (!(Test-Path -LiteralPath $source -PathType Container)) { throw 'Source missing' }
if ((Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath (Join-Path $target 'node.exe'))) { throw 'A target runtime already exists' }
if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source must be a real directory' }
$record = Join-Path 'E:\Program\Development\environment-migrations' ('node-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path (Join-Path $record 'before') -Force | Out-Null
$paths = @(
    'C:\Users\RedAndBlack\.codex\config.toml',
    'C:\Users\RedAndBlack\.codex\skills\local-reverse-engineering\references\local-environment.md',
    'C:\Users\RedAndBlack\.dsh\profiles\web\cordis.patch.yml',
    'C:\Users\RedAndBlack\.dsh\skills\reverse-engineering\references\environment.md',
    'E:\Program\DeepSeekDsh\dsh.cmd',
    'E:\Program\Development\pnpm\pnpm.cmd',
    'E:\Program\MCP\probes\test_x64dbg_timeout.py',
    'E:\Program\MCP\probes\probe_tools.py',
    'E:\Program\ReverseEngineering\README-native-tools.zh-CN.md',
    'E:\Program\Development\WrathPerformance\skills\local-reverse-engineering\references\local-environment.md',
    'E:\Program\Development\WrathPerformance\docs\native-tools.md',
    'E:\Program\Development\WrathPerformance\scripts\test_native_mcp.py'
)
$envBefore = @{
    UserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    MachinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
}
$envBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $record 'before\environment.json') -Encoding utf8
$fileRecords = @()
foreach ($path in $paths) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $content = [IO.File]::ReadAllText($path)
    $updated = $content.Replace('E:\Program\node', $target).Replace('E:\\Program\\node', 'E:\\Program\\Development\\node').Replace('E:/Program/node', 'E:/Program/Development/node')
    if ($content -ceq $updated) { continue }
    $backup = Join-Path $record ('before\' + $fileRecords.Count + '-' + [IO.Path]::GetFileName($path))
    Copy-Item -LiteralPath $path -Destination $backup
    $fileRecords += @{ path=$path; backup=$backup; before_sha256=(Get-FileHash -LiteralPath $path).Hash }
}
$hashBefore = (Get-FileHash -LiteralPath (Join-Path $source 'node.exe')).Hash
$running = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like ($source + '\*') })
$processRecords = @($running | Select-Object ProcessId,Name,ExecutablePath)
foreach ($process in $running) {
    $current = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $process.ProcessId)
    if ($current -and $current.ExecutablePath -like ($source + '\*')) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }
}
Start-Sleep -Seconds 1
if (@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like ($source + '\*') }).Count) { throw 'A source runtime process is still running' }
$copyLogArgument = '/LOG:' + (Join-Path $record 'copy.log')
& robocopy.exe $source $target /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /NP /NFL /NDL $copyLogArgument | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Node copy failed' }
if (!(Test-Path -LiteralPath (Join-Path $target 'node.exe'))) { throw 'Target executable missing' }
if ((Get-FileHash -LiteralPath (Join-Path $target 'node.exe')).Hash -ne $hashBefore) { throw 'Node executable hash changed' }
foreach ($entry in $fileRecords) {
    $content = [IO.File]::ReadAllText($entry.path)
    $updated = $content.Replace('E:\Program\node', $target).Replace('E:\\Program\\node', 'E:\\Program\\Development\\node').Replace('E:/Program/node', 'E:/Program/Development/node')
    [IO.File]::WriteAllText($entry.path, $updated, [Text.UTF8Encoding]::new($false))
    $entry.after_sha256 = (Get-FileHash -LiteralPath $entry.path).Hash
}
$machineUpdated = $false
$machineError = $null
try {
    [Environment]::SetEnvironmentVariable('Path', $envBefore.MachinePath.Replace($source, $target), 'Machine')
    $machineUpdated = $true
} catch { $machineError = $_.Exception.Message }
$userParts = @($envBefore.UserPath -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ine $source -and $_.TrimEnd('\') -ine $target })
[Environment]::SetEnvironmentVariable('Path', (@($target) + $userParts -join ';'), 'User')
$env:Path = $env:Path.Replace($source, $target)
$report = @{
    migrated_at=(Get-Date).ToString('o'); source=$source; target=$target
    node_sha256=$hashBefore; stopped_processes=$processRecords
    changed_files=$fileRecords; machine_path_updated=$machineUpdated; machine_path_error=$machineError
    user_path_updated=$true; backup_directory=$record
    mode='copied_pending_admin_cleanup'; source_cleanup_pending=$true
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $record 'migration.json') -Encoding utf8
if (!$machineUpdated) {
    @(
        '$ErrorActionPreference = ''Stop''',
        '$current = [Environment]::GetEnvironmentVariable(''Path'', ''Machine'')',
        '[Environment]::SetEnvironmentVariable(''Path'', $current.Replace(''E:\Program\node'', ''E:\Program\Development\node''), ''Machine'')',
        'Write-Output ''Machine PATH updated.'''
    ) | Set-Content -LiteralPath (Join-Path $record 'Update-MachinePath-AsAdmin.ps1') -Encoding utf8
}
& (Join-Path $target 'node.exe') --version
& (Join-Path $target 'npm.cmd') --version
& (Join-Path $target 'npx.cmd') --version
& 'E:\Program\Development\pnpm\pnpm.cmd' --version
& 'E:\Program\DeepSeekDsh\dsh.cmd' --version
[pscustomobject]@{record=$record; stopped_process_count=$running.Count; updated_files=$fileRecords.Count; machine_path_updated=$machineUpdated; machine_path_error=$machineError; old_directory_exists=(Test-Path -LiteralPath $source)} | ConvertTo-Json
