param(
    [Parameter(Mandatory=$true)][ValidateSet('shot','click','move','key','activate','info','wait-window')][string]$Action,
    [string]$Out,
    [int]$X = -1,
    [int]$Y = -1,
    [string]$Key = '',
    [int]$TimeoutSeconds = 60
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class W32 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
$null = [W32]::SetProcessDPIAware()
$script:SW_RESTORE = 9
$script:MOUSEEVENTF_LEFTDOWN = 0x0002
$script:MOUSEEVENTF_LEFTUP = 0x0004

function Get-GameProcess {
    Get-Process -Name 'Wrath' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}
function Get-GameWindow {
    $p = Get-GameProcess
    if (-not $p) { return $null }
    $h = [IntPtr]$p.MainWindowHandle
    $r = New-Object W32+RECT
    $null = [W32]::GetWindowRect($h, [ref]$r)
    [pscustomobject]@{ Handle = $h; Left = $r.Left; Top = $r.Top; Width = $r.Right - $r.Left; Height = $r.Bottom - $r.Top; Title = $p.MainWindowTitle; Pid = $p.Id }
}
function Activate-Game {
    $w = Get-GameWindow
    if (-not $w) { throw 'game window not found' }
    if ([W32]::IsIconic($w.Handle)) { $null = [W32]::ShowWindow($w.Handle, $script:SW_RESTORE) }
    $null = [W32]::SetForegroundWindow($w.Handle)
    Start-Sleep -Milliseconds 400
    $w
}
function Save-Shot([string]$path) {
    $w = Get-GameWindow
    if (-not $w) { throw 'game window not found' }
    $dir = Split-Path -Parent $path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $bmp = New-Object System.Drawing.Bitmap($w.Width, $w.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($w.Left, $w.Top, 0, 0, $bmp.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $path
}

switch ($Action) {
    'info' {
        Get-GameWindow | Format-List
        "screen: $([W32]::GetSystemMetrics(0)) x $([W32]::GetSystemMetrics(1))"
        "foreground: $([W32]::GetForegroundWindow())"
    }
    'activate' { Activate-Game | Format-List }
    'wait-window' {
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            $w = Get-GameWindow
            if ($w -and $w.Width -gt 200) { $w | Format-List; exit 0 }
            Start-Sleep -Milliseconds 500
        }
        'timeout waiting for game window'; exit 1
    }
    'shot' { Save-Shot $Out }
    'move' {
        $w = Activate-Game
        $null = [W32]::SetCursorPos($w.Left + $X, $w.Top + $Y)
        "moved to $($w.Left + $X),$($w.Top + $Y)"
    }
    'click' {
        $w = Activate-Game
        $null = [W32]::SetCursorPos($w.Left + $X, $w.Top + $Y)
        Start-Sleep -Milliseconds 120
        [W32]::mouse_event($script:MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        [W32]::mouse_event($script:MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 250
        "clicked $($w.Left + $X),$($w.Top + $Y)"
    }
    'key' {
        $null = Activate-Game
        [System.Windows.Forms.SendKeys]::SendWait($Key)
        "sent key $Key"
    }
}
