param(
    [string]$RootPath = (Resolve-Path (Join-Path $PSScriptRoot ".."))
)

$ErrorActionPreference = "Stop"

$filePath = Join-Path $RootPath "Views/SpecificationSettingsWindow.axaml"
if (-not (Test-Path $filePath)) {
    throw "SpecificationSettingsWindow.axaml not found: $filePath"
}

$content = Get-Content -Path $filePath -Raw -Encoding UTF8

$replacements = @(
    @{
        Old = 'Text="Связь по умолчанию:"'
        New = 'Text="Связь старых условий:"'
    },
    @{
        Old = '<ComboBox Width="90" ItemsSource="{Binding SelectedDataset.RootFilterGroup.AvailableJoinOperators}" SelectedItem="{Binding SelectedDataset.RootFilterGroup.JoinOperator, Mode=TwoWay}"/>'
        New = '<ComboBox Width="90" ItemsSource="{Binding SelectedDataset.RootFilterGroup.AvailableJoinOperators}" SelectedItem="{Binding SelectedDataset.RootFilterGroup.JoinOperator, Mode=TwoWay}" ToolTip.Tip="Используется только для совместимости со старым плоским списком условий"/>'
    },
    @{
        Old = 'Text="Порядок элементов сохраняется в общем списке"'
        New = 'Text="В строках и группах выбирается связь со следующим элементом; у последнего элемента она не учитывается"'
    },
    @{
        Old = 'ToolTip.Tip="Связь со следующим элементом"'
        New = 'ToolTip.Tip="Связь со следующим элементом; у последнего элемента не используется"'
    },
    @{
        Old = 'Text="Группа, связь дальше:"'
        New = 'Text="Группа, связь со следующим:"'
    },
    @{
        Old = '<ComboBox Width="90" ItemsSource="{Binding Group.AvailableJoinOperators}" SelectedItem="{Binding Group.JoinWithNext, Mode=TwoWay}"/>'
        New = '<ComboBox Width="90" ItemsSource="{Binding Group.AvailableJoinOperators}" SelectedItem="{Binding Group.JoinWithNext, Mode=TwoWay}" ToolTip.Tip="Связь со следующим корневым элементом; у последней группы не используется"/>'
    },
    @{
        Old = 'ToolTip.Tip="Связь со следующим условием"'
        New = 'ToolTip.Tip="Связь со следующим условием внутри группы; у последнего условия не используется"'
    }
)

foreach ($replacement in $replacements) {
    if (-not $content.Contains($replacement.Old)) {
        throw "Expected text was not found: $($replacement.Old)"
    }

    $content = $content.Replace($replacement.Old, $replacement.New)
}

Set-Content -Path $filePath -Value $content -Encoding UTF8

Write-Host "Updated filter join labels and tooltips." -ForegroundColor Green
Write-Host "Run: dotnet build SpecStudioParser.sln -c Debug" -ForegroundColor Cyan
