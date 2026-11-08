# Records a video of the Shield Expanded 1.7 port in combat. The game runs windowed, and the validation mod saves the
# game's own frame every few ticks (ShieldValidation\CombatDemo.cs). This script runs the game, then turns the frames into
# combat.mp4. Each frame lasts the game time it covers, so the video runs at game speed. Output: .artifacts\combat\<timestamp>\.
# The wiring follows Invoke-Scenarios.ps1.
param(
    [string]$RimWorldDirectory = 'C:\SteamLibrary\steamapps\common\RimWorld',
    [int]$TimeoutSeconds = 300,
    [int]$WindowWidth = 1280,
    [int]$WindowHeight = 720,
    [int]$OutputFps = 30
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShieldDemoWindow {
    public static readonly IntPtr TopMost = new IntPtr(-1);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$portFolder = Join-Path $repo 'RimElShrine.Shields'
$validationFolder = Join-Path $PSScriptRoot 'ShieldValidation'
$portId = 'foxcosmic.rimelshrine.shields17'
$validationId = 'local.shields17.validation'
$game = (Resolve-Path -LiteralPath $RimWorldDirectory).Path
$exe = Join-Path $game 'RimWorld.exe'
$ffmpegExe = (Get-Command ffmpeg -ErrorAction Stop).Source

if (Get-Process -Name RimWorld -ErrorAction SilentlyContinue) { throw 'Close the running RimWorld process before a combat recording.' }

$build = & dotnet build (Join-Path $validationFolder 'ShieldValidation.csproj') -c Release --nologo 2>&1
if ($LASTEXITCODE -ne 0) { $build; throw 'The validation mod does not build.' }
foreach ($file in @((Join-Path $portFolder '1.7\Assemblies\RimElShrine.Shields.dll'), (Join-Path $portFolder '1.7\Assemblies\RimElShrine.dll'), (Join-Path $validationFolder 'Assemblies\ShieldValidation.dll'), (Join-Path $validationFolder 'About\About.xml'))) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing $file" }
}

$version = (Get-Content -LiteralPath (Join-Path $game 'Version.txt') -TotalCount 1).Trim()
$run = Join-Path $repo ('.artifacts\combat\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$video = Join-Path $run 'combat.mp4'
$framesFolder = Join-Path $run 'combat-frames'
# The game writes its log to this shared path, and the run copies it after the game exits.
$log = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log'
$config = Join-Path $run 'Config'
New-Item -ItemType Directory -Path $config -Force | Out-Null

$modsConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>$version</version>
  <activeMods>
    <li>ludeon.rimworld</li>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.biotech</li>
    <li>brrainz.harmony</li>
    <li>$portId</li>
    <li>$validationId</li>
  </activeMods>
  <knownExpansions>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.anomaly</li>
    <li>ludeon.rimworld.odyssey</li>
  </knownExpansions>
</ModsConfigData>
"@
Set-Content -LiteralPath (Join-Path $config 'ModsConfig.xml') -Value $modsConfig -Encoding UTF8
# A windowed game of a fixed size. The learning helper is off, so its panel stays out of the video.
$prefs = @"
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <langFolderName>English</langFolderName>
  <screenWidth>$WindowWidth</screenWidth>
  <screenHeight>$WindowHeight</screenHeight>
  <fullscreen>False</fullscreen>
  <exclusiveFullscreen>False</exclusiveFullscreen>
  <adaptiveTrainingEnabled>False</adaptiveTrainingEnabled>
</PrefsData>
"@
Set-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Value $prefs -Encoding UTF8

# Both mods reach the game through junctions. rmdir removes only each link, never the target's files.
$portLink = Join-Path $game 'Mods\ShieldExpanded17-combat'
$validationLink = Join-Path $game 'Mods\ShieldValidation-combat'
foreach ($link in @($portLink, $validationLink)) {
    if (Test-Path -LiteralPath $link) { throw "Mod link already exists: $link. Remove it first." }
}
New-Item -ItemType Junction -Path $portLink -Target $portFolder | Out-Null
New-Item -ItemType Junction -Path $validationLink -Target $validationFolder | Out-Null

$process = $null

# True when the game log was written by this game process and contains a line that matches the pattern.
function Test-DemoMarker([string]$pattern) {
    if ($null -eq $process -or -not (Test-Path -LiteralPath $log)) { return $false }
    if ((Get-Item -LiteralPath $log).LastWriteTime -lt $process.StartTime) { return $false }
    return [bool](Select-String -LiteralPath $log -Pattern $pattern -Quiet)
}

try {
    $gameArgs = @('-NoCrashDialog', '-quicktest', ('"-savedatafolder={0}"' -f $run), '-shieldvalidation-combat')
    $process = Start-Process -FilePath $exe -WorkingDirectory $game -ArgumentList $gameArgs -PassThru

    # Wait for the game window and its title.
    $handle = [IntPtr]::Zero
    $title = ''
    $windowLimit = (Get-Date).AddSeconds(120)
    while (((Get-Date) -lt $windowLimit) -and (-not $process.HasExited) -and (($handle -eq [IntPtr]::Zero) -or ($title -eq ''))) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        $handle = $process.MainWindowHandle
        $title = $process.MainWindowTitle
    }
    if ($handle -eq [IntPtr]::Zero -or $title -eq '') { throw 'The game window did not appear.' }
    # Keep the game on top and focused. An unfocused Unity window can throttle its rendering.
    [void][ShieldDemoWindow]::SetWindowPos($handle, [ShieldDemoWindow]::TopMost, 0, 0, 0, 0, 0x0001)
    [void][ShieldDemoWindow]::ShowWindow($handle, 9)
    $shell = New-Object -ComObject WScript.Shell
    [void]$shell.AppActivate($process.Id)
    [void][ShieldDemoWindow]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 500
    Write-Output ("game window has focus: " + ([ShieldDemoWindow]::GetForegroundWindow() -eq $handle))

    # The scene runs until its done marker. A failed scene also writes that marker, so its frames still exist.
    $limit = (Get-Date).AddSeconds($TimeoutSeconds)
    while (((Get-Date) -lt $limit) -and (-not $process.HasExited) -and -not (Test-DemoMarker '\[ShieldCombat\] done\s*$')) {
        Start-Sleep -Seconds 1
    }
    if ($process.HasExited -and -not (Test-DemoMarker '\[ShieldCombat\] done\s*$')) { throw 'The game exited before the scene finished.' }
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $run 'Player.log') -Force }
    cmd /c rmdir $portLink | Out-Null
    cmd /c rmdir $validationLink | Out-Null
}

