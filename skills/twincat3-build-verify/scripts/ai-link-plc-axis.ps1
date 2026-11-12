# Link a PLC AXIS_REF variable (Tc2_MC2) to an NC axis.
#
# Two links are required:
#   NC  -> PLC : <NcAxis>^Outputs^ToPlc    -> PlcTask Inputs^<var>.NcToPlc
#   PLC -> NC  : PlcTask Outputs^<var>.PlcToNc -> <NcAxis>^Inputs^FromPlc
#
# NOTE: ASCII only (PS 5.1 reads BOM-less UTF-8 as GBK).
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File ai-link-plc-axis.ps1
#   ... -File ai-link-plc-axis.ps1 -VarPath "GVL_Motion.Axis1" -NcAxis "TINC^NC-Task SAF^Axes^Axis 1"

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [string]$ProgId  = 'TcXaeShell.DTE.15.0',
    [string]$VarPath = 'GVL_Motion.Axis1',
    [string]$NcAxis  = 'TINC^NC-Task SAF^Axes^Axis 1',
    [string]$TaskName = 'PlcTask',
    [string]$PlcName  = 'MotionTest',
    [switch]$Visible
)

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Must run in STA (use Windows PowerShell 5.1, or pwsh -STA).'
}

$cs = @'
using System;
using System.Runtime.InteropServices;
public static class TwLinkAxis {
    [ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleMessageFilter {
        [PreserveSig] int HandleInComingCall(int a, IntPtr b, int c, IntPtr d);
        [PreserveSig] int RetryRejectedCall(IntPtr h, int t, int rejectType);
        [PreserveSig] int MessagePending(IntPtr h, int t, int p);
    }
    class MessageFilter : IOleMessageFilter {
        [DllImport("Ole32.dll")] private static extern int CoRegisterMessageFilter(IOleMessageFilter nf, out IOleMessageFilter of);
        public static void Register() { IOleMessageFilter of; CoRegisterMessageFilter(new MessageFilter(), out of); }
        public static void Revoke() { IOleMessageFilter of; CoRegisterMessageFilter(null, out of); }
        public int HandleInComingCall(int a, IntPtr b, int c, IntPtr d) { return 0; }
        public int RetryRejectedCall(IntPtr h, int t, int rejectType) { return (rejectType == 2) ? 99 : -1; }
        public int MessagePending(IntPtr h, int t, int p) { return 2; }
    }
    public static void RegisterFilter() { MessageFilter.Register(); }
    public static void RevokeFilter() { MessageFilter.Revoke(); }
}
'@
Add-Type -TypeDefinition $cs
[TwLinkAxis]::RegisterFilter()

$dte = New-Object -ComObject $ProgId
try {
    try { $dte.SuppressUI = $false } catch {}
    try { $dte.MainWindow.Visible = $Visible.IsPresent } catch {}
    if ($dte.Solution.FullName -ne $Sln) { $dte.Solution.Open($Sln); Start-Sleep -Seconds 3 }
    $sm = $dte.Solution.Projects.Item(1).Object

    $ncOut = "$NcAxis^Outputs^ToPlc"
    $ncIn  = "$NcAxis^Inputs^FromPlc"
    # PLC side needs the FULL path (TIPC^<project>^<project> Instance^...),
    # the short "PlcTask Inputs^..." form only works in documentation examples.
    $plcBase = "TIPC^$PlcName^$PlcName Instance"
    $plcIn  = "$plcBase^$TaskName Inputs^$VarPath.NcToPlc"
    $plcOut = "$plcBase^$TaskName Outputs^$VarPath.PlcToNc"

    Write-Host "== check tree items"
    foreach ($p in @($ncOut, $ncIn, $plcIn, $plcOut)) {
        try {
            $null = $sm.LookupTreeItem($p)
            Write-Host "   OK  $p"
        } catch {
            Write-Host "   MISSING $p"
        }
    }

    Write-Host "== link NC -> PLC"
    $sm.LinkVariables($ncOut, $plcIn, 0, 0, 0)
    Write-Host "   done"

    Write-Host "== link PLC -> NC"
    $sm.LinkVariables($plcOut, $ncIn, 0, 0, 0)
    Write-Host "   done"

    $dte.ExecuteCommand('File.SaveAll')
    Write-Host "== saved"
}
finally {
    try { $dte.Quit() } catch {}
    try { [TwLinkAxis]::RevokeFilter() } catch {}
}
