param(
    [string]$RootPath = (Resolve-Path (Join-Path $PSScriptRoot ".."))
)

$ErrorActionPreference = "Stop"

$filePath = Join-Path $RootPath "ViewModels/MainWindowViewModel.cs"
if (-not (Test-Path $filePath)) {
    throw "MainWindowViewModel.cs not found: $filePath"
}

$content = Get-Content -Path $filePath -Raw -Encoding UTF8

$oldBlock = @'
        private void ExecuteAddCondition()
        {
            if (SelectedDataset == null) return;

            var item = new FilterConditionItem();
            item.PropertyChanged += (s, e) => RebuildFilterFormulaFromConditions();
            SelectedDataset.FilterConditions.Add(item);
            RebuildFilterFormulaFromConditions();
        }

        private void ExecuteRemoveCondition(FilterConditionItem? item)
        {
            if (SelectedDataset == null || item == null) return;
            SelectedDataset.FilterConditions.Remove(item);
            RebuildFilterFormulaFromConditions();
        }

        private void RebuildFilterFormulaFromConditions()
        {
            if (SelectedDataset == null) return;

            if (!SelectedDataset.FilterConditions.Any())
            {
                SelectedDataset.FilterFormula = "1";
                return;
            }

            var parts = SelectedDataset.FilterConditions
                .Where(c => !string.IsNullOrWhiteSpace(c.Attribute) && !string.IsNullOrWhiteSpace(c.Operator))
                .Select(c => $"[{c.Attribute}] {c.Operator} '{c.Value}'");

            SelectedDataset.FilterFormula = parts.Any() ? string.Join(" AND ", parts) : "1";
        }
'@

$newBlock = @'
        private void ExecuteAddCondition()
        {
            SelectedDataset?.AddRootFilterCondition();
        }

        private void ExecuteRemoveCondition(FilterConditionItem? item)
        {
            SelectedDataset?.RemoveFilterCondition(item);
        }

        private void RebuildFilterFormulaFromConditions()
        {
            SelectedDataset?.EnsureRootFilterItems();
        }
'@

if (-not $content.Contains($oldBlock)) {
    throw "Expected legacy filter command block was not found. The file may have changed."
}

$content = $content.Replace($oldBlock, $newBlock)
Set-Content -Path $filePath -Value $content -Encoding UTF8

Write-Host "Updated MainWindowViewModel legacy filter command block." -ForegroundColor Green
Write-Host "Run: dotnet build SpecStudioParser.sln -c Debug" -ForegroundColor Cyan
