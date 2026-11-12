# Activate a TwinCAT configuration on a REMOTE target selected by AMS NetId.
#
#   ITcSysManager2::SetTargetNetId(netId) -- select the target system
#   ITcSysManager::ActivateConfiguration() -- compile + download the config
#   ITcSysManager::StartRestartTwinCAT()   -- bring the runtime into Run
#
# NOTE: this RESTARTS TwinCAT on the target machine.  Only run it when the
#       owner of that machine has explicitly allowed it.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File ai-activate-remote.ps1 `
#       -Sln <x.sln> -TargetNetId 1.2.3.4.5.6 [-NoRestart]
#
# NOTE: ASCII only (PS 5.1 reads BOM-less UTF-8 as GBK).

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [Parameter(Mandatory=$true)][string]$TargetNetId,
    [string]$ProgId = 'TcXaeShell.DTE.15.0',
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Must run in STA (use Windows PowerShell 5.1).'
}
if (-not (Test-Path $Sln)) { throw "Solution not found: $Sln" }

$cs = @'
using System;
using System.Runtime.InteropServices;

public static class TwActComRemote {
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
[TwActComRemote]::RegisterFilter()

$dte = $null
try {
    Write-Host "== start TcXaeShell ($ProgId)"
    $dte = New-Object -ComObject $ProgId
    try { $dte.SuppressUI = $false } catch {}
    try { $dte.MainWindow.Visible = $false } catch {}

    Write-Host "== open solution"
    $dte.Solution.Open($Sln)

    $sysManager = $dte.Solution.Projects.Item(1).Object

    Write-Host "== target before = $($sysManager.GetTargetNetId())"
    Write-Host "== SetTargetNetId($TargetNetId)"
    $sysManager.SetTargetNetId($TargetNetId)
    $after = "$($sysManager.GetTargetNetId())"
    Write-Host "== target after  = $after"
    if ($after -ne $TargetNetId) {
        throw "Target mismatch: expected '$TargetNetId', got '$after'"
    }

    Write-Host "== IsTwinCATStarted (before) = $($sysManager.IsTwinCATStarted())"

    Write-Host "== ActivateConfiguration()  <- compiles + downloads to $TargetNetId"
    $r = $sysManager.ActivateConfiguration()
    if ("$r") { Write-Host "   returned: $r" } else { Write-Host "   returned empty (no error text)" }

    if ($NoRestart) {
        Write-Host "== -NoRestart given: not starting TwinCAT"
    } else {
        Write-Host "== StartRestartTwinCAT()"
        $r2 = $sysManager.StartRestartTwinCAT()
        if ("$r2") { Write-Host "   returned: $r2" } else { Write-Host "   returned empty (no error text)" }
    }

    Write-Host "== IsTwinCATStarted (after) = $($sysManager.IsTwinCATStarted())"
    Write-Host "== DONE"
}
finally {
    if ($dte) { try { $dte.Quit() } catch {} }
    try { [TwActComRemote]::RevokeFilter() } catch {}
}
