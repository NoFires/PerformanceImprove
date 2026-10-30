param(
    [Parameter(Mandatory=$true)][string]$Out,
    [int]$PollMs = 8,
    [int]$Seconds = 0,
    [string]$StopFile = ''
)

# Records global keyboard and mouse-button transitions while the game runs, so a test
# sequence can be replayed in batch with scripts/ingame-input-player.ps1.
# Polling with GetAsyncKeyState sees both physical input and SendInput-style injection,
# which is what ingame-driver.ps1 uses.

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class InputProbe {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
$null = [InputProbe]::SetProcessDPIAware()

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }

$names = @{
    1='LButton';2='RButton';4='MButton';8='Back';9='Tab';13='Enter';16='Shift';17='Control';18='Alt';
    19='Pause';20='CapsLock';27='Escape';32='Space';33='PageUp';34='PageDown';35='End';36='Home';
    37='Left';38='Up';39='Right';40='Down';44='PrintScreen';45='Insert';46='Delete';
    48='0';49='1';50='2';51='3';52='4';53='5';54='6';55='7';56='8';57='9';
    65='A';66='B';67='C';68='D';69='E';70='F';71='G';72='H';73='I';74='J';75='K';76='L';77='M';
    78='N';79='O';80='P';81='Q';82='R';83='S';84='T';85='U';86='V';87='W';88='X';89='Y';90='Z';
    91='LWin';92='RWin';93='Apps';96='Num0';97='Num1';98='Num2';99='Num3';100='Num4';101='Num5';
    102='Num6';103='Num7';104='Num8';105='Num9';106='Multiply';107='Add';109='Subtract';110='Decimal';
    111='Divide';112='F1';113='F2';114='F3';115='F4';116='F5';117='F6';118='F7';119='F8';120='F9';
    121='F10';122='F11';123='F12';144='NumLock';145='ScrollLock';160='LShift';161='RShift';
    162='LControl';163='RControl';164='LAlt';165='RAlt';186='Semicolon';187='Equals';188='Comma';
    189='Minus';190='Period';191='Slash';192='Tilde';219='LBracket';220='Backslash';221='RBracket';
    222='Quote';226='OEM102'
}
$watched = @(1,2,4,8,9,13,16,17,18,19,20,27,32,33,34,35,36,37,38,39,40,44,45,46) +
    @(48..57) + @(65..90) + @(91,92,93) + @(96..111) + @(112..123) + @(144,145) +
    @(160..165) + @(186..192) + @(219,220,221,222,226)

$script:count = 0
$script:prev = @{}
$script:sw = [System.Diagnostics.Stopwatch]::StartNew()
$deadline = if ($Seconds -gt 0) { $sw.Elapsed.TotalSeconds + $Seconds } else { [double]::MaxValue }

$writer = New-Object System.IO.StreamWriter($Out, $false, [System.Text.UTF8Encoding]::new($false))
$writer.AutoFlush = $true
$lastMove = -1000
$lastX = -1; $lastY = -1

function Emit([string]$type, [string]$name, [int]$vk, [int]$x, [int]$y, [double]$t) {
    $script:count++
    $line = '{"t":' + [math]::Round($t, 3) + ',"type":"' + $type + '"'
    if ($name) { $line += ',"name":"' + $name + '"' }
    if ($vk -ge 0) { $line += ',"vk":' + $vk }
    if ($x -ge 0) { $line += ',"x":' + $x + ',"y":' + $y }
    $line += '}'
    $writer.WriteLine($line)
}

try {
    while ($true) {
        $elapsed = $sw.Elapsed.TotalSeconds
        if ($elapsed -gt $deadline) { break }
        if ($StopFile -and (Test-Path -LiteralPath $StopFile)) { break }
        foreach ($vk in $watched) {
            $down = ([InputProbe]::GetAsyncKeyState($vk) -band 0x8000) -ne 0
            $was = $script:prev[$vk]
            if ($down -and -not $was) {
                $p = New-Object InputProbe+POINT
                $null = [InputProbe]::GetCursorPos([ref]$p)
                if ($vk -eq 1 -or $vk -eq 2 -or $vk -eq 4) {
                    $btn = if ($vk -eq 1) { 'left' } elseif ($vk -eq 2) { 'right' } else { 'middle' }
                    $script:count++
                    $writer.WriteLine('{"t":' + [math]::Round($elapsed, 3) + ',"type":"mouse","button":"' + $btn + '","down":true,"x":' + $p.X + ',"y":' + $p.Y + '}')
                } else {
                    Emit 'keydown' $names[$vk] $vk $p.X $p.Y $elapsed
                }
            } elseif (-not $down -and $was) {
                $p = New-Object InputProbe+POINT
                $null = [InputProbe]::GetCursorPos([ref]$p)
                if ($vk -eq 1 -or $vk -eq 2 -or $vk -eq 4) {
                    $btn = if ($vk -eq 1) { 'left' } elseif ($vk -eq 2) { 'right' } else { 'middle' }
                    $script:count++
                    $writer.WriteLine('{"t":' + [math]::Round($elapsed, 3) + ',"type":"mouse","button":"' + $btn + '","down":false,"x":' + $p.X + ',"y":' + $p.Y + '}')
                } else {
                    Emit 'keyup' $names[$vk] $vk $p.X $p.Y $elapsed
                }
            }
            $script:prev[$vk] = $down
        }
        $pt = New-Object InputProbe+POINT
        $null = [InputProbe]::GetCursorPos([ref]$pt)
        if ($elapsed - $lastMove -ge 1.0 -and ($pt.X -ne $lastX -or $pt.Y -ne $lastY)) {
            Emit 'move' '' -1 $pt.X $pt.Y $elapsed
            $lastMove = $elapsed
            $lastX = $pt.X; $lastY = $pt.Y
        }
        Start-Sleep -Milliseconds $PollMs
    }
}
finally {
    $writer.WriteLine('{"t":' + [math]::Round($sw.Elapsed.TotalSeconds, 3) + ',"type":"end","events":' + $script:count + '}')
    $writer.Dispose()
}
"recorded $script:count events to $Out"
