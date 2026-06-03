using SpecStudioParser.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpecStudioParser.Services
{
    public static class ModelStudioFilterService
    {
        public static bool EvaluateConditions(IReadOnlyDictionary<string, string> values, IEnumerable<FilterConditionItem> conditions)
        {
            foreach (var condition in conditions)
            {
                if (!EvaluateCondition(values, condition))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool EvaluateCondition(IReadOnlyDictionary<string, string> values, FilterConditionItem condition)
        {
            var attribute = NormalizeAttributeName(condition.Attribute);
            var op = NormalizeOperator(condition.Operator);
            var expected = condition.Value ?? string.Empty;
            var hasValue = TryGetValue(values, attribute, out var actual);
            actual ??= string.Empty;

            if (op == "isset") return hasValue && !string.IsNullOrWhiteSpace(actual);
            if (op == "not isset") return !hasValue || string.IsNullOrWhiteSpace(actual);

            if (!hasValue && IsNumericComparison(op)) return false;

            return op switch
            {
                "=" or "==" => CompareValues(actual, expected) == 0,
                "<>" or "!=" => CompareValues(actual, expected) != 0,
                ">" => CompareValues(actual, expected) > 0,
                "<" => CompareValues(actual, expected) < 0,
                ">=" => CompareValues(actual, expected) >= 0,
                "<=" => CompareValues(actual, expected) <= 0,
                "like" => IsLikeMatch(actual, expected),
                "not like" => !IsLikeMatch(actual, expected),
                "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
                "not contains" => !actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            };
        }

        public static bool EvaluateFormula(IReadOnlyDictionary<string, string> values, string? formula)
        {
            if (string.IsNullOrWhiteSpace(formula) || formula.Trim() == "1") return true;

            var orParts = Regex.Split(formula, @"\s+or\s+", RegexOptions.IgnoreCase);
            foreach (var orPart in orParts)
            {
                var andParts = Regex.Split(orPart, @"\s+and\s+", RegexOptions.IgnoreCase);
                if (andParts.All(part => EvaluateFormulaAtom(values, part)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EvaluateFormulaAtom(IReadOnlyDictionary<string, string> values, string expression)
        {
            var text = expression.Trim();
            if (string.IsNullOrWhiteSpace(text) || text == "1") return true;
            if (text == "0") return false;

            var issetMatch = Regex.Match(text, @"^(?<not>not\s+)?isset\s*\(\s*(?<attr>[^\)]+)\s*\)$", RegexOptions.IgnoreCase);
            if (issetMatch.Success)
            {
                var attribute = issetMatch.Groups["attr"].Value;
                var op = issetMatch.Groups["not"].Success ? "not isset" : "isset";
                return EvaluateCondition(values, new FilterConditionItem { Attribute = attribute, Operator = op });
            }

            var likeMatch = Regex.Match(text, @"^(?<attr>.+?)\s+(?<op>not\s+like|like)\s+(?<value>.+)$", RegexOptions.IgnoreCase);
            if (likeMatch.Success)
            {
                return EvaluateCondition(values, new FilterConditionItem
                {
                    Attribute = likeMatch.Groups["attr"].Value,
                    Operator = likeMatch.Groups["op"].Value,
                    Value = Unquote(likeMatch.Groups["value"].Value)
                });
            }

            var compareMatch = Regex.Match(text, @"^(?<attr>.+?)\s*(?<op>>=|<=|<>|!=|==|=|>|<)\s*(?<value>.+)$", RegexOptions.IgnoreCase);
            if (compareMatch.Success)
            {
                return EvaluateCondition(values, new FilterConditionItem
                {
                    Attribute = compareMatch.Groups["attr"].Value,
                    Operator = compareMatch.Groups["op"].Value,
                    Value = Unquote(compareMatch.Groups["value"].Value)
                });
            }

            return false;
        }

        private static int CompareValues(string actual, string expected)
        {
            if (TryParseModelStudioNumber(actual, out var actualNumber) && TryParseModelStudioNumber(expected, out var expectedNumber))
            {
                return actualNumber.CompareTo(expectedNumber);
            }

            return string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNumericComparison(string op) => op is ">" or "<" or ">=" or "<=";

        private static bool TryGetValue(IReadOnlyDictionary<string, string> values, string attribute, out string value)
        {
            value = string.Empty;
            if (values.TryGetValue(attribute, out var directValue))
            {
                value = directValue;
                return true;
            }

            var shortName = NormalizeAttributeName(attribute);
            var match = values.FirstOrDefault(pair => string.Equals(NormalizeAttributeName(pair.Key), shortName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Key))
            {
                value = match.Value;
                return true;
            }

            return false;
        }

        private static string NormalizeAttributeName(string? attribute)
        {
            var name = (attribute ?? string.Empty).Trim();
            name = Unquote(name);
            if (name.StartsWith("[", StringComparison.Ordinal) && name.EndsWith("]", StringComparison.Ordinal))
            {
                name = name[1..^1];
            }

            foreach (var prefix in new[] { "root.", "current.", "object." })
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[prefix.Length..];
                    break;
                }
            }

            if (name.StartsWith("parameters[", StringComparison.OrdinalIgnoreCase))
            {
                var parameterMatch = Regex.Match(name, "parameters\\[\\\"(?<name>.+?)\\\"\\]", RegexOptions.IgnoreCase);
                if (parameterMatch.Success) name = parameterMatch.Groups["name"].Value;
            }

            if (name.StartsWith("[", StringComparison.Ordinal) && name.EndsWith("]", StringComparison.Ordinal))
            {
                name = name[1..^1];
            }

            return name.Trim();
        }

        private static string NormalizeOperator(string? op)
        {
            var value = (op ?? "=").Trim().ToLowerInvariant();
            return value switch
            {
                "содержит" => "contains",
                "не содержит" => "not contains",
                "не равно" => "<>",
                _ => value
            };
        }

        private static bool TryParseModelStudioNumber(string? value, out double number)
        {
            var text = (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        private static bool IsLikeMatch(string actual, string pattern)
        {
            var normalizedPattern = Unquote(pattern);
            var regexPattern = "^" + Regex.Escape(normalizedPattern)
                .Replace("%", ".*")
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return Regex.IsMatch(actual ?? string.Empty, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string Unquote(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if ((text.StartsWith("\"", StringComparison.Ordinal) && text.EndsWith("\"", StringComparison.Ordinal)) ||
                (text.StartsWith("'", StringComparison.Ordinal) && text.EndsWith("'", StringComparison.Ordinal)))
            {
                return text[1..^1];
            }

            return text;
        }
    }
}