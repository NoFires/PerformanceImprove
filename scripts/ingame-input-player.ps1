param(
    [Parameter(Mandatory=$true)][string]$In,
    [double]$Speed = 1.0,
    [switch]$SkipActivate,
    [int]$MaxEvents = 0
)

# Replays a recording made by scripts/ingame-input-recorder.ps1.
# Keyboard and mouse events are injected with keybd_event / mouse_event, which the
# Unity game reads the same way as the ingame-driver.ps1 clicks.

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class InputReplay {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
}
'@
$null = [InputReplay]::SetProcessDPIAware()
if (-not $SkipActivate) {
    $h = [InputReplay]::FindWindow($null, 'Pathfinder Wrath Of The Righteous')
    if ($h -ne [IntPtr]::Zero) { $null = [InputReplay]::ShowWindow($h, 9); $null = [InputReplay]::SetForegroundWindow($h); Start-Sleep -Milliseconds 500 }
}
$KEYEVENTF_KEYUP = 0x0002
$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004
$MOUSEEVENTF_RIGHTDOWN = 0x0008
$MOUSEEVENTF_RIGHTUP = 0x0010
$MOUSEEVENTF_MIDDLEDOWN = 0x0020
$MOUSEEVENTF_MIDDLEUP = 0x0040

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$count = 0
foreach ($line in Get-Content -LiteralPath $In) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $e = $line | ConvertFrom-Json
    if ($e.type -eq 'end') { break }
    if ($MaxEvents -gt 0 -and $count -ge $MaxEvents) { break }
    $target = [double]$e.t / [Math]::Max(0.01, $Speed)
    while ($sw.Elapsed.TotalSeconds -lt $target) { Start-Sleep -Milliseconds 2 }
    switch ($e.type) {
        'move' { $null = [InputReplay]::SetCursorPos([int]$e.x, [int]$e.y) }
        'keydown' { [InputReplay]::keybd_event([byte]$e.vk, 0, 0, [UIntPtr]::Zero) }
        'keyup' { [InputReplay]::keybd_event([byte]$e.vk, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero) }
        'mouse' {
            if ($e.x -ge 0) { $null = [InputReplay]::SetCursorPos([int]$e.x, [int]$e.y) }
            switch ($e.button) {
                'left' { if ($e.down) { [InputReplay]::mouse_event($MOUSEEVENTF_LEFTDOWN,0,0,0,[UIntPtr]::Zero) } else { [InputReplay]::mouse_event($MOUSEEVENTF_LEFTUP,0,0,0,[UIntPtr]::Zero) } }
                'right' { if ($e.down) { [InputReplay]::mouse_event($MOUSEEVENTF_RIGHTDOWN,0,0,0,[UIntPtr]::Zero) } else { [InputReplay]::mouse_event($MOUSEEVENTF_RIGHTUP,0,0,0,[UIntPtr]::Zero) } }
                'middle' { if ($e.down) { [InputReplay]::mouse_event($MOUSEEVENTF_MIDDLEDOWN,0,0,0,[UIntPtr]::Zero) } else { [InputReplay]::mouse_event($MOUSEEVENTF_MIDDLEUP,0,0,0,[UIntPtr]::Zero) } }
            }
        }
    }
    $count++
}
"replayed $count events from $In"
