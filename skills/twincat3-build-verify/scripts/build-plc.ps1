# Build a TwinCAT 3 PLC project headlessly and READ THE COMPILER ERRORS.
#
# Why the C# bridge: ToolWindows.ErrorList / OutputWindow live on EnvDTE80.DTE2.
# PowerShell COM is late-bound and resolves $dte.ToolWindows against DTE1, so the
# Error List silently comes back empty. TE1000 Automation Interface 4.3.1.6 says
# "Make sure that the ToolWindows property is available in the EnvDTE80.DTE2
# namespace" -- the same warning is in the manual's PowerShell/STA section.
# Fix = load EnvDTE.dll + EnvDTE80.dll and cast to DTE2 in C#.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build-plc.ps1
#   ... -File build-plc.ps1 -Sln <x.sln> -Cfg "Debug|TwinCAT RT (x64)" -Visible
#
# NOTE: ASCII only (PS 5.1 reads BOM-less UTF-8 as GBK). Must run in STA
#       (Windows PowerShell 5.1 is STA by default; pwsh needs -STA).

param(
    [Parameter(Mandatory=$true)][string]$Sln,
    [string]$Cfg     = 'Debug|TwinCAT RT (x64)',
    [string]$ProgId  = 'TcXaeShell.DTE.15.0',
    [string]$PlcPrj  = '',
    [switch]$Visible
)

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw "Must run in STA (use Windows PowerShell 5.1, or pwsh -STA)."
}

$asmDir   = 'C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\PublicAssemblies'
$envDte   = Join-Path $asmDir 'EnvDTE.dll'
$envDte80 = Join-Path $asmDir 'EnvDTE80.dll'
foreach ($p in @($envDte, $envDte80)) {
    if (-not (Test-Path $p)) { throw "Missing assembly: $p" }
}

Add-Type -Path $envDte
Add-Type -Path $envDte80

$cs = @'
using System;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;

public class TwError {
    public int    Level       { get; set; }
    public string Description { get; set; }
    public string FileName    { get; set; }
    public int    Line        { get; set; }
    public int    Column      { get; set; }
    public string Project     { get; set; }
}

public static class TwBuild {
    private static DTE  _dte;
    private static DTE2 _dte2;

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

    public static void Open(string sln, bool visible) {
        Type t = Type.GetTypeFromProgID("TcXaeShell.DTE.15.0", true);
        object raw = Activator.CreateInstance(t);
        _dte  = (DTE)raw;
        _dte2 = (DTE2)raw;          // <-- the whole point: early binding to DTE2
        _dte.SuppressUI = false;
        try { _dte.MainWindow.Visible = visible; } catch {}
        try {
            object settings = _dte.GetObject("TcAutomationSettings");
            settings.GetType().InvokeMember("SilentMode",
                System.Reflection.BindingFlags.SetProperty, null, settings, new object[] { true });
        } catch {}
        _dte.Solution.Open(sln);
    }

    public static string Configs() {
        SolutionConfigurations all = _dte.Solution.SolutionBuild.SolutionConfigurations;
        System.Text.StringBuilder names = new System.Text.StringBuilder();
        for (int i = 1; i <= all.Count; i++) {
            if (names.Length > 0) { names.Append(", "); }
            names.Append(all.Item(i).Name);
        }
        return names.ToString();
    }

    // cfg carries the platform ("Debug|TwinCAT RT (x64)"); prj may be empty to
    // build the whole solution instead of one project.
    public static int Build(string cfg, string prj) {
        if (string.IsNullOrEmpty(prj)) {
            _dte.Solution.SolutionBuild.Build(true);
        } else {
            _dte.Solution.SolutionBuild.BuildProject(cfg, prj, true);
        }
        while (_dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress) {
            System.Threading.Thread.Sleep(500);
        }
        return _dte.Solution.SolutionBuild.LastBuildInfo;
    }

    public static TwError[] Errors() {
        ErrorItems items = _dte2.ToolWindows.ErrorList.ErrorItems;
        int n = items.Count;
        TwError[] arr = new TwError[n];
        for (int i = 1; i <= n; i++) {
            ErrorItem it = items.Item(i);
            arr[i - 1] = new TwError {
                Level       = (int)it.ErrorLevel,
                Description = it.Description,
                FileName    = it.FileName,
                Line        = it.Line,
                Column      = it.Column,
                Project     = it.Project,
            };
        }
        return arr;
    }

    public static void Quit() {
        if (_dte != null) { try { _dte.Quit(); } catch {} _dte = null; _dte2 = null; }
    }
}
'@
Add-Type -TypeDefinition $cs -ReferencedAssemblies @($envDte, $envDte80)

[TwBuild]::RegisterFilter()
try {
    Write-Host "== creating DTE ($ProgId), visible=$($Visible.IsPresent)"
    [TwBuild]::Open($Sln, $Visible.IsPresent)

    Write-Host "== solution configurations: $([TwBuild]::Configs())"
    Write-Host "== building: $Cfg"
    if ($PlcPrj) { Write-Host "   project: $PlcPrj" } else { Write-Host "   (whole solution)" }
    $info = [TwBuild]::Build($Cfg, $PlcPrj)
    Write-Host "== LastBuildInfo = $info   (0 = OK, N = N failed projects)"

    $errs = [TwBuild]::Errors()
    Write-Host "== ErrorList: $($errs.Count) item(s)"
    foreach ($e in $errs) {
        $lvl = switch ($e.Level) { 1 { 'error' } 2 { 'warning' } 3 { 'message' } default { "lvl$($e.Level)" } }
        $loc = ''
        if ($e.FileName) { $loc = "$($e.FileName)($($e.Line),$($e.Column)): " }
        Write-Host "   $lvl $loc$($e.Description)"
    }

    if ($info -eq 0 -and $errs.Count -eq 0) { Write-Host "== BUILD OK" } else { Write-Host "== BUILD FAILED" }
}
finally {
    [TwBuild]::Quit()
    [TwBuild]::RevokeFilter()
}
