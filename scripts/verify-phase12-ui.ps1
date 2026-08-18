[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [string]$PageCase = 'dashboard/empty',
    [string]$Theme = 'light',
    [string]$Size = 'normal',
    [string]$RequiredScale = '100',
    [string]$OutputDirectory,
    [switch]$CaptureAllForCurrentScale
)

$ErrorActionPreference = 'Stop'
$previewCases = @(
    [pscustomobject]@{ Page = 'dashboard';  Case = 'empty';                StateCategory = 'empty' }
    [pscustomobject]@{ Page = 'dashboard';  Case = 'long-text';            StateCategory = 'long-text' }
    [pscustomobject]@{ Page = 'meetings';   Case = 'empty';                StateCategory = 'empty' }
    [pscustomobject]@{ Page = 'meetings';   Case = 'long-text';            StateCategory = 'long-text' }
    [pscustomobject]@{ Page = 'search';     Case = 'empty';                StateCategory = 'empty' }
    [pscustomobject]@{ Page = 'dictionary'; Case = 'empty';                StateCategory = 'empty' }
    [pscustomobject]@{ Page = 'models';     Case = 'ready';                StateCategory = 'ready' }
    [pscustomobject]@{ Page = 'models';     Case = 'downloading';          StateCategory = 'loading' }
    [pscustomobject]@{ Page = 'models';     Case = 'failure';              StateCategory = 'failure' }
    [pscustomobject]@{ Page = 'models';     Case = 'offline';              StateCategory = 'offline' }
    [pscustomobject]@{ Page = 'shortcuts';  Case = 'conflict';             StateCategory = 'failure' }
    [pscustomobject]@{ Page = 'settings';   Case = 'normal';               StateCategory = 'normal' }
    [pscustomobject]@{ Page = 'settings';   Case = 'startup-unavailable';  StateCategory = 'failure' }
    [pscustomobject]@{ Page = 'about';      Case = 'diagnostics';          StateCategory = 'normal' }
    [pscustomobject]@{ Page = 'onboarding'; Case = 'welcome';              StateCategory = 'empty' }
    [pscustomobject]@{ Page = 'onboarding'; Case = 'long-text';            StateCategory = 'long-text' }
    [pscustomobject]@{ Page = 'onboarding'; Case = 'downloading';          StateCategory = 'loading' }
    [pscustomobject]@{ Page = 'onboarding'; Case = 'failure';              StateCategory = 'failure' }
    [pscustomobject]@{ Page = 'onboarding'; Case = 'offline';              StateCategory = 'offline' }
    [pscustomobject]@{ Page = 'onboarding'; Case = 'permissions-denied';   StateCategory = 'failure' }
    [pscustomobject]@{ Page = 'onboarding'; Case = 'completed';            StateCategory = 'completed' }
    [pscustomobject]@{ Page = 'tour';       Case = 'feature-tour';         StateCategory = 'normal' }
)
$pageCases = @($previewCases | ForEach-Object { "$($_.Page)/$($_.Case)" })
$caseByPageCase = @{}
foreach ($previewCase in $previewCases) {
    $key = "$($previewCase.Page)/$($previewCase.Case)"
    if ($caseByPageCase.ContainsKey($key)) { throw "Phase 12 preview case table contains a duplicate '$key'." }
    $caseByPageCase[$key] = $previewCase
}
if ($previewCases.Count -ne 22 -or $caseByPageCase.Count -ne $previewCases.Count) { throw 'Phase 12 preview case table must contain 22 unique page/cases.' }
$themes = @('light', 'dark')
$sizes = @('narrow', 'normal')
$scales = @('100', '125', '150', '200')
$dpiByScale = @{ '100' = 96; '125' = 120; '150' = 144; '200' = 192 }

function Assert-ExactEnum([string]$Name, [string]$Value, [string[]]$Allowed) {
    if ($Allowed -cnotcontains $Value) {
        throw "$Name must be exactly one of: $($Allowed -join ', '). Received '$Value'."
    }
}

