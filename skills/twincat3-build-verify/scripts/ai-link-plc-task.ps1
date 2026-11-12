# Bind a PLC project to an XAE task via ITcPlcTaskReference.LinkedTask.
#
# Without this binding a freshly created PLC project stays on Context "Default"
# and an x64 build fails inside Tc3_Module with pointer-to-UDINT errors.
#
# NOTE: ASCII only (PS 5.1 reads BOM-less UTF-8 as GBK).
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File ai-link-plc-task.ps1
#   ... -File ai-link-plc-task.ps1 -Sln <path> -PlcName MotionTest -TaskPath "TIRT^PlcTask"

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [string]$ProgId   = 'TcXaeShell.DTE.15.0',
    [string]$PlcName  = 'MotionTest',
    [string]$TaskPath = 'TIRT^PlcTask',
    [switch]$Visible
)

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Must run in STA (use Windows PowerShell 5.1, or pwsh -STA).'
}

$cs = @'
using System;
using System.Runtime.InteropServices;

public static class TwComTask {
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
[TwComTask]::RegisterFilter()

# Late binding cannot see LinkedTask: PowerShell resolves the ITcSmTreeItem
# interface, and LinkedTask lives on ITcPlcTaskReference.  Force an early-bound
# cast through the stock interop assembly.
$tlbDll = 'C:\TwinCAT\3.1\Components\TcBlockDiagram\Common\TCatSysManagerLib.dll'
if (-not (Test-Path $tlbDll)) { throw "Interop assembly not found: $tlbDll" }
$binder = @'
using System;
public static class TwTaskBinder {
    public static string GetLinkedTask(object node) {
        return ((TCatSysManagerLib.ITcPlcTaskReference)node).LinkedTask;
    }
    public static void SetLinkedTask(object node, string taskPath) {
        ((TCatSysManagerLib.ITcPlcTaskReference)node).LinkedTask = taskPath;
    }
}
'@
# Load the interop assembly first, otherwise the compiled binder cannot resolve
# TCatSysManagerLib at runtime (FileNotFoundException, Version=3.4.0.0).
if (-not ('TwTaskBinder' -as [type])) {
    [Reflection.Assembly]::LoadFrom($tlbDll) | Out-Null
    Add-Type -TypeDefinition $binder -ReferencedAssemblies $tlbDll
}

$dte = $null
try {
    $dte = New-Object -ComObject $ProgId
    try { $dte.SuppressUI = $false } catch {}
    try { $dte.MainWindow.Visible = $Visible.IsPresent } catch {}
    if ($dte.Solution.FullName -ne $Sln) {
        $dte.Solution.Open($Sln)
        Start-Sleep -Seconds 2
    }
    $sysManager = $dte.Solution.Projects.Item(1).Object

    Write-Host "== tasks under TIRT"
    $tirt = $sysManager.LookupTreeItem('TIRT')
    foreach ($t in $tirt) { Write-Host "   - $($t.Name)  subtype=$($t.ItemSubType)" }

    # The ITcPlcTaskReference lives on the task-reference node under the nested
    # project (e.g. "...^MotionTest Project^PlcTask"), NOT on the project node.
    $projNode = $sysManager.LookupTreeItem("TIPC^$PlcName^$PlcName Project^PlcTask")
    Write-Host "== current LinkedTask: '$([TwTaskBinder]::GetLinkedTask($projNode))'"
    [TwTaskBinder]::SetLinkedTask($projNode, $TaskPath)
    Write-Host "== new LinkedTask:     '$([TwTaskBinder]::GetLinkedTask($projNode))'"

    $dte.ExecuteCommand('File.SaveAll')
    Write-Host "== saved"
}
finally {
    if ($dte) { try { $dte.Quit() } catch {} }
    try { [TwComTask]::RevokeFilter() } catch {}
}
