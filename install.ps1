#Requires -RunAsAdministrator
<#
  Puts Simple Temp in Program Files and lets it start as admin with no prompt.

  The easy way: double-click install.cmd or uninstall.cmd.

  From an admin PowerShell:
    Install:         .\install.ps1
    Undo all of it:  .\install.ps1 -Uninstall

  From source, build first: dotnet publish -c Release -o dist
  "Start with Windows" is a checkbox in the app. It makes the autostart task below.
#>
param(
    [switch]$Uninstall,
    [string]$LogFile
)

$ErrorActionPreference = 'Stop'

$taskName   = 'Simple Temp'
$autoStartTaskName = 'Simple Temp Autostart'
$installDir = Join-Path $env:ProgramFiles 'Simple Temp'
$exeName    = 'SimpleTemp.exe'
$exe        = Join-Path $installDir $exeName
$shortcut   = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Simple Temp.lnk'

# A release zip has the app next to this script. A source checkout has it in dist.
$source = if (Test-Path (Join-Path $PSScriptRoot $exeName)) { $PSScriptRoot } else { Join-Path $PSScriptRoot 'dist' }

if ($LogFile) { Start-Transcript -Path $LogFile -Force | Out-Null }

try {
    # A running copy holds its exe open. Stop the ones the tasks started, so the files can change.
    foreach ($name in $taskName, $autoStartTaskName) {
        if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
            Stop-ScheduledTask -TaskName $name
        }
    }

    if ($Uninstall) {
        foreach ($name in $taskName, $autoStartTaskName) {
            if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
                Unregister-ScheduledTask -TaskName $name -Confirm:$false
                "Removed task: $name"
            }
        }
        if (Test-Path $shortcut) { Remove-Item $shortcut; "Removed shortcut: $shortcut" }
        if (Test-Path $installDir) { Remove-Item $installDir -Recurse; "Removed folder: $installDir" }
        'Done. Simple Temp is uninstalled.'
        return
    }

    if (-not (Test-Path (Join-Path $source $exeName))) {
        throw "Can't find $exeName next to this script or in $source. From source, run: dotnet publish -c Release -o dist"
    }

    # Normal programs can't change files in Program Files. That is what keeps this safe.
    New-Item -ItemType Directory -Force $installDir | Out-Null
    Get-ChildItem $source -File | Where-Object { $_.Name -eq $exeName -or $_.Extension -eq '.dll' } |
        Copy-Item -Destination $installDir -Force
    # Files from a download zip are marked "from the internet". You chose to install these ones.
    Get-ChildItem $installDir -File | Unblock-File
    "Copied app to: $installDir"

    $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction -Execute $exe -Argument '--from-task' -WorkingDirectory $installDir
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    # No time limit: by default Windows would close the app after 3 days.
    # Parallel: clicking the shortcut again opens a window, instead of doing nothing.
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances Parallel `
        -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    $task = @{ TaskName = $taskName; Action = $action; Principal = $principal; Settings = $settings; Force = $true }
    Register-ScheduledTask @task | Out-Null
    "Made task: $taskName (runs as $user)"

    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $installDir
    $link.IconLocation = "$exe,0"
    $link.Description = 'CPU and GPU temperature and usage, and RAM'
    $link.Save()
    "Made shortcut: $shortcut"

    '--- sensor check, running as admin'
    & $exe --probe | Out-String

    if (-not (Get-Service -Name 'PawnIO' -ErrorAction SilentlyContinue)) {
        ''
        'CPU temp needs the free PawnIO driver, and it is not installed.'
        'To install it, run: winget install namazso.PawnIO'
        'Everything else works without it.'
    }
    ''
    'Done. Open Simple Temp from your desktop.'
}
catch {
    "FAILED: $_"
    exit 1
}
finally {
    if ($LogFile) { Stop-Transcript | Out-Null }
    elseif ([Environment]::UserInteractive) { Read-Host 'Press Enter to close' | Out-Null }
}
