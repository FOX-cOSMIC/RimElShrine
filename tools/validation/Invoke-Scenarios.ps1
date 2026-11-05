# Scenario run: loads the 1.7 port and the validation mod in a new colony, waits for the scenarios to finish,
# then checks the results. Rules: tools/validation/README.md. Output: .artifacts/runs/<timestamp>/.
param(
    [string]$RimWorldDirectory = 'C:\SteamLibrary\steamapps\common\RimWorld',
    [int]$TimeoutSeconds = 300,
    # Extra game arguments, used by control runs. Example: -ExtraGameArgs '-shieldvalidation-nopower'
    [string[]]$ExtraGameArgs = @()
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$portFolder = Join-Path $repo 'RimElShrine.Shields'
$validationFolder = Join-Path $PSScriptRoot 'ShieldValidation'
$portId = 'foxcosmic.rimelshrine.shields17'
$validationId = 'local.shields17.validation'
$required = @(
    'network: parts join one shield network',
    'network: terminal allocation reaches the network',
    'network: no power, no upgrade value',
    'equipment: colonist wears the ES belt',
    'equipment: ES belt absorbs a ranged hit and loses energy',
    'equipment: ES belt with no energy lets a hit through',
    'equipment: ES belt recharges while worn'
)
# Each group writes one completion row when it ends. The run is complete when every group has written its row.
$groups = @('network group complete', 'equipment group complete')
$game = (Resolve-Path -LiteralPath $RimWorldDirectory).Path
$exe = Join-Path $game 'RimWorld.exe'
$log = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log'

if (Get-Process -Name RimWorld -ErrorAction SilentlyContinue) { throw 'Close the running RimWorld process before a scenario run.' }

$build = & dotnet build (Join-Path $validationFolder 'ShieldValidation.csproj') -c Release --nologo 2>&1
if ($LASTEXITCODE -ne 0) { $build; throw 'The validation mod does not build.' }

foreach ($file in @((Join-Path $portFolder '1.7\Assemblies\RimElShrine.Shields.dll'), (Join-Path $portFolder '1.7\Assemblies\RimElShrine.dll'), (Join-Path $validationFolder 'Assemblies\ShieldValidation.dll'), (Join-Path $validationFolder 'About\About.xml'))) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing $file" }
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
$prefs = @'
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <langFolderName>English</langFolderName>
</PrefsData>
'@
Set-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Value $prefs -Encoding UTF8

# Both mods reach the game through junctions. rmdir removes only each link, never the target's files.
$portLink = Join-Path $game 'Mods\ShieldExpanded17-scenarios'
$validationLink = Join-Path $game 'Mods\ShieldValidation-scenarios'
foreach ($link in @($portLink, $validationLink)) {
    if (Test-Path -LiteralPath $link) { throw "Mod link already exists: $link. Remove it first." }
}
New-Item -ItemType Junction -Path $portLink -Target $portFolder | Out-Null
New-Item -ItemType Junction -Path $validationLink -Target $validationFolder | Out-Null

$results = Join-Path $run 'validation-results.txt'
$process = $null
try {
    $gameArgs = @('-NoCrashDialog', '-quicktest', ('"-savedatafolder={0}"' -f $run), '-Headless', '-shieldvalidation') + $ExtraGameArgs
    $process = Start-Process -FilePath $exe -WorkingDirectory $game -ArgumentList $gameArgs -WindowStyle Hidden -PassThru
    $limit = (Get-Date).AddSeconds($TimeoutSeconds)
    $completeAt = $null
    while (-not $process.HasExited -and (Get-Date) -lt $limit) {
        Start-Sleep -Seconds 2
        $finishedGroups = if (Test-Path -LiteralPath $results) { @($groups | Where-Object { Select-String -LiteralPath $results -Pattern " | $_ | " -SimpleMatch -Quiet }).Count } else { 0 }
        if ($finishedGroups -eq $groups.Count) {
            # Stop only after the game has flushed every result marker to Player.log, or after 20 seconds.
            if (-not $completeAt) { $completeAt = Get-Date }
            $rowCount = @(Get-Content -LiteralPath $results).Count
            $markerCount = if (Test-Path -LiteralPath $log) { @(Select-String -LiteralPath $log -Pattern '[ShieldValidation] ' -SimpleMatch).Count } else { 0 }
            if ($markerCount -ge $rowCount -or ((Get-Date) - $completeAt).TotalSeconds -ge 20) { break }
        }
    }
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 3
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $run 'Player.log') -Force }
    cmd /c rmdir $portLink | Out-Null
    cmd /c rmdir $validationLink | Out-Null
}

