<#
  Checks whether the Experimental Instance runs the latest build of Shortcut Cheatsheet, and repairs it when asked.
  Close every Experimental Instance first: a running instance locks the DLL, and copying then fails half-way.

  Example (from the repository root):
    powershell -File tools\check-exp-deploy.ps1          # only check
    powershell -File tools\check-exp-deploy.ps1 -Repair  # copy the build into the Experimental Instance
#>
param([switch]$Repair, [string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root "Builds\ShortcutCheatsheet\$Configuration"

# The Experimental Instance of the newest VS 2026 hive on this machine.
$hive = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\VisualStudio" -Directory -Filter '18.0_*Exp' | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $hive) { throw 'No Experimental Instance of VS 2026 found.' }
$extensions = Join-Path $hive.FullName 'Extensions'
$deployed = Join-Path $extensions 'Herbert Nijkamp\Shortcut Cheatsheet\0.1.0'

$running = Get-CimInstance Win32_Process -Filter "Name='devenv.exe'" | Where-Object CommandLine -like '*rootsuffix Exp*'
if ($running) { Write-Warning "An Experimental Instance is running (pid $($running.ProcessId -join ', ')). Close it before repairing." }

"Build:    $build"
"Deployed: $deployed"
foreach ($name in 'ShortcutCheatsheet.dll', 'ShortcutCheatsheet.pkgdef', 'extension.vsixmanifest') {
    $a = Join-Path $build $name
    $b = Join-Path $deployed $name
    $ha = if (Test-Path $a) { (Get-FileHash $a -Algorithm MD5).Hash } else { 'missing' }
    $hb = if (Test-Path $b) { (Get-FileHash $b -Algorithm MD5).Hash } else { 'missing' }
    $time = if (Test-Path $b) { (Get-Item $b).LastWriteTime } else { '' }
    "{0,-28} {1}  deployed {2}" -f $name, $(if ($ha -eq $hb) { 'same     ' } else { 'DIFFERENT' }), $time
}

# Files of 0 bytes in any extension of the Experimental Instance block every deploy (see the handbook, step 1).
$empty = Get-ChildItem $extensions -Recurse -File | Where-Object { $_.Length -eq 0 -and $_.Name -ne 'extensions.configurationchanged' }
if ($empty) { 'Files of 0 bytes (a broken deploy):'; $empty | ForEach-Object { "  $($_.FullName)" } }

if ($Repair) {
    if ($running) { throw 'Close the Experimental Instance first.' }
    New-Item -ItemType Directory -Force $deployed | Out-Null
    Copy-Item (Join-Path $build '*') $deployed -Recurse -Force -Exclude '*.vsix'
    # Makes VS read the .pkgdef files again at the next start.
    $flag = Join-Path $extensions 'extensions.configurationchanged'
    if (Test-Path $flag) { (Get-Item $flag).LastWriteTime = Get-Date } else { New-Item $flag -ItemType File | Out-Null }
    'Copied. Start the Experimental Instance again.'
}
