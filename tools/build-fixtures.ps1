<#
  Compiles the test fixtures tests\Fixtures\CommandTables\*.vsct to .cto with VSCT.exe from the
  Microsoft.VSSDK.BuildTools NuGet package. Run it after changing a fixture; the .cto files are committed,
  so the tests do not need VSCT.

  Also writes <name>.decompiled.vsct: VSCT turned back into XML. That is what VS itself sees in the .cto,
  and it is the reference for the tests.

  Example:
    powershell -File tools\build-fixtures.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdk = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.vssdk.buildtools" -Directory | Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
if (-not $sdk) { throw 'Microsoft.VSSDK.BuildTools not found in the NuGet cache. Build the solution once first.' }
$tools = Join-Path $sdk.FullName 'tools\VSSDK'
$vsct = Join-Path $tools 'bin\VSCT.exe'

foreach ($source in Get-ChildItem (Join-Path $root 'tests\Fixtures\CommandTables') -Filter *.vsct | Where-Object Name -notlike '*.decompiled.vsct') {
    $cto = [IO.Path]::ChangeExtension($source.FullName, '.cto')
    $decompiled = [IO.Path]::ChangeExtension($source.FullName, '.decompiled.vsct')
    & $vsct $source.FullName $cto "-I$tools\inc" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "VSCT failed for $($source.Name)" }
    # VSCT writes the .h of a decompile into the current folder, so run it from the fixture folder.
    Push-Location $source.DirectoryName
    try { & $vsct $cto $decompiled | Out-Null } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "Decompiling $($source.Name) failed" }
    "{0} -> {1} ({2} bytes)" -f $source.Name, (Split-Path $cto -Leaf), (Get-Item $cto).Length
}
