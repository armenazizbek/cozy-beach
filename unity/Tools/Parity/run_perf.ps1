<#
Measures two builds against each other (unity/NATIVE_PLAN.md 4.3): the -autoshot-perf tour with VSync off, in real time,
taking turns (reference, branch, reference, branch, ...) so the PC's state weighs on both the same way.

  powershell -File unity\Tools\Parity\run_perf.ps1 -Ref C:\TidefinderParity\ref -Branch release\unity-win-unpacked -Rounds 3 -Out C:\TidefinderParity\perf\S9
  python unity\Tools\Parity\compare.py --perf C:\TidefinderParity\perf\S9

Use IL2CPP builds (npm run dist:unity) for numbers that mean anything.
#>
param(
    [Parameter(Mandatory = $true)][string]$Ref,
    [Parameter(Mandatory = $true)][string]$Branch,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$Rounds = 3,
    [switch]$Deck,
    [int]$TimeoutMinutes = 10
)

$ErrorActionPreference = "Stop"
$builds = [ordered]@{ ref = (Resolve-Path (Join-Path $Ref "Tidefinder.exe")).Path; branch = (Resolve-Path (Join-Path $Branch "Tidefinder.exe")).Path }
New-Item -ItemType Directory -Force $Out | Out-Null
$w = 1280; $h = if ($Deck) { 800 } else { 720 }
$failed = 0
# fixed, silent settings for the runs; the player's own come back afterwards (TestSettings.ps1)
. (Join-Path $PSScriptRoot "TestSettings.ps1")
Use-TestSettings
try {
for ($i = 1; $i -le $Rounds; $i++) {
    foreach ($which in $builds.Keys) {
        $dir = Join-Path $Out ("{0}_{1}" -f $which, $i)
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        New-Item -ItemType Directory -Force $dir | Out-Null
        $argv = @("-autoshot", $dir, "-autoshot-perf", "-autoshot-novsync",
                  "-screen-width", "$w", "-screen-height", "$h", "-screen-fullscreen", "0",
                  "-logFile", (Join-Path $dir "Player.log"))
        if ($Deck) { $argv += "-force-deck" }
        Write-Host ("[{0}] round {1}: {2}" -f (Get-Date).ToString("HH:mm:ss"), $i, $which)
        $p = Start-Process -FilePath $builds[$which] -ArgumentList $argv -PassThru
        if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) { Stop-Process -Id $p.Id -Force; Write-Host "    timeout"; $failed++ }
        elseif ($p.ExitCode -ne 0) { Write-Host "    exit $($p.ExitCode)"; $failed++ }
    }
}
}
finally { Restore-PlayerSettings }
if ($failed -gt 0) { Write-Host "$failed perf run(s) failed"; exit 1 }
Write-Host "Done: $Rounds rounds in $Out"