$copy = Join-Path $run 'Player.log'
if (-not (Test-Path -LiteralPath $copy)) { throw "No game log was copied into $run" }
$lines = @(Get-Content -LiteralPath $copy)
$rows = if (Test-Path -LiteralPath $results) { @(Get-Content -LiteralPath $results) } else { @() }

$problems = @()
foreach ($group in $groups) {
    $done = @($rows | Where-Object { ($_ -split ' \| ', 3)[1] -eq $group })
    if ($done.Count -ne 1) { $problems += "group row missing or repeated: $group"; continue }
    if (-not $done[0].EndsWith('| finished')) { $problems += "$group -> " + ($done[0] -split ' \| ', 3)[2] }
}
foreach ($name in $required) {
    $row = @($rows | Where-Object { ($_ -split ' \| ', 3)[1] -eq $name })
    if ($row.Count -ne 1) { $problems += "scenario row missing or repeated: $name"; continue }
    $result = ($row[0] -split ' \| ', 3)[2]
    if (-not $result.StartsWith('PASS')) { $problems += "$name -> $result" }
}
$marks = @($lines | Where-Object { $_ -match '\[ShieldValidation\] ' })
if ($marks.Count -ne $rows.Count) { $problems += "results file has $($rows.Count) rows but the log has $($marks.Count) markers" }

$header = -1
for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match 'Initializing new game with mods:') { $header = $i; break } }
$listed = @{ port = $false; validation = $false }
$notes = @()
if ($header -ge 0) {
    for ($j = $header + 1; $j -lt $lines.Count; $j++) {
        $item = [regex]::Match($lines[$j], '^\s+-\s+(\S+)(.*)$')
        if (-not $item.Success) { break }
        if ($item.Groups[1].Value -eq $portId) { $listed.port = $true; if ($item.Groups[2].Value.Trim()) { $notes += $item.Groups[2].Value.Trim() } }
        if ($item.Groups[1].Value -eq $validationId) { $listed.validation = $true; if ($item.Groups[2].Value.Trim()) { $notes += $item.Groups[2].Value.Trim() } }
    }
}
if (-not $listed.port -or -not $listed.validation) { $problems += 'the port or the validation mod is not in the new-game mod list' }
if ($notes.Count -gt 0) { $problems += 'a mod is listed with a note: ' + ($notes -join '; ') }

$errors = @($lines | Where-Object { $_ -match ' ERROR ' })
$ours = @($errors | Where-Object { $_ -match 'RimElShrine|rimelshrine|ShieldValidation|ShieldNet|ShieldGenerator|LimbsRegen|MechSerum|Instance_ModSettings_Write|Static_LoadedModManager' })
if ($ours.Count -gt 0) { $problems += "$($ours.Count) error line(s) mention the port or the validation mod" }

$verdict = if ($problems.Count -eq 0) { 'PASS' } else { 'FAIL' }
$summary = @(
    "verdict: $verdict",
    "run: $run",
    "game version: $version",
    "scenario rows: $($rows.Count)",
    "ERROR lines in the log: $($errors.Count)",
    "ERROR lines that mention the port or validation: $($ours.Count)"
) + ($rows | ForEach-Object { "row: $_" }) + ($problems | ForEach-Object { "problem: $_" })
$summary | Set-Content -LiteralPath (Join-Path $run 'summary.txt') -Encoding UTF8
$summary
if ($verdict -ne 'PASS') { exit 1 }
