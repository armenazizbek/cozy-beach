<#
Runs smoke-test suites against one build, for picture-by-picture comparison (unity/NATIVE_PLAN.md section 4).

  powershell -File unity\Tools\Parity\run_suites.ps1 -Exe C:\TidefinderParity\ref\Tidefinder.exe -Out C:\TidefinderParity\shots\ref -Suites all
  powershell -File unity\Tools\Parity\run_suites.ps1 -Exe release\unity-win-unpacked\Tidefinder.exe -Out C:\TidefinderParity\shots\S9 -Suites core,world

Each run is its own launch: -autoshot <Out>\<run> -autoshot-seed <Seed> -autoshot-fixedstep -autoshot-scenedump, a 1280x720
window (1280x800 and -force-deck with -Deck), its own flags from suites.json, and its log in <Out>\<run>\Player.log.
Each game window opens behind your other windows and the focus goes back to where it was (-Front keeps it in front);
the runs' own mouse and keyboard work without focus. Keep the mouse off the game window if you bring it forward: the
pointer can light up whatever it rests on.
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Suites = "core",
    [int]$Seed = 1,
    [switch]$Deck,
    [switch]$NoDump,
    # leave the game windows in front instead of behind the other windows
    [switch]$Front,
    [int]$TimeoutMinutes = 8,
    [string[]]$Only,
    # extra flags for every run, e.g. -Extra "-spike-mirror" (space-separated)
    [string]$Extra = ""
)

$ErrorActionPreference = "Stop"
$exePath = (Resolve-Path $Exe).Path
$config = Get-Content -Raw (Join-Path $PSScriptRoot "suites.json") | ConvertFrom-Json
$names = if ($Suites -eq "all") { $config.suites.PSObject.Properties.Name } else { $Suites.Split(",") | ForEach-Object { $_.Trim() } }

# the runs, in order, each once
$runs = [ordered]@{}
foreach ($s in $names) {
    $suite = $config.suites.$s
    if ($null -eq $suite) { throw "No suite '$s' in suites.json" }
    foreach ($p in $suite.PSObject.Properties) {
        if (-not $runs.Contains($p.Name)) { $runs[$p.Name] = @($p.Value) }
    }
}
if ($Only) {
    # "-Only a,b" arrives as one string through powershell -File
    $keep = @($Only | ForEach-Object { $_.Split(",") } | ForEach-Object { $_.Trim() })
    foreach ($k in @($runs.Keys)) { if ($keep -notcontains $k) { $runs.Remove($k) } }
}

New-Item -ItemType Directory -Force $Out | Out-Null
$w = 1280; $h = if ($Deck) { 800 } else { 720 }
$results = @()
# fixed, silent settings for the runs; the player's own come back afterwards (TestSettings.ps1)
. (Join-Path $PSScriptRoot "TestSettings.ps1")
Use-TestSettings
try {
foreach ($name in $runs.Keys) {
    $dir = Join-Path $Out $name
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force $dir | Out-Null
    # a save to load must be in place before the run (see NATIVE_PLAN.md 4.2, suite "saves")
    $argv = @("-autoshot", $dir, "-autoshot-seed", "$Seed", "-autoshot-fixedstep",
              "-screen-width", "$w", "-screen-height", "$h", "-screen-fullscreen", "0",
              "-logFile", (Join-Path $dir "Player.log"))
    if (-not $NoDump) { $argv += "-autoshot-scenedump" }
    if ($Deck) { $argv += "-force-deck" }
    $argv += $runs[$name]
    if ($Extra) { $argv += $Extra.Split(" ", [System.StringSplitOptions]::RemoveEmptyEntries) }
    $quoted = $argv | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $t0 = Get-Date
    Write-Host ("[{0}] {1} {2}" -f $t0.ToString("HH:mm:ss"), $name, ($runs[$name] -join " "))
    $focusWas = Get-FrontWindow
    $p = Start-Process -FilePath $exePath -ArgumentList $quoted -PassThru
    if (-not $Front) { Hide-GameWindow $p $focusWas }
    if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        Stop-Process -Id $p.Id -Force
        $code = "timeout"
    } else { $code = $p.ExitCode }
    $secs = [int]((Get-Date) - $t0).TotalSeconds
    $shots = @(Get-ChildItem $dir -Filter *.png -ErrorAction SilentlyContinue).Count
    Write-Host ("    exit {0}, {1} s, {2} shots" -f $code, $secs, $shots)
    $results += [pscustomobject]@{ run = $name; flags = ($runs[$name] -join " "); exit = "$code"; seconds = $secs; shots = $shots }
}
}
finally { Restore-PlayerSettings }
$results | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $Out "runs.json")
$bad = @($results | Where-Object { $_.exit -ne "0" })
if ($bad.Count -gt 0) {
    Write-Host ("{0} run(s) did not exit cleanly: {1}" -f $bad.Count, (($bad | ForEach-Object { $_.run + "=" + $_.exit }) -join ", "))
    exit 1
}
Write-Host "All $($results.Count) runs exited cleanly."
