# Add an NC-Task and simulated NC axis(es) to a TwinCAT solution over the
# Automation Interface.  Headless / scripted.
#
# API (TE1000 v1.6.2, section 4.3.10 "NC Configuration"):
#   TINC                     -> NC configuration node
#   TINC.CreateChild("NC-Task", 1)
#   TINC^NC-Task^Axes.CreateChild("Axis 1", 1)
#   axis.ProduceXml() / axis.ConsumeXml(xml)   -> parameterization
#
# A freshly created axis defaults to a simulated encoder/drive
# (EncType=1, DrvType=5) -- no hardware needed.
#
# NOTE: ASCII only.  Windows PowerShell 5.1 reads BOM-less UTF-8 as GBK, so any
# Chinese literal in this file would be mangled into a syntax error.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File ai-add-nc-axis.ps1
#   ... -File ai-add-nc-axis.ps1 -Sln <path> -Axes "Axis 1,Axis 2"

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [string]$ProgId   = 'TcXaeShell.DTE.15.0',
    [string]$TaskName = 'NC-Task',
    [string]$Axes     = 'Axis 1',
    [switch]$Visible
)

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Must run in STA (use Windows PowerShell 5.1, or pwsh -STA).'
}
if (-not (Test-Path $Sln)) { throw "Solution not found: $Sln" }

# --- COM message filter (same rationale as ai-add-plc.ps1) ----------------
$cs = @'
using System;
using System.Runtime.InteropServices;

public static class TwComNc {
    [ComImport, Guid("00000016-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleMessageFilter {
        [PreserveSig] int HandleInComingCall(int a, IntPtr b, int c, IntPtr d);
        [PreserveSig] int RetryRejectedCall(IntPtr h, int t, int rejectType);
        [PreserveSig] int MessagePending(IntPtr h, int t, int p);
    }

    class MessageFilter : IOleMessageFilter {
        [DllImport("Ole32.dll")]
        private static extern int CoRegisterMessageFilter(IOleMessageFilter nf, out IOleMessageFilter of);
        public static void Register() { IOleMessageFilter of; CoRegisterMessageFilter(new MessageFilter(), out of); }
        public static void Revoke()   { IOleMessageFilter of; CoRegisterMessageFilter(null, out of); }
        public int HandleInComingCall(int a, IntPtr b, int c, IntPtr d) { return 0; }
        public int RetryRejectedCall(IntPtr h, int t, int rejectType)   { return (rejectType == 2) ? 99 : -1; }
        public int MessagePending(IntPtr h, int t, int p)               { return 2; }
    }

    public static void RegisterFilter() { MessageFilter.Register(); }
    public static void RevokeFilter()   { MessageFilter.Revoke(); }
}
'@
Add-Type -TypeDefinition $cs
[TwComNc]::RegisterFilter()

$dte = $null
$attached = $false
try {
    # Prefer attaching to an already running TcXaeShell (the one the user has
    # open).  Starting a second instance on the same solution makes saving
    # fail because the file is locked by the first one.
    Write-Host "== 1) attach to running TcXaeShell, else start one"
    try {
        $dte = [Runtime.InteropServices.Marshal]::GetActiveObject($ProgId)
        $attached = $true
        Write-Host "   attached to running instance"
    } catch {
        $dte = New-Object -ComObject $ProgId
        try { $dte.SuppressUI = $false } catch {}
        try { $dte.MainWindow.Visible = $Visible.IsPresent } catch {}
        Write-Host "   started new instance, visible=$($Visible.IsPresent)"
    }

    Write-Host "== 2) ensure solution is open"
    if ($dte.Solution.FullName -ne $Sln) {
        $dte.Solution.Open($Sln)
        Start-Sleep -Seconds 2
    }
    Write-Host "   solution: $($dte.Solution.FullName)"

    $proj = $dte.Solution.Projects.Item(1)
    Write-Host "   project: $($proj.Name)"
    $sysManager = $proj.Object

    Write-Host "== 3) TINC children before"
    $nc = $sysManager.LookupTreeItem('TINC')
    $existing = @()
    foreach ($c in $nc) {
        $existing += $c.Name
        Write-Host "   existing: $($c.Name)  subtype=$($c.ItemSubType)"
    }

    Write-Host "== 4) create NC-Task '$TaskName' (skip if present)"
    if ($existing -contains $TaskName) {
        Write-Host "   already exists -- not creating"
    } else {
        $newTask = $nc.CreateChild($TaskName, 1)
        Write-Host "   created: $($newTask.Name)  subtype=$($newTask.ItemSubType)"
    }

    Write-Host "== 5) TINC children after"
    $taskRealName = $null
    foreach ($c in $nc) {
        Write-Host "   - $($c.Name)  subtype=$($c.ItemSubType)"
        if ($c.Name -like "$TaskName*") { $taskRealName = $c.Name }
    }
    if (-not $taskRealName) { throw 'NC task node not found after creation.' }

    $axesPath = "TINC^$taskRealName^Axes"
    Write-Host "== 6) axes node: $axesPath"
    $axesNode = $sysManager.LookupTreeItem($axesPath)
    $axisExisting = @()
    foreach ($c in $axesNode) { $axisExisting += $c.Name; Write-Host "   existing axis: $($c.Name)" }

    foreach ($a in ($Axes -split ',')) {
        $a = $a.Trim()
        if (-not $a) { continue }
        Write-Host "== 7) create axis '$a' (skip if present)"
        if ($axisExisting -contains $a) {
            Write-Host "   already exists -- not creating"
        } else {
            $newAxis = $axesNode.CreateChild($a, 1)
            Write-Host "   created: $($newAxis.Name)  subtype=$($newAxis.ItemSubType)"
        }
    }

    Write-Host "== 8) dump axis XML (key nodes only)"
    foreach ($a in ($Axes -split ',')) {
        $a = $a.Trim()
        if (-not $a) { continue }
        $axis = $sysManager.LookupTreeItem("$axesPath^$a")
        $xml = $axis.ProduceXml()
        Write-Host "--- axis: $a ---"
        # Print only the pieces that tell sim-vs-hardware apart.
        $xml -split "`r?`n" | Where-Object { $_ -match 'EncType|DrvType|EncPara|DrvPara|nCtrl|MaxCount|DriveControlDWord|AxisType|<Name>' } |
            ForEach-Object { Write-Host "   $_" }
    }

    Write-Host "== 9) save"
    if ($attached) {
        # Working inside the user's own IDE instance: save in place.
        $dte.ExecuteCommand('File.SaveAll')
        Write-Host "   File.SaveAll OK (attached instance)"
    } else {
        try {
            $dte.ExecuteCommand('File.SaveAll')
            Write-Host "   File.SaveAll OK"
        } catch {
            Write-Host "   File.SaveAll failed: $($_.Exception.Message)"
            $dte.Solution.SaveAs($Sln)
            Write-Host "   Solution.SaveAs OK"
        }
    }

    Write-Host "== DONE"
}
finally {
    if ($dte -and -not $attached) { try { $dte.Quit() } catch {} }
    try { [TwComNc]::RevokeFilter() } catch {}
}
