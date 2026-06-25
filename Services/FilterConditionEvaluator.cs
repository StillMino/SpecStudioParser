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
