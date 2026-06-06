using SpecStudioParser.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpecStudioParser.Services
{
    public static class FilterFormulaBuilder
    {
        private sealed record FormulaPart(string Expression, string JoinWithNext);

        public static string BuildFromFlatConditions(IEnumerable<FilterConditionItem> conditions)
        {
            return BuildFromFlatConditions(conditions, "and");
        }

        public static string BuildFromFlatConditions(IEnumerable<FilterConditionItem> conditions, string? joinOperator)
        {
            var parts = BuildConditionParts(conditions)
                .Select(expression => new FormulaPart(expression, joinOperator ?? "and"))
                .ToList();

            return BuildJoinedExpression(parts);
        }

        public static string BuildFromRoot(FilterConditionGroup rootGroup, IEnumerable<FilterConditionItem> rootConditions)
        {
            return BuildFromRoot(rootGroup, rootConditions, Enumerable.Empty<FilterRootItem>());
        }

        public static string BuildFromRoot(FilterConditionGroup rootGroup, IEnumerable<FilterConditionItem> rootConditions, IEnumerable<FilterRootItem> rootItems)
        {
            rootGroup.EnsureItems();

            var orderedItems = rootItems.ToList();
            if (orderedItems.Any())
            {
                var orderedParts = new List<FormulaPart>();

                foreach (var item in orderedItems)
                {
                    if (item.Condition != null)
                    {
                        var expression = BuildConditionExpression(item.Condition);
                        if (!string.IsNullOrWhiteSpace(expression))
                        {
                            orderedParts.Add(new FormulaPart(expression, item.Condition.JoinWithNext));
                        }
                    }
                    else if (item.Group != null)
                    {
                        var childExpression = BuildFromGroup(item.Group);
                        if (!string.IsNullOrWhiteSpace(childExpression) && childExpression != "1")
                        {
                            orderedParts.Add(new FormulaPart($"({childExpression})", item.Group.JoinWithNext));
                        }
                    }
                }

                return BuildJoinedExpression(orderedParts);
            }

            return BuildFromGroup(rootGroup, rootConditions);
        }

        public static string BuildFromGroup(FilterConditionGroup group)
        {
            return BuildFromGroup(group, Enumerable.Empty<FilterConditionItem>());
        }

        private static string BuildFromGroup(FilterConditionGroup group, IEnumerable<FilterConditionItem> fallbackConditions)
        {
            group.EnsureItems();

            var orderedParts = new List<FormulaPart>();
            foreach (var item in group.Items)
            {
                if (item.Condition != null)
                {
                    var expression = BuildConditionExpression(item.Condition);
                    if (!string.IsNullOrWhiteSpace(expression))
                    {
                        orderedParts.Add(new FormulaPart(expression, item.Condition.JoinWithNext));
                    }
                }
                else if (item.Group != null)
                {
                    var childExpression = BuildFromGroup(item.Group);
                    if (!string.IsNullOrWhiteSpace(childExpression) && childExpression != "1")
                    {
                        orderedParts.Add(new FormulaPart($"({childExpression})", item.Group.JoinWithNext));
                    }
                }
            }

            if (orderedParts.Any())
            {
                return BuildJoinedExpression(orderedParts);
            }

            var parts = BuildConditionFormulaParts(fallbackConditions);
            return BuildJoinedExpression(parts);
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

        private static List<FormulaPart> BuildConditionFormulaParts(IEnumerable<FilterConditionItem> conditions)
        {
            return conditions
                .Where(c => !string.IsNullOrWhiteSpace(c.Attribute) && !string.IsNullOrWhiteSpace(c.Operator))
                .Select(c => new FormulaPart(BuildConditionExpression(c), c.JoinWithNext))
                .Where(part => !string.IsNullOrWhiteSpace(part.Expression))
                .ToList();
        }

        private static List<string> BuildConditionParts(IEnumerable<FilterConditionItem> conditions)
        {
            return conditions
                .Where(c => !string.IsNullOrWhiteSpace(c.Attribute) && !string.IsNullOrWhiteSpace(c.Operator))
                .Select(BuildConditionExpression)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();
        }

        private static string BuildJoinedExpression(IReadOnlyList<FormulaPart> parts)
        {
            if (parts.Count == 0) return "1";
            if (parts.Count == 1) return parts[0].Expression;

            var result = parts[0].Expression;
            for (var i = 1; i < parts.Count; i++)
            {
                var join = NormalizeJoinOperator(parts[i - 1].JoinWithNext);
                result += $" {join} {parts[i].Expression}";
            }

            return result;
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
