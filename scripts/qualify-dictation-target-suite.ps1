param(
    [Parameter(Mandatory = $true)]
    [string[]]$ReportPaths,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$reports = @($ReportPaths | ForEach-Object {
    $path = (Resolve-Path -LiteralPath $_).Path
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($report.schemaVersion -ne 2 -or -not $report.passed -or -not $report.textVerified) {
        throw "Target report is not a passed, human-verified schema-2 report: $path"
    }
    $report
})
$requiredKinds = @("Notepad", "Chrome", "Office", "Other")
$missing = @($requiredKinds | Where-Object { $_ -notin @($reports.targetKind) })
$models = @($reports.requiredModelId | Sort-Object -Unique)
$traceIds = @($reports.traceId | Sort-Object -Unique)
if ($missing.Count -gt 0) { throw "Missing target qualifications: $($missing -join ', ')" }
if ($models.Count -ne 1) { throw "Target reports do not use one explicit dictation model identity." }
if ($traceIds.Count -ne $reports.Count) { throw "Every target qualification must use a distinct real dictation trace." }

$summary = [ordered]@{
    schemaVersion = 1
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    modelId = $models[0]
    targetKinds = @($reports.targetKind)
    passed = $true
    reports = $reports
}
$json = $summary | ConvertTo-Json -Depth 10
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
    Set-Content -LiteralPath $resolvedOutput -Value $json -Encoding UTF8
}
Write-Host "Dictation target suite passed for Notepad, Chrome, Office, and another editor."
$json
