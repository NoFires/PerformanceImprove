$ErrorActionPreference = 'Stop'
$Workspace = Split-Path -Parent $PSScriptRoot
$Install = 'E:\Program\MCP\wrath-analysis'
$Python = Join-Path $Install '.venv\Scripts\python.exe'
$env:PIP_CACHE_DIR = 'E:\Program\Caches\pip'
New-Item -ItemType Directory -Force -Path $Install | Out-Null
if (!(Test-Path -LiteralPath $Python)) { python -m venv (Join-Path $Install '.venv') }
& $Python -m pip install -r (Join-Path $Workspace 'mcp\requirements.lock.txt') --disable-pip-version-check
if ($LASTEXITCODE -ne 0) { throw 'MCP dependency installation failed' }
Copy-Item -LiteralPath (Join-Path $Workspace 'mcp\server.py') -Destination (Join-Path $Install 'server.py')
$BackupDir = 'E:\Program\MCP\backups'
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
$CodexConfig = Join-Path $env:USERPROFILE '.codex\config.toml'
if (Test-Path -LiteralPath $CodexConfig) {
    Copy-Item -LiteralPath $CodexConfig -Destination (Join-Path $BackupDir ('codex-config-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.toml'))
}
& codex mcp add wrath-analysis --env "WRATH_WORKSPACE=$Workspace" -- $Python -u (Join-Path $Install 'server.py')
if ($LASTEXITCODE -ne 0) { throw 'Codex MCP registration failed' }
& $Python (Join-Path $Workspace 'scripts\test_mcp.py')
if ($LASTEXITCODE -ne 0) { throw 'MCP integration verification failed' }
