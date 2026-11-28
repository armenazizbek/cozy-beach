<#
Fixed game settings for test runs (unity/UI_PLAN.md section 5, U0).

The game keeps its settings in PlayerPrefs (HKCU\Software\Cozy Beach\Tidefinder), the same store for the player and for
the test runs, so a player who turned VSync off or went fullscreen changed every run after it. Before a run the scripts
back the whole key up to a file and write a fixed, silent set; afterwards the player's own key is put back exactly as it
was. A backup left by a run that was killed is restored first, so the player's settings are never lost.

Dot-source it, then:  Use-TestSettings  ...runs...  Restore-PlayerSettings   (in a finally)
#>

$script:SettingsKey = "HKCU\Software\Cozy Beach\Tidefinder"
$script:SettingsPath = "HKCU:\Software\Cozy Beach\Tidefinder"
$script:SettingsValue = "cozy-treasure-hunter-settings-v1_h1351868011"
$script:SettingsBackup = "C:\TidefinderParity\player-settings-backup.reg"
$script:SettingsHad = "C:\TidefinderParity\player-settings-backup.none"

# The game's defaults (Core/Settings.cs), except: silent, VSync on, windowed at the harness's size.
$script:TestSettingsJson = '{"master":0.0,"music":0.0,"ambience":0.0,"sensitivity":1.0,"invertMouse":false,"invertStick":false,"vibration":false,"vibrationSensitivity":1.0,"fov":50.0,"shadows":true,"sharp":true,"renderScale":1.0,"quality":"high","vsync":true,"fpsLimit":0,"uiScale":1.0,"displayMode":"windowed","windowSize":"1280x720"}'

# reg.exe reports even success on stderr, which a script running with $ErrorActionPreference = "Stop" takes for an error:
# run it with errors allowed and judge it by its exit code
function Invoke-Reg {
    $ErrorActionPreference = "Continue"
    & reg.exe @args 2>&1 | Out-Null
    return $LASTEXITCODE
}

function Restore-PlayerSettings {
    if (Test-Path $script:SettingsBackup) {
        Invoke-Reg delete $script:SettingsKey /f | Out-Null
        if ((Invoke-Reg import $script:SettingsBackup) -ne 0) { throw "Could not restore the player's settings from $($script:SettingsBackup); it is kept." }
        Remove-Item $script:SettingsBackup -Force
        Write-Host "Player settings restored."
    }
    elseif (Test-Path $script:SettingsHad) {
        # there were no settings before the run: leave none behind
        Invoke-Reg delete $script:SettingsKey /f | Out-Null
        Remove-Item $script:SettingsHad -Force
        Write-Host "Player settings restored (there were none)."
    }
}

function Use-TestSettings {
    New-Item -ItemType Directory -Force (Split-Path $script:SettingsBackup) | Out-Null
    # a run that was killed left the player's settings in a backup: put them back before anything else
    Restore-PlayerSettings
    if (Test-Path $script:SettingsPath) {
        if ((Invoke-Reg export $script:SettingsKey $script:SettingsBackup /y) -ne 0 -or -not (Test-Path $script:SettingsBackup)) { throw "Could not back up the player's settings; not running." }
    }
    else {
        New-Item -ItemType File -Force $script:SettingsHad | Out-Null
        New-Item -Path $script:SettingsPath -Force | Out-Null
    }
    # PlayerPrefs keeps a string as UTF-8 bytes with a closing zero
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($script:TestSettingsJson + [char]0)
    Set-ItemProperty -Path $script:SettingsPath -Name $script:SettingsValue -Type Binary -Value $bytes
    Write-Host "Test settings in place (silent, VSync on, windowed); the player's are backed up."
}

# ---- keeping test windows out of the way ----
# A test run needs a real window (the pictures come from the GPU), but it need not be in front: each game window is sent
# behind every other window, on the same monitor (so the display it reports, and the reference, stay the same), and the
# window that was in front gets the focus back.
if (-not ("TestWindow" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TestWindow {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    public static readonly IntPtr Bottom = new IntPtr(1);
    // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
    public const uint Flags = 0x0001 | 0x0002 | 0x0010;
}
"@
}

function Get-FrontWindow { [TestWindow]::GetForegroundWindow() }

# Waits (up to 20 s) for the process's window, sends it to the back and gives the focus back to $front.
function Hide-GameWindow([System.Diagnostics.Process]$p, [IntPtr]$front) {
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and -not $p.HasExited) {
        $p.Refresh()
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
            [void][TestWindow]::SetWindowPos($p.MainWindowHandle, [TestWindow]::Bottom, 0, 0, 0, 0, [TestWindow]::Flags)
            if ($front -ne [IntPtr]::Zero -and [TestWindow]::IsWindow($front)) { [void][TestWindow]::SetForegroundWindow($front) }
            return
        }
        Start-Sleep -Milliseconds 50
    }
}
