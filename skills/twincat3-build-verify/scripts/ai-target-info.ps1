# Read-only query of a TwinCAT target's SYSTEM node (no activation, no download).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File ai-target-info.ps1 `
#       -Sln <x.sln> -TargetNetId 1.2.3.4.5.6
#
# Prints TargetNetId and the SYSTEM-node XML lines that look like platform /
# product / version / OS / CPU information.
# NOTE: ASCII only (PS 5.1 reads BOM-less UTF-8 as GBK).

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [Parameter(Mandatory=$true)][string]$TargetNetId,
    [string]$ProgId = 'TcXaeShell.DTE.15.0'
)

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Must run in STA (use Windows PowerShell 5.1).'
}
if (-not (Test-Path $Sln)) { throw "Solution not found: $Sln" }

$cs = @'
using System;
using System.Runtime.InteropServices;

public static class TwInfoCom {
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
[TwInfoCom]::RegisterFilter()

$dte = $null
try {
    $dte = New-Object -ComObject $ProgId
    try { $dte.SuppressUI = $false } catch {}
    try { $dte.MainWindow.Visible = $false } catch {}

    $dte.Solution.Open($Sln)
    $sysManager = $dte.Solution.Projects.Item(1).Object

    Write-Host "== SetTargetNetId($TargetNetId)"
    $sysManager.SetTargetNetId($TargetNetId)
    Write-Host "== TargetNetId = $($sysManager.GetTargetNetId())"

    Write-Host "== SYSTEM node properties"
    $sys = $sysManager.LookupTreeItem("SYSTEM")
    try {
        Write-Host ("   ItemName : " + $sys.Name)
        Write-Host ("   ItemType : " + $sys.ItemType)
        Write-Host ("   ItemSubType : " + $sys.ItemSubType)
        Write-Host ("   ChildCount : " + $sys.ChildCount)
    } catch { Write-Host "   (basic properties unavailable: $_)" }

    Write-Host "== SYSTEM XML (filtered)"
    try {
        $xml = $sys.ProduceXml()
        $lines = $xml -split "`r?`n"
        $hit = $lines | Select-String -Pattern 'Target|Platform|Product|Version|Cpu|CPU|OS|Image|Computer|Name' |
               Select-Object -First 80
        foreach ($l in $hit) { Write-Host ("   " + $l.Line.Trim()) }
    } catch {
        Write-Host "   ProduceXml failed: $_"
    }
}
finally {
    if ($dte) { try { $dte.Quit() } catch {} }
    try { [TwInfoCom]::RevokeFilter() } catch {}
}
