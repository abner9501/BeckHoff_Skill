# Build a TC3 PLC project and dump the FULL Output window text (all panes) to
# a file.  ErrorList only carries what the IDE chose to surface; the Build pane
# has the real compiler chatter.  Reading the panes needs early binding to
# EnvDTE80.DTE2 -- PowerShell late binding cannot enumerate OutputWindowPanes.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File dump-build-output.ps1 `
#       -Sln <x.sln> -PlcPrj <x.plcproj> -Out <log.txt>

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [Parameter(Mandatory=$true)][string]$PlcPrj,
    [string]$Out    = (Join-Path $env:TEMP 'twincat-build-output.log'),
    [string]$Cfg    = 'Debug|TwinCAT RT (x64)',
    [string]$ProgId = 'TcXaeShell.DTE.15.0'
)

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Must run in STA (use Windows PowerShell 5.1).'
}

$asmDir   = 'C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\PublicAssemblies'
$envDte   = Join-Path $asmDir 'EnvDTE.dll'
$envDte80 = Join-Path $asmDir 'EnvDTE80.dll'
Add-Type -Path $envDte
Add-Type -Path $envDte80

$cs = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using EnvDTE;
using EnvDTE80;

public static class TwOut {
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

    public static string Dump(object dteObj) {
        DTE2 dte2 = (DTE2)dteObj;
        OutputWindow ow = dte2.ToolWindows.OutputWindow;
        StringBuilder sb = new StringBuilder();
        foreach (OutputWindowPane p in ow.OutputWindowPanes) {
            sb.AppendLine("##### PANE: " + p.Name + " #####");
            try {
                TextDocument td = p.TextDocument;
                sb.AppendLine(td.StartPoint.CreateEditPoint().GetText(td.EndPoint));
            } catch (Exception e) {
                sb.AppendLine("(cannot read pane: " + e.Message + ")");
            }
        }
        return sb.ToString();
    }
}
'@
Add-Type -TypeDefinition $cs -ReferencedAssemblies @($envDte, $envDte80)

[TwOut]::RegisterFilter()

$dte = $null
try {
    $dte = New-Object -ComObject $ProgId
    try { $dte.SuppressUI = $false } catch {}
    try { $dte.MainWindow.Visible = $false } catch {}
    try { $dte.GetObject('TcAutomationSettings').SilentMode = $true } catch {}

    $dte.Solution.Open($Sln)
    Write-Host "== building '$PlcPrj' ($Cfg)"
    $dte.Solution.SolutionBuild.BuildProject($Cfg, $PlcPrj, $true)
    while ($dte.Solution.SolutionBuild.BuildState -eq 1) { Start-Sleep -Milliseconds 500 }
    Write-Host "== LastBuildInfo = $($dte.Solution.SolutionBuild.LastBuildInfo)"

    $txt = [TwOut]::Dump($dte)
    Set-Content -Path $Out -Value $txt -Encoding UTF8
    Write-Host "== output written to $Out  ($($txt.Length) chars)"
}
finally {
    if ($dte) { try { $dte.Quit() } catch {} }
    try { [TwOut]::RevokeFilter() } catch {}
}