Assert-ExactEnum 'PageCase' $PageCase $pageCases
Assert-ExactEnum 'Theme' $Theme $themes
Assert-ExactEnum 'Size' $Size $sizes
Assert-ExactEnum 'RequiredScale' $RequiredScale $scales

Write-Host 'Phase 12 actual-product-page visual verification matrix'
Write-Host 'Isolation: preview arguments are parsed before logging, single-instance, startup repair, production services, stores, tray, hooks, microphones, model/cache, credentials, network, registry, or user-data composition.'
Write-Host 'Viewport sizes are WPF DIPs only; they are not DPI evidence.'

if ($ValidateOnly) {
    $count = 0
    foreach ($matrixPageCase in $pageCases) {
        foreach ($matrixTheme in $themes) {
            foreach ($matrixSize in $sizes) {
                foreach ($matrixScale in $scales) { $count++ }
            }
        }
    }
    Write-Host "ValidateOnly completed: $count cells (22 exact page/cases x 2 themes x 2 viewport sizes x 4 required scales)."
    Write-Host "Coverage: $($pageCases -join '; ')"
    Write-Host 'No application was launched and no files, user data, registry values, logs, or screenshots were written.'
    exit 0
}

if ($CaptureAllForCurrentScale) {
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { throw 'OutputDirectory is required for CaptureAllForCurrentScale.' }
    Write-Host "CaptureAllForCurrentScale: capturing all 22 page/cases x 2 themes x 2 sizes at the one verified $RequiredScale% scale. No other scale or monitor is claimed."
    foreach ($matrixPageCase in $pageCases) {
        foreach ($matrixTheme in $themes) {
            foreach ($matrixSize in $sizes) {
                & $PSCommandPath -PageCase $matrixPageCase -Theme $matrixTheme -Size $matrixSize -RequiredScale $RequiredScale -OutputDirectory $OutputDirectory
                if ($LASTEXITCODE -ne 0) { throw "Capture failed for $matrixPageCase / $matrixTheme / $matrixSize; stopping the bounded current-scale run." }
            }
        }
    }
    exit 0
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    throw 'OutputDirectory is required for a capture. It is the only directory this script writes to.'
}

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'windows-native\Muesli.Windows\bin\Debug\net8.0-windows\Muesli.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Build output missing: $exe" }

