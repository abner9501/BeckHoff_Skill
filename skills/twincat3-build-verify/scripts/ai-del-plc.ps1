# Delete one or more PLC projects from a TwinCAT solution over the Automation
# Interface (ITcSmTreeItem::DeleteChild).  Deleting through the tree -- rather
# than removing the folder -- also drops the <Project> node in the .tsproj and
# the GUID entries in the .sln.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File ai-del-plc.ps1 `
#       -Sln <x.sln> -Names Probe2,Probe3
#
# NOTE: ASCII only (PS 5.1 reads BOM-less UTF-8 as GBK).

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [Parameter(Mandatory=$true)][string]$Names,   # comma-separated PLC projects
    [string]$TaskNames = '',                      # comma-separated tasks under TIRT
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

public static class TwDelCom {
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
[TwDelCom]::RegisterFilter()

$dte = $null
try {
    Write-Host "== start TcXaeShell ($ProgId)"
    $dte = New-Object -ComObject $ProgId
    try { $dte.SuppressUI = $false } catch {}
    try { $dte.MainWindow.Visible = $false } catch {}
    # TcAutomationSettings.SilentMode is NOT set on purpose -- see ai-add-plc.ps1

    Write-Host "== open solution"
    $dte.Solution.Open($Sln)

    $sysManager = $dte.Solution.Projects.Item(1).Object
    $plc = $sysManager.LookupTreeItem('TIPC')

    $before = @()
    foreach ($c in $plc) { $before += $c.Name }
    Write-Host "== PLC projects before: $($before -join ', ')"

    $nameList = $Names -split '\s*,\s*'
    foreach ($n in $nameList) {
        Write-Host "== delete '$n'"
        if ($before -notcontains $n) {
            Write-Host "   not present -- skipped"
            continue
        }
        try {
            $plc.DeleteChild($n) | Out-Null
            Write-Host "   deleted"
        } catch {
            Write-Host "   FAILED: $($_.Exception.Message)"
        }
    }

    if ($TaskNames) {
        $tasks = $sysManager.LookupTreeItem('TIRT')
        $taskBefore = @()
        foreach ($c in $tasks) { $taskBefore += $c.Name }
        Write-Host "== tasks before: $($taskBefore -join ', ')"
        foreach ($n in ($TaskNames -split '\s*,\s*')) {
            Write-Host "== delete task '$n'"
            if ($taskBefore -notcontains $n) {
                Write-Host "   not present -- skipped"
                continue
            }
            try {
                $tasks.DeleteChild($n) | Out-Null
                Write-Host "   deleted"
            } catch {
                Write-Host "   FAILED: $($_.Exception.Message)"
            }
        }
    }

    Write-Host "== save"
    $dte.ExecuteCommand('File.SaveAll')
    Write-Host "== DONE"
}
finally {
    if ($dte) { try { $dte.Quit() } catch {} }
    try { [TwDelCom]::RevokeFilter() } catch {}
}
