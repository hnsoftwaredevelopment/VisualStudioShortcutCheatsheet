<#
Screenshots for the README, made in the Experimental Instance without keyboard or mouse:
DTE runs commands and floats the window, UI Automation fills in the window, PrintWindow captures only that window.

  tools\readme-shots.ps1 -Start                  start the Experimental Instance (with README.md, past the start window)
  tools\readme-shots.ps1 -Open                   open Shortcut Cheatsheet as a floating window of 900 x 700
  tools\readme-shots.ps1 -Search 'debug'         type in the search box ('' clears it)
  tools\readme-shots.ps1 -Group 'Extension'      choose a grouping by its label
  tools\readme-shots.ps1 -Toggle 'Only extensions'  switch a filter
  tools\readme-shots.ps1 -Expand 'Global'        open a group
  tools\readme-shots.ps1 -Shot docs\images\window.png   capture the floating window

The labels are those of the language set in the options; the README uses English.
#>
param(
    [switch]$Start, [switch]$Open, [string]$Search, [string]$Group, [string]$Toggle, [string]$Expand, [string]$Shot,
    [int]$Width = 900, [int]$Height = 700)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$title = 'Shortcut Cheatsheet'

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
public static class Shots
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    // A floating tool window is a top-level window of devenv without "Microsoft Visual Studio" in its title
    // (VS gives it the solution name, not the caption of the tool window).
    public static IntPtr FindFloating(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => { int id; GetWindowThreadProcessId(h, out id); var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
            if (id == pid && IsWindowVisible(h) && sb.Length > 0 && !sb.ToString().Contains("Microsoft Visual Studio")) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);
    [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static IntPtr FindTitle(string exact)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => { var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
            if (IsWindowVisible(h) && sb.ToString() == exact) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }
    public static object Rot(string name)
    {
        IRunningObjectTable rot; GetRunningObjectTable(0, out rot);
        IEnumMoniker e; rot.EnumRunning(out e);
        var m = new IMoniker[1];
        while (e.Next(1, m, IntPtr.Zero) == 0)
        {
            IBindCtx ctx; CreateBindCtx(0, out ctx);
            string d; m[0].GetDisplayName(ctx, null, out d);
            if (d == name) { object o; rot.GetObject(m[0], out o); return o; }
        }
        return null;
    }
}
'@
[Shots]::SetProcessDPIAware() | Out-Null

function Get-ExpProcess {
    Get-CimInstance Win32_Process -Filter "Name='devenv.exe'" | Where-Object CommandLine -like '*rootsuffix Exp*' | Select-Object -First 1
}

function Get-Dte {
    $p = Get-ExpProcess
    if (-not $p) { throw 'The Experimental Instance is not running. Use -Start.' }
    $dte = [Shots]::Rot("!VisualStudio.DTE.18.0:$($p.ProcessId)")
    if (-not $dte) { throw 'No DTE yet; wait until Visual Studio has loaded.' }
    $dte
}

$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function Find-Element([string]$name, $type) {
    $p = Get-ExpProcess
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$p.ProcessId)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type)))
    $e = $A::RootElement.FindFirst($TS::Descendants, $cond)
    if (-not $e) { throw "'$name' not found." }
    $e
}

if ($Start) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -prerelease -products * -property installationPath
    Start-Process (Join-Path $vs 'Common7\IDE\devenv.exe') -ArgumentList '/rootsuffix', 'Exp', (Join-Path $root 'README.md')
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep 2
        try { Get-Dte | Out-Null; 'started'; break } catch { }
    }
}

if ($Open) {
    $dte = Get-Dte
    $dte.ExecuteCommand('View.ShortcutCheatsheet')
    Start-Sleep 2
    # By the GUID of CheatsheetWindow; the caption could be in another language.
    $window = $dte.Windows.Item('{68b99b08-124c-4da3-ace2-dcd8c36dbd5a}')
    $window.IsFloating = $true
    $window.Width = $Width
    $window.Height = $Height
    $window.Left = 80
    $window.Top = 80
    'open'
}

if ($PSBoundParameters.ContainsKey('Search')) {
    $box = Find-Element 'Search' ([System.Windows.Automation.ControlType]::Edit)
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Search)
    Start-Sleep -Milliseconds 800   # the window waits 200 ms after the last change
    "search '$Search'"
}

if ($Group) {
    # The items of a ComboBox only exist once its list has been opened.
    $combo = Find-Element 'Group by' ([System.Windows.Automation.ControlType]::ComboBox)
    # Not $expand: PowerShell names are case-insensitive, and that is the parameter -Expand.
    $dropDown = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $dropDown.Expand()
    Start-Sleep -Milliseconds 300
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $Group)),
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
    $item = $combo.FindFirst($TS::Descendants, $cond)
    if (-not $item) { $dropDown.Collapse(); throw "No grouping '$Group'." }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $dropDown.Collapse()
    Start-Sleep -Milliseconds 800
    "group '$Group'"
}

if ($Toggle) {
    $box = Find-Element $Toggle ([System.Windows.Automation.ControlType]::CheckBox)
    $box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 800
    "toggled '$Toggle'"
}

if ($Expand) {
    $p = Get-ExpProcess
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$p.ProcessId)),
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Group)))
    $groups = $A::RootElement.FindAll($TS::Descendants, $cond)
    $hit = $groups | Where-Object { $_.Current.Name -like "*$Expand*" } | Select-Object -First 1
    if (-not $hit) { throw "Group '$Expand' not found." }
    $hit.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 500
    "expanded '$Expand'"
}

if ($Shot) {
    $h = [Shots]::FindFloating([int](Get-ExpProcess).ProcessId)
    if ($h -eq [IntPtr]::Zero) { throw "No floating window; use -Open first." }
    $r = New-Object Shots+RECT
    [Shots]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    [Shots]::PrintWindow($h, $dc, 2) | Out-Null   # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($dc)
    $path = if ([IO.Path]::IsPathRooted($Shot)) { $Shot } else { Join-Path $root $Shot }
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    "saved $path ($($bmp.Width) x $($bmp.Height))"
}