$outputFull = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputFull | Out-Null
$outputPrefix = $outputFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Phase12Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public struct MONITORINFOEX {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@

$startedPreview = $null
try {
    $arguments = @("--phase12-page-case=$PageCase", "--phase12-theme=$Theme", "--phase12-size=$Size")
    $startedPreview = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $exe) -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $hWnd = [IntPtr]::Zero
    while ([DateTime]::UtcNow -lt $deadline) {
        $startedPreview.Refresh()
        if ($startedPreview.HasExited) { throw "Preview process exited before creating a window. Exit code: $($startedPreview.ExitCode)." }
        if ($startedPreview.MainWindowHandle -ne [IntPtr]::Zero) { $hWnd = $startedPreview.MainWindowHandle; break }
        Start-Sleep -Milliseconds 200
    }
    if ($hWnd -eq [IntPtr]::Zero) { throw 'Timed out waiting for the preview process to expose an actual HWND.' }

    $foregroundDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while ([DateTime]::UtcNow -lt $foregroundDeadline -and [Phase12Native]::GetForegroundWindow() -ne $hWnd) {
        [void][Phase12Native]::ShowWindow($hWnd, 9) # SW_RESTORE
        [void][Phase12Native]::SetForegroundWindow($hWnd)
        Start-Sleep -Milliseconds 200
    }
    if ([Phase12Native]::GetForegroundWindow() -ne $hWnd) {
        throw 'The preview HWND could not be foregrounded in this interactive session; refusing to capture a possibly obscured window.'
    }

    $requiredDpi = [int]$dpiByScale[$RequiredScale]
    $actualDpi = [int][Phase12Native]::GetDpiForWindow($hWnd)
    if ($actualDpi -ne $requiredDpi) {
        throw "RequiredScale $RequiredScale% requires $requiredDpi DPI, but GetDpiForWindow returned $actualDpi DPI. Move the same preview to a monitor/VM at the required Windows scale and rerun; this script never changes Windows scale."
    }

    $rect = New-Object Phase12Native+RECT
    if (-not [Phase12Native]::GetWindowRect($hWnd, [ref]$rect)) { throw 'GetWindowRect failed for the preview HWND.' }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { throw "Preview HWND has invalid visible bounds: $width x $height." }

    $monitor = [Phase12Native]::MonitorFromWindow($hWnd, 2) # MONITOR_DEFAULTTONEAREST
    if ($monitor -eq [IntPtr]::Zero) { throw 'MonitorFromWindow returned no monitor for the preview HWND.' }
    $monitorInfo = New-Object Phase12Native+MONITORINFOEX
    $monitorInfo.cbSize = [Runtime.InteropServices.Marshal]::SizeOf([type]'Phase12Native+MONITORINFOEX')
    if (-not [Phase12Native]::GetMonitorInfo($monitor, [ref]$monitorInfo)) { throw 'GetMonitorInfo failed for the preview monitor.' }

    $stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $safeCase = $PageCase.Replace('/', '-')
    $pngPath = [IO.Path]::GetFullPath((Join-Path $outputFull "phase12-$safeCase-$Theme-$Size-$RequiredScale-$stamp.png"))
    $manifestPath = [IO.Path]::GetFullPath((Join-Path $outputFull "phase12-$safeCase-$Theme-$Size-$RequiredScale-$stamp.json"))
    if (-not $pngPath.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not $manifestPath.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to write outside OutputDirectory.'
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size, [System.Drawing.CopyPixelOperation]::SourceCopy)
        $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    $manifest = [ordered]@{
        pageCase = $PageCase
        page = $PageCase.Split('/')[0]
        case = $PageCase.Split('/')[1]
        stateCategory = $caseByPageCase[$PageCase].StateCategory
        theme = $Theme
        size = $Size
        requestedViewportDips = if ($Size -ceq 'narrow') { '780x600' } else { '980x720' }
        actualWindowViewportDips = "$([Math]::Round($width / ($actualDpi / 96.0), 2))x$([Math]::Round($height / ($actualDpi / 96.0), 2))"
        requiredScalePercent = [int]$RequiredScale
        requiredDpi = $requiredDpi
        actualDpi = $actualDpi
        hwnd = ('0x{0:X}' -f $hWnd.ToInt64())
        monitor = [ordered]@{
            handle = ('0x{0:X}' -f $monitor.ToInt64())
            device = $monitorInfo.szDevice
            bounds = [ordered]@{ left = $monitorInfo.rcMonitor.Left; top = $monitorInfo.rcMonitor.Top; right = $monitorInfo.rcMonitor.Right; bottom = $monitorInfo.rcMonitor.Bottom }
            workArea = [ordered]@{ left = $monitorInfo.rcWork.Left; top = $monitorInfo.rcWork.Top; right = $monitorInfo.rcWork.Right; bottom = $monitorInfo.rcWork.Bottom }
        }
        windowBounds = [ordered]@{ left = $rect.Left; top = $rect.Top; right = $rect.Right; bottom = $rect.Bottom; width = $width; height = $height }
        timestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
        pngPath = $pngPath
        isolationMarker = 'phase12 preview parse-first; actual MainWindow XAML or actual onboarding/tour; no production composition or user-data writes'
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Captured actual foreground preview HWND to: $pngPath"
    Write-Host "Manifest: $manifestPath (GetDpiForWindow=$actualDpi; monitor=$($monitorInfo.szDevice))"
}
finally {
    if ($null -ne $startedPreview -and -not $startedPreview.HasExited) {
        Stop-Process -Id $startedPreview.Id -ErrorAction SilentlyContinue
    }
}
