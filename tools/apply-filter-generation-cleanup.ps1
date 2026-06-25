param(
    [string]$RootPath = (Resolve-Path (Join-Path $PSScriptRoot ".."))
)

$ErrorActionPreference = "Stop"

$servicePath = Join-Path $RootPath "Services/FilterConditionEvaluator.cs"
$viewModelPath = Join-Path $RootPath "ViewModels/MainWindowViewModel.cs"

$serviceContent = @'
using SpecStudioParser.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpecStudioParser.Services
{
    public static class FilterConditionEvaluator
    {
        private sealed record FilterPart(bool Value, string JoinWithNext);

        public static bool Matches(DatasetConfig dataset, IReadOnlyDictionary<string, string> values)
        {
            dataset.EnsureRootFilterItems();

            if (dataset.RootFilterItems.Any())
            {
                var orderedParts = new List<FilterPart>();
                foreach (var item in dataset.RootFilterItems)
                {
                    if (item.Condition != null)
                    {
                        orderedParts.Add(new FilterPart(EvaluateCondition(item.Condition, values), item.Condition.JoinWithNext));
                    }
                    else if (item.Group != null)
                    {
                        orderedParts.Add(new FilterPart(EvaluateGroup(item.Group, values), item.Group.JoinWithNext));
                    }
                }

                return JoinParts(orderedParts);
            }

            var legacyParts = dataset.FilterConditions
                .Select(condition => new FilterPart(EvaluateCondition(condition, values), condition.JoinWithNext))
                .ToList();

            foreach (var group in dataset.RootFilterGroup.Groups)
            {
                legacyParts.Add(new FilterPart(EvaluateGroup(group, values), group.JoinWithNext));
            }

            return JoinParts(legacyParts);
        }

        private static bool EvaluateGroup(FilterConditionGroup group, IReadOnlyDictionary<string, string> values)
        {
            var parts = group.Conditions
                .Select(condition => new FilterPart(EvaluateCondition(condition, values), condition.JoinWithNext))
                .ToList();

            foreach (var childGroup in group.Groups)
            {
                parts.Add(new FilterPart(EvaluateGroup(childGroup, values), childGroup.JoinWithNext));
            }

            return JoinParts(parts);
        }

        private static bool JoinParts(IReadOnlyList<FilterPart> parts)
        {
            if (parts.Count == 0) return true;

            var result = parts[0].Value;
            for (var i = 1; i < parts.Count; i++)
            {
                var join = NormalizeJoinOperator(parts[i - 1].JoinWithNext);
                result = join == "or" ? result || parts[i].Value : result && parts[i].Value;
            }

            return result;
        }

        private static bool EvaluateCondition(FilterConditionItem condition, IReadOnlyDictionary<string, string> values)
        {
            var attribute = NormalizeAttribute(condition.Attribute);
            var op = NormalizeOperator(condition.Operator);
            var expected = condition.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(attribute) || string.IsNullOrWhiteSpace(op))
            {
                return true;
            }

            var hasValue = values.TryGetValue(attribute, out var actual);
            actual ??= string.Empty;

            return op switch
            {
                "isset" => hasValue && !string.IsNullOrWhiteSpace(actual),
                "not isset" => !hasValue || string.IsNullOrWhiteSpace(actual),
                "=" => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                "<>" => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                ">" => Compare(actual, expected) > 0,
                "<" => Compare(actual, expected) < 0,
                ">=" => Compare(actual, expected) >= 0,
                "<=" => Compare(actual, expected) <= 0,
                "like" => Contains(actual, expected),
                "contains" => Contains(actual, expected),
                "not like" => !Contains(actual, expected),
                "not contains" => !Contains(actual, expected),
                _ => true
            };
        }

        private static string NormalizeAttribute(string? attribute)
        {
            var text = (attribute ?? string.Empty).Trim();
            if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
            {
                return text[1..^1];
            }

            return text;
        }

        private static string NormalizeOperator(string? op)
        {
            return (op ?? "=").Trim().ToLowerInvariant() switch
            {
                "gt" => ">",
                "lt" => "<",
                "gte" => ">=",
                "lte" => "<=",
                "!=" => "<>",
                var value => value
            };
        }

        private static string NormalizeJoinOperator(string? joinOperator)
        {
            return string.Equals(joinOperator, "or", StringComparison.OrdinalIgnoreCase) ? "or" : "and";
        }

        private static bool Contains(string actual, string expected)
        {
            return actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int Compare(string actual, string expected)
        {
            if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var actualNumber) &&
                double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var expectedNumber))
            {
                return actualNumber.CompareTo(expectedNumber);
            }

            if (double.TryParse(actual, NumberStyles.Any, CultureInfo.CurrentCulture, out actualNumber) &&
                double.TryParse(expected, NumberStyles.Any, CultureInfo.CurrentCulture, out expectedNumber))
            {
                return actualNumber.CompareTo(expectedNumber);
            }

            return string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
'@

Set-Content -Path $servicePath -Value $serviceContent -Encoding UTF8

$content = Get-Content -Path $viewModelPath -Raw -Encoding UTF8
$oldBlock = @'
                    if (!string.IsNullOrEmpty(dataset.FilterFormula) && dataset.FilterFormula != "1")
                    {
                        bool conditionPassed = true;
                        foreach (var cond in dataset.FilterConditions)
                        {
                            string val = evalDict.ContainsKey(cond.Attribute) ? evalDict[cond.Attribute] : "";
                            if (cond.Operator == "=" && val != cond.Value) conditionPassed = false;
                            if (cond.Operator == "<>" && val == cond.Value) conditionPassed = false;
                            if (cond.Operator == ">" && string.Compare(val, cond.Value) <= 0) conditionPassed = false;
                            if (cond.Operator == "<" && string.Compare(val, cond.Value) >= 0) conditionPassed = false;
                        }
                        if (!conditionPassed) continue;
                    }
'@
$newBlock = @'
                    if (!FilterConditionEvaluator.Matches(dataset, evalDict))
                    {
                        continue;
                    }
'@

if (-not $content.Contains($oldBlock)) {
    throw "Expected legacy generation filter block was not found."
}

$content = $content.Replace($oldBlock, $newBlock)
Set-Content -Path $viewModelPath -Value $content -Encoding UTF8

$temporaryScripts = @(
    "tools/apply-filter-command-cleanup.ps1",
    "tools/apply-filter-join-polish.ps1",
    "tools/apply-filter-join-polish-bom.cmd"
)

foreach ($relativePath in $temporaryScripts) {
    $path = Join-Path $RootPath $relativePath
    if (Test-Path $path) {
        Remove-Item $path
    }
}

Write-Host "Updated generation filter logic and removed temporary scripts." -ForegroundColor Green
Write-Host "Run: dotnet build SpecStudioParser.sln -c Debug" -ForegroundColor Cyan
