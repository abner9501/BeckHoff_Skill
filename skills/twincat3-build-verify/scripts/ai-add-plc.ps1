# Add a PLC project (from the stock "Standard PLC Template") to an existing
# TwinCAT solution over the Automation Interface, then create a GVL holding one
# BOOL variable.  Headless / scripted -- no manual clicking in XAE.
#
# Why PowerShell late binding is fine here: the DTE2 early-binding trap only
# bites when reading ToolWindows.ErrorList (see build-plc.ps1).  Creating tree
# items via ITcSysManager works fine late-bound, and the TE1000 manual itself
# documents the PowerShell form of every one of these calls.
#
# API notes (TE1000 v1.6.2):
#   ITcSmTreeItem::CreateChild(bstrName, nSubType, bstrPrefix, vInfo)
#   - PLC project under TIPC:  CreateChild(name, 0, "", "<template name>")
#     The stock templates are addressed by NAME, not path:
#       "Standard PLC Template"  /  "Empty PLC Template"
#   - object subtypes: GVL = 615, PRG = 602, FUN = 603, FB = 604, ITF = 618
#   - a GVL's vInfo is the declaration text (VAR_GLOBAL ... END_VAR)
#
# NOTE: ASCII only.  Windows PowerShell 5.1 reads BOM-less UTF-8 as GBK, so any
# Chinese literal in this file would be mangled into a syntax error.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File ai-add-plc.ps1
#   ... -File ai-add-plc.ps1 -PlcName AdsTest -GvlName GVL_Test -VarName bTest

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [string]$ProgId  = 'TcXaeShell.DTE.15.0',
    [string]$PlcName = 'AdsTest',
    [string]$GvlName = 'GVL_Test',
    [string]$VarName = 'bTest',
    [switch]$Visible,
    [switch]$SaveAs
)

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Must run in STA (use Windows PowerShell 5.1, or pwsh -STA).'
}
if (-not (Test-Path $Sln)) { throw "Solution not found: $Sln" }

# --- COM message filter ---------------------------------------------------
# Without this, TcXaeShell rejects calls that arrive while it is busy:
#   RPC_E_CALL_REJECTED 0x80010001 ("Call was rejected by callee")
# RetryRejectedCall returning 99 makes COM retry after 99 ms instead of
# bubbling the failure up.  Same trap build-plc.ps1 documents.
$cs = @'
using System;
using System.Runtime.InteropServices;

public static class TwCom {
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
[TwCom]::RegisterFilter()

$dte = $null
try {
    Write-Host "== 1) start TcXaeShell ($ProgId), visible=$($Visible.IsPresent)"
    $dte = New-Object -ComObject $ProgId
    try { $dte.SuppressUI = $false } catch {}
    try { $dte.MainWindow.Visible = $Visible.IsPresent } catch {}
    # TcAutomationSettings.SilentMode is deliberately NOT set.  With SilentMode
    # on, TcXaeShell skips the step that binds a freshly created PLC project to
    # a task -- the project stays on Context "Default", and the follow-up build
    # then fails inside Tc3_Module's task glue code:
    #   IecTaskModule.TcGetObjectName : Cannot convert type 'POINTER TO SINT'
    #   to type 'UDINT' [tc3_module, 3.4.5.0]
    # (x64 only -- pointers are 8 bytes there, so the cast is illegal.)
    # Verified 2026-10-15: the same create+GVL flow compiles clean on x64 as
    # soon as SilentMode is left alone.

    Write-Host "== 2) open solution"
    $dte.Solution.Open($Sln)
    Write-Host "   opened: $($dte.Solution.FullName)"

    Write-Host "== 3) grab ITcSysManager from project 1"
    $proj = $dte.Solution.Projects.Item(1)
    Write-Host "   project: $($proj.Name)"
    $sysManager = $proj.Object

    Write-Host "== 4) TIPC children before"
    $plc = $sysManager.LookupTreeItem('TIPC')
    $existing = @()
    foreach ($c in $plc) { $existing += $c.Name; Write-Host "   existing: $($c.Name)" }

    Write-Host "== 5) create PLC project '$PlcName' from 'Standard PLC Template' (skip if present)"
    if ($existing -contains $PlcName) {
        Write-Host "   already exists -- not creating"
    } else {
        $newProject = $plc.CreateChild($PlcName, 0, '', 'Standard PLC Template')
        Write-Host "   created: $($newProject.Name)"
    }

    $projPath = "TIPC^$PlcName^$PlcName Project"
    Write-Host "== 6) nested project path: $projPath"
    $nested = $sysManager.LookupTreeItem($projPath)
    Write-Host "   nested: $($nested.Name)  (subtype $($nested.ItemSubType))"

    Write-Host "== 7) list children of nested project"
    foreach ($c in $nested) {
        Write-Host "   - $($c.Name)  subtype=$($c.ItemSubType)"
    }

    Write-Host "== 8) create GVL '$GvlName' with VAR_GLOBAL $VarName : BOOL; END_VAR"
    $gvls = $sysManager.LookupTreeItem("$projPath^GVLs")
    $gvlExisting = @()
    foreach ($c in $gvls) { $gvlExisting += $c.Name; Write-Host "   existing GVL: $($c.Name)" }
    if ($gvlExisting -contains $GvlName) {
        Write-Host "   already exists -- not creating"
    } else {
        $decl = "VAR_GLOBAL`r`n`t$VarName : BOOL;`r`nEND_VAR"
        $gvl = $gvls.CreateChild($GvlName, 615, '', $decl)
        Write-Host "   created: $($gvl.Name)"
    }

    Write-Host "== 9) save"
    # SaveConfiguration() no longer exists in current AI builds:
    #   "SaveConfiguration only supported in Automation Legacy Mode!
    #    Please use standard DTE Solution features for Project persistence"
    # so persist through DTE instead.
    try {
        $dte.ExecuteCommand('File.SaveAll')
        Write-Host "   File.SaveAll OK"
    } catch {
        Write-Host "   File.SaveAll failed: $($_.Exception.Message)"
        $dte.Solution.SaveAs($Sln)
        Write-Host "   Solution.SaveAs OK"
    }

    Write-Host "== DONE"
}
finally {
    if ($dte) { try { $dte.Quit() } catch {} }
    try { [TwCom]::RevokeFilter() } catch {}
}