# Turn the frames into the video. Each frame lasts from its tick to the next frame's tick, at 60 ticks per second.
$frames = @(Get-ChildItem -LiteralPath $framesFolder -Filter 'f_*.jpg' -ErrorAction Stop | Sort-Object Name)
if ($frames.Count -lt 2) { throw "Only $($frames.Count) frames were saved in $framesFolder" }
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$list = Join-Path $run 'frames.txt'
$entries = @()
for ($i = 0; $i -lt $frames.Count; $i++) {
    $tick = [int]$frames[$i].BaseName.Substring(2)
    $nextTick = if ($i + 1 -lt $frames.Count) { [int]$frames[$i + 1].BaseName.Substring(2) } else { $tick + 6 }
    $seconds = ($nextTick - $tick) / 60.0
    $entries += "file '$($frames[$i].FullName)'"
    $entries += 'duration ' + $seconds.ToString('0.0000', $invariant)
}
# The concat demuxer needs the last frame once more, without a duration.
$entries += "file '$($frames[-1].FullName)'"
Set-Content -LiteralPath $list -Value $entries -Encoding ASCII
& $ffmpegExe -hide_banner -loglevel warning -f concat -safe 0 -i $list -vf "fps=$OutputFps,format=yuv420p" -c:v libx264 -preset veryfast -crf 20 -y $video
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg could not write the video.' }

$firstTick = [int]$frames[0].BaseName.Substring(2)
$lastTick = [int]$frames[-1].BaseName.Substring(2)
$copy = Join-Path $run 'Player.log'
if (-not (Test-Path -LiteralPath $copy)) { throw "No game log was copied into $run" }
$lines = @(Get-Content -LiteralPath $copy)
$demo = @($lines | Where-Object { $_ -match '\[ShieldCombat\]' })
$errors = @($lines | Where-Object { $_ -match ' ERROR ' })
$videoWritten = (Test-Path -LiteralPath $video) -and ((Get-Item -LiteralPath $video).Length -gt 0)
$summary = @(
    "run: $run",
    "video: $video",
    "video written: $videoWritten",
    "frames: $($frames.Count), game seconds covered: $((($lastTick - $firstTick) / 60.0).ToString('0.0', $invariant))",
    "game version: $version",
    "ERROR lines in the log: $($errors.Count)"
) + $demo
$summary | Set-Content -LiteralPath (Join-Path $run 'summary.txt') -Encoding UTF8
$summary
