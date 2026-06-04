using SpecStudioParser.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpecStudioParser.Services
{
    public static class FilterFormulaBuilder
    {
        public static string BuildFromFlatConditions(IEnumerable<FilterConditionItem> conditions)
        {
            return BuildFromFlatConditions(conditions, "and");
        }

        public static string BuildFromFlatConditions(IEnumerable<FilterConditionItem> conditions, string? joinOperator)
        {
            var parts = BuildConditionParts(conditions);
            if (!parts.Any()) return "1";

            var normalizedJoin = NormalizeJoinOperator(joinOperator);
            return string.Join($" {normalizedJoin} ", parts);
        }

        public static string BuildFromRoot(FilterConditionGroup rootGroup, IEnumerable<FilterConditionItem> rootConditions)
        {
            var parts = BuildConditionParts(rootConditions);

            foreach (var childGroup in rootGroup.Groups)
            {
                var childExpression = BuildFromGroup(childGroup);
                if (!string.IsNullOrWhiteSpace(childExpression) && childExpression != "1")
                {
                    parts.Add($"({childExpression})");
                }
            }

            if (!parts.Any()) return "1";

            var joinOperator = NormalizeJoinOperator(rootGroup.JoinOperator);
            return string.Join($" {joinOperator} ", parts);
        }

        public static string BuildFromGroup(FilterConditionGroup group)
        {
            var parts = BuildConditionParts(group.Conditions);

            foreach (var childGroup in group.Groups)
            {
                var childExpression = BuildFromGroup(childGroup);
                if (!string.IsNullOrWhiteSpace(childExpression) && childExpression != "1")
                {
                    parts.Add($"({childExpression})");
                }
            }

            if (!parts.Any()) return "1";

            var joinOperator = NormalizeJoinOperator(group.JoinOperator);
            return string.Join($" {joinOperator} ", parts);
        }

        public static string BuildConditionExpression(FilterConditionItem condition)
        {
            var attribute = NormalizeAttribute(condition.Attribute);
            var op = NormalizeOperatorForFormula(condition.Operator);
            var value = condition.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(attribute) || string.IsNullOrWhiteSpace(op))
            {
                return string.Empty;
            }

            if (op == "isset") return $"isset([{attribute}])";
            if (op == "not isset") return $"not isset([{attribute}])";

            return $"[{attribute}] {op} {Quote(value)}";
        }

        private static List<string> BuildConditionParts(IEnumerable<FilterConditionItem> conditions)
        {
            return conditions
                .Where(c => !string.IsNullOrWhiteSpace(c.Attribute) && !string.IsNullOrWhiteSpace(c.Operator))
                .Select(BuildConditionExpression)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();
        }

        private static string NormalizeJoinOperator(string? joinOperator)
        {
            return string.Equals(joinOperator, "or", StringComparison.OrdinalIgnoreCase) ? "or" : "and";
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

        private static string NormalizeOperatorForFormula(string? op)
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

        private static string Quote(string value)
        {
            var escaped = value.Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
    }
}