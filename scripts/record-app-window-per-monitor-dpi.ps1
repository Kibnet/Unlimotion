[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Output,

    [string]$WindowTitle,

    [string]$ProcessName,

    [ValidateRange(1, 3600)]
    [int]$DurationSeconds = 20,

    [ValidateRange(1, 120)]
    [int]$Fps = 30,

    [ValidateRange(0, 200)]
    [int]$Padding = 0,

    [switch]$NoCursor,

    [switch]$PreviewRegion
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$recorderScriptPath = $env:UNLIMOTION_RECORD_APP_SCREEN_SCRIPT
if ([string]::IsNullOrWhiteSpace($recorderScriptPath) -or
    -not (Test-Path -LiteralPath $recorderScriptPath -PathType Leaf)) {
    throw "UNLIMOTION_RECORD_APP_SCREEN_SCRIPT must identify the record-app-screen skill script."
}

# The bundled recorder uses the legacy SetProcessDPIAware API. On a desktop
# whose monitors use different scale factors, that API reports the target
# window in system-DPI coordinates while ffmpeg/gdigrab consumes physical
# desktop pixels. Preloading the recorder's native type lets its existing
# SetProcessDPIAware call opt into Per-Monitor DPI v2 instead.
if (-not ("NativeWindowCapture" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class NativeWindowCapture
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    private static extern bool GetWindowRectNative(IntPtr hWnd, out RECT rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hWnd,
        int attribute,
        out RECT value,
        int valueSize);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    public static bool SetProcessDPIAware()
    {
        return SetProcessDpiAwarenessContext(new IntPtr(-4));
    }

    public static bool GetWindowRect(IntPtr hWnd, out RECT rect)
    {
        const int ExtendedFrameBounds = 9;
        if (DwmGetWindowAttribute(
                hWnd,
                ExtendedFrameBounds,
                out rect,
                Marshal.SizeOf<RECT>()) == 0 &&
            rect.Right > rect.Left &&
            rect.Bottom > rect.Top)
        {
            return true;
        }

        return GetWindowRectNative(hWnd, out rect);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@
}

$recorderArguments = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $recorderArguments[$entry.Key] = $entry.Value
}

& $recorderScriptPath @recorderArguments
exit $LASTEXITCODE
