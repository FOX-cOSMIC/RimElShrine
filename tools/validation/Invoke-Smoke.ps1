# Smoke run for the Shield Expanded 1.7 build. Starts RimWorld in a new colony with an isolated save folder,
# then reads the game log. Rules: tools/validation/README.md. Output: .artifacts/runs/<timestamp>/.
param(
    [string]$RimWorldDirectory = 'C:\SteamLibrary\steamapps\common\RimWorld',
    [int]$ColonyWaitSeconds = 60,
    [int]$TimeoutSeconds = 240
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$modFolder = Join-Path $repo 'RimElShrine.Shields'
$packageId = 'foxcosmic.rimelshrine.shields17'
$game = (Resolve-Path -LiteralPath $RimWorldDirectory).Path
$exe = Join-Path $game 'RimWorld.exe'
$log = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log'

if (Get-Process -Name RimWorld -ErrorAction SilentlyContinue) { throw 'Close the running RimWorld process before a smoke run.' }

$about = Join-Path $modFolder 'About\About.xml'
foreach ($file in @((Join-Path $modFolder '1.7\Assemblies\RimElShrine.Shields.dll'), (Join-Path $modFolder '1.7\Assemblies\RimElShrine.dll'), $about)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing $file. Build the 1.7 version first." }
}
if (-not (Select-String -LiteralPath $about -Pattern ('<packageId>' + [regex]::Escape($packageId) + '</packageId>') -Quiet)) {
    throw "About.xml does not carry the packageId $packageId."
}

$version = (Get-Content -LiteralPath (Join-Path $game 'Version.txt') -TotalCount 1).Trim()
$run = Join-Path $repo ('.artifacts\runs\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
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
    <li>$packageId</li>
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
$prefs = @'
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <langFolderName>English</langFolderName>
</PrefsData>
'@
Set-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Value $prefs -Encoding UTF8

# The mod reaches the game through a junction. rmdir removes only the link, never the target's files.
$link = Join-Path $game 'Mods\ShieldExpanded17-smoke'
if (Test-Path -LiteralPath $link) { throw "Mod link already exists: $link. Remove it first." }
New-Item -ItemType Junction -Path $link -Target $modFolder | Out-Null

$process = $null
try {
    $gameArgs = @('-NoCrashDialog', '-quicktest', ('"-savedatafolder={0}"' -f $run), '-Headless')
    $process = Start-Process -FilePath $exe -WorkingDirectory $game -ArgumentList $gameArgs -WindowStyle Hidden -PassThru
    $limit = (Get-Date).AddSeconds($TimeoutSeconds)
    $colonyAt = $null
    while (-not $process.HasExited -and (Get-Date) -lt $limit) {
        Start-Sleep -Seconds 5
        if (-not $colonyAt -and (Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -Pattern 'Initializing new game with mods:' -Quiet)) { $colonyAt = Get-Date }
        if ($colonyAt -and ((Get-Date) - $colonyAt).TotalSeconds -ge $ColonyWaitSeconds) { break }
    }
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 3
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $run 'Player.log') -Force }
    cmd /c rmdir $link | Out-Null
}

$copy = Join-Path $run 'Player.log'
if (-not (Test-Path -LiteralPath $copy)) { throw "No game log was copied into $run" }
$lines = @(Get-Content -LiteralPath $copy)
$header = -1
for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match 'Initializing new game with mods:') { $header = $i; break } }
$colony = $header -ge 0
$listed = $false
$note = ''
if ($colony) {
    for ($j = $header + 1; $j -lt $lines.Count; $j++) {
        $item = [regex]::Match($lines[$j], '^\s+-\s+(\S+)(.*)$')
        if (-not $item.Success) { break }
        if ($item.Groups[1].Value -eq $packageId) { $listed = $true; $note = $item.Groups[2].Value.Trim() }
    }
}
$errors = @($lines | Where-Object { $_ -match ' ERROR ' })
$ours = @($errors | Where-Object { $_ -match 'RimElShrine|rimelshrine|Instance_ModSettings_Write|Static_LoadedModManager|ShieldNet|ShieldGenerator|LimbsRegen|MechSerum' })
$problems = @()
if (-not $colony) { $problems += 'the game never started a new colony' }
if (-not $listed) { $problems += "$packageId is not in the new-game mod list" }
elseif ($note -ne '') { $problems += "$packageId is listed but not loaded: $note" }
if ($ours.Count -gt 0) { $problems += "$($ours.Count) error line(s) mention the port" }
$verdict = if ($problems.Count -eq 0) { 'PASS' } else { 'FAIL' }
$summary = @(
    "verdict: $verdict",
    "run: $run",
    "game version: $version",
    "new colony started: $colony",
    "mod listed and loaded: $($listed -and $note -eq '')",
    "ERROR lines in the log: $($errors.Count)",
    "ERROR lines that mention the port: $($ours.Count)"
) + ($problems | ForEach-Object { "problem: $_" })
$summary | Set-Content -LiteralPath (Join-Path $run 'summary.txt') -Encoding UTF8
$summary
if ($verdict -ne 'PASS') { exit 1 }
