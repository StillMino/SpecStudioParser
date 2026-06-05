param(
    [string]$RootPath = (Resolve-Path (Join-Path $PSScriptRoot ".."))
)

$ErrorActionPreference = "Stop"

$legacyPatterns = @(
    "AddConditionCommand",
    "RemoveConditionCommand",
    "RebuildFilterFormulaFromConditions"
)

$searchRoots = @(
    (Join-Path $RootPath "Views"),
    (Join-Path $RootPath "Models"),
    (Join-Path $RootPath "Services")
)

$files = foreach ($path in $searchRoots) {
    if (Test-Path $path) {
        Get-ChildItem -Path $path -Recurse -File -Include *.axaml,*.cs
    }
}

$matches = @()
foreach ($file in $files) {
    foreach ($pattern in $legacyPatterns) {
        $found = Select-String -Path $file.FullName -Pattern $pattern -SimpleMatch
        if ($found) {
            $matches += $found
        }
    }
}

if ($matches.Count -gt 0) {
    Write-Host "Legacy filter editor bindings/usages found:" -ForegroundColor Red
    foreach ($match in $matches) {
        Write-Host "$($match.Path):$($match.LineNumber): $($match.Line.Trim())"
    }
    exit 1
}

Write-Host "No legacy filter editor bindings found outside MainWindowViewModel." -ForegroundColor Green
