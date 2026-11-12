# Create a PLC project with a Tc2_MC2 axis reference and an MC_Power enable
# program.  Headless / scripted.
#
# What it does:
#   1. create PLC project <PlcName> from "Standard PLC Template" (if missing)
#   2. add the Tc2_MC2 library reference
#   3. create GVL_Motion with Axis1 : Tc2_MC2.AXIS_REF + status mirror vars
#   4. overwrite MAIN declaration/implementation with the MC_Power logic
#   5. save
#
# Axis linking to an NC axis is a separate step (LinkVariables), because the
# tree path of the NC axis depends on the NC task name.
#
# NOTE: ASCII only.  Windows PowerShell 5.1 reads BOM-less UTF-8 as GBK, so any
# Chinese literal in this file would be mangled into a syntax error.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File ai-add-motion-plc.ps1
#   ... -File ai-add-motion-plc.ps1 -Sln <path> -PlcName MotionTest

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [string]$ProgId  = 'TcXaeShell.DTE.15.0',
    [string]$PlcName = 'MotionTest',
    [switch]$Visible
)

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Must run in STA (use Windows PowerShell 5.1, or pwsh -STA).'
}
if (-not (Test-Path $Sln)) { throw "Solution not found: $Sln" }

$cs = @'
using System;
using System.Runtime.InteropServices;

public static class TwComMotion {
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
[TwComMotion]::RegisterFilter()

$dte = $null
try {
    Write-Host "== 1) start TcXaeShell, open solution"
    $dte = New-Object -ComObject $ProgId
    try { $dte.SuppressUI = $false } catch {}
    try { $dte.MainWindow.Visible = $Visible.IsPresent } catch {}
    if ($dte.Solution.FullName -ne $Sln) {
        $dte.Solution.Open($Sln)
        Start-Sleep -Seconds 2
    }
    Write-Host "   solution: $($dte.Solution.FullName)"

    $sysManager = $dte.Solution.Projects.Item(1).Object

    Write-Host "== 2) create PLC project '$PlcName' (skip if present)"
    $plc = $sysManager.LookupTreeItem('TIPC')
    $existing = @()
    foreach ($c in $plc) { $existing += $c.Name }
    if ($existing -contains $PlcName) {
        Write-Host "   already exists -- not creating"
    } else {
        $null = $plc.CreateChild($PlcName, 0, '', 'Standard PLC Template')
        Write-Host "   created"
    }

    $projPath = "TIPC^$PlcName^$PlcName Project"
    $projNode = $sysManager.LookupTreeItem($projPath)
    Write-Host "   project node: $($projNode.Name)"

    Write-Host "== 3) library references before"
    $refs = $sysManager.LookupTreeItem("$projPath^References")
    $refNames = @()
    foreach ($c in $refs) { $refNames += $c.Name; Write-Host "   - $($c.Name)" }

    Write-Host "== 4) add Tc2_MC2 (skip if present)"
    if ($refNames -contains 'Tc2_MC2') {
        Write-Host "   already referenced"
    } else {
        $refs.AddLibrary('Tc2_MC2', '*', 'Beckhoff Automation GmbH')
        Write-Host "   added"
    }

    Write-Host "== 5) create GVL_Motion (skip if present)"
    $gvls = $sysManager.LookupTreeItem("$projPath^GVLs")
    $gvlNames = @()
    foreach ($c in $gvls) { $gvlNames += $c.Name }
    $gvlDecl = @"
VAR_GLOBAL
	Axis1 : Tc2_MC2.AXIS_REF;

	// ---- control (write from ADS) ----
	bPowerOn : BOOL;

	// ---- status mirror (read from ADS) ----
	bEnabled : BOOL;
	bBusy : BOOL;
	bError : BOOL;
	nErrorID : UDINT;
	fActPos : LREAL;
	bStandStill : BOOL;
	bErrorStop : BOOL;
END_VAR
"@
    if ($gvlNames -contains 'GVL_Motion') {
        Write-Host "   GVL_Motion already exists -- rewriting declaration"
        $gvl = $sysManager.LookupTreeItem("$projPath^GVLs^GVL_Motion")
        $gvl.DeclarationText = $gvlDecl
    } else {
        $null = $gvls.CreateChild('GVL_Motion', 615, '', $gvlDecl)
        Write-Host "   created"
    }

    Write-Host "== 6) write MAIN declaration + implementation"
    $main = $sysManager.LookupTreeItem("$projPath^POUs^MAIN")
    $decl = @"
PROGRAM MAIN
VAR
	fbPower : Tc2_MC2.MC_Power;
END_VAR
"@
    $impl = @"
// Axis enable: GVL_Motion.bPowerOn is written from the outside (ADS).
// MC_Power stays called every cycle; Enable is level-triggered.
fbPower(
	Axis := GVL_Motion.Axis1,
	Enable := GVL_Motion.bPowerOn,
	Enable_Positive := TRUE,
	Enable_Negative := TRUE,
	Status => GVL_Motion.bEnabled,
	Busy => GVL_Motion.bBusy,
	Error => GVL_Motion.bError,
	ErrorID => GVL_Motion.nErrorID
);

// ActPos from the NC axis (linked NcToPlc), plus two state bits for observation.
GVL_Motion.fActPos := GVL_Motion.Axis1.NcToPlc.ActPos;
GVL_Motion.bStandStill := GVL_Motion.Axis1.Status.StandStill;
GVL_Motion.bErrorStop := GVL_Motion.Axis1.Status.ErrorStop;
"@
    $main.DeclarationText = $decl
    $main.ImplementationText = $impl
    Write-Host "   MAIN written"

    Write-Host "== 7) save"
    $dte.ExecuteCommand('File.SaveAll')
    Write-Host "   saved"
    Write-Host "== DONE"
}
finally {
    if ($dte) { try { $dte.Quit() } catch {} }
    try { [TwComMotion]::RevokeFilter() } catch {}
}
