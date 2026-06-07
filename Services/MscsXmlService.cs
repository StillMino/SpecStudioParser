using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpecStudioParser.Models;

namespace SpecStudioParser.Services
{
    public static class MscsXmlService
    {
        private sealed record FilterExpressionPart(string Expression, string JoinWithNext);

        public static ReportProfile LoadFromMscsXml(string filePath)
        {
            var profile = new ReportProfile
            {
                Name = Path.GetFileNameWithoutExtension(filePath)
            };

            if (!File.Exists(filePath)) return profile;

            try
            {
                XDocument doc = XDocument.Load(filePath);
                XElement? datasetProfile = doc.Root?.Element("DatasetProfile");
                if (datasetProfile == null) return profile;

                // Читаем каждый тег <Dataset> из XML
                foreach (XElement datasetEl in datasetProfile.Elements("Dataset"))
                {
                    XElement? tableEl = datasetEl.Element("Table");
                    if (tableEl == null) continue;

                    var filterFormula = tableEl.Attribute("filter")?.Value ?? "1";
                    var datasetConfig = new DatasetConfig
                    {
                        Caption = tableEl.Attribute("caption")?.Value ?? "Набор данных",
                        FilterFormula = filterFormula,
                        Aggregated = int.TryParse(tableEl.Attribute("aggregated")?.Value, out int agg) ? agg : 1
                    };

                    foreach (var rootItem in ParseRootFilterItems(filterFormula))
                    {
                        datasetConfig.RootFilterItems.Add(rootItem);
                    }
                    datasetConfig.RunFilterIntegrityDiagnostics();

                    // Чтение разрешенных типов объектов (<Types><Type name="..."/></Types>)
                    XElement? typesEl = tableEl.Element("Types");
                    if (typesEl != null)
                    {
                        foreach (XElement typeEl in typesEl.Elements("Type"))
                        {
                            string? name = typeEl.Attribute("name")?.Value;
                            if (!string.IsNullOrEmpty(name))
                            {
                                datasetConfig.TargetTypes.Add(name);
                            }
                        }
                    }

                    // Чтение полей/колонок таблицы (<Fields><Field .../></Fields>)
                    XElement? fieldsEl = tableEl.Element("Fields");
                    if (fieldsEl != null)
                    {
                        int index = 0;
                        foreach (XElement fieldEl in fieldsEl.Elements("Field"))
                        {
                            var col = new ReportColumnConfig
                            {
                                Caption = fieldEl.Attribute("caption")?.Value ?? $"Столбец {index + 1}",
                                DataFormula = fieldEl.Attribute("data")?.Value ?? "",
                                Visible = int.TryParse(fieldEl.Attribute("visible")?.Value, out int vis) ? vis : 1,
                                Aggregate = int.TryParse(fieldEl.Attribute("aggregate")?.Value, out int ag) ? ag : 0,
                                DisplayIndex = index++
                            };
                            datasetConfig.Columns.Add(col);
                        }
                    }

                    profile.Datasets.Add(datasetConfig);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка разбора XML: {ex.Message}");
            }

            return profile;
        }

        private static IReadOnlyList<FilterRootItem> ParseRootFilterItems(string? filterFormula)
        {
            if (string.IsNullOrWhiteSpace(filterFormula) || filterFormula.Trim() == "1")
            {
                return Array.Empty<FilterRootItem>();
            }

            return SplitTopLevel(filterFormula)
                .Select(ParseRootFilterItem)
                .Where(item => item != null)
                .Cast<FilterRootItem>()
                .ToList();
        }

        private static FilterRootItem? ParseRootFilterItem(FilterExpressionPart part)
        {
            var expression = part.Expression.Trim();
            if (TryUnwrapParentheses(expression, out var innerExpression))
            {
                var group = ParseFilterGroup(innerExpression);
                group.JoinWithNext = part.JoinWithNext;
                return FilterRootItem.FromGroup(group);
            }

            var condition = ParseFilterConditionAtom(expression);
            if (condition == null) return null;

            condition.JoinWithNext = part.JoinWithNext;
            return FilterRootItem.FromCondition(condition);
        }

        private static FilterConditionGroup ParseFilterGroup(string expression)
        {
            var group = new FilterConditionGroup();
            foreach (var part in SplitTopLevel(expression))
            {
                var itemExpression = part.Expression.Trim();
                if (TryUnwrapParentheses(itemExpression, out var innerExpression))
                {
                    var childGroup = ParseFilterGroup(innerExpression);
                    childGroup.JoinWithNext = part.JoinWithNext;
                    group.Items.Add(FilterGroupItem.FromGroup(childGroup));
                    continue;
                }

                var condition = ParseFilterConditionAtom(itemExpression);
                if (condition != null)
                {
                    condition.JoinWithNext = part.JoinWithNext;
                    group.Items.Add(FilterGroupItem.FromCondition(condition));
                }
            }

            return group;
        }

        private static IReadOnlyList<FilterExpressionPart> SplitTopLevel(string expression)
        {
            var parts = new List<FilterExpressionPart>();
            var text = expression.Trim();
            var depth = 0;
            var start = 0;
            char? quote = null;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (quote != null)
                {
                    if (ch == quote && !IsEscaped(text, i))
                    {
                        quote = null;
                    }
                    continue;
                }

                if (ch == '\'' || ch == '"')
                {
                    quote = ch;
                    continue;
                }

                if (ch == '(')
                {
                    depth++;
                    continue;
                }

                if (ch == ')')
                {
                    depth = Math.Max(0, depth - 1);
                    continue;
                }

                if (depth == 0 && TryReadJoinOperator(text, i, out var joinOperator, out var joinLength))
                {
                    var part = text[start..i].Trim();
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        parts.Add(new FilterExpressionPart(part, joinOperator));
                    }

                    i += joinLength - 1;
                    start = i + 1;
                }
            }

            var tail = text[start..].Trim();
            if (!string.IsNullOrWhiteSpace(tail))
            {
                parts.Add(new FilterExpressionPart(tail, "and"));
            }

            return parts;
        }

        private static bool TryReadJoinOperator(string text, int index, out string joinOperator, out int length)
        {
            if (IsWordAt(text, index, "and"))
            {
                joinOperator = "and";
                length = 3;
                return true;
            }

            if (IsWordAt(text, index, "or"))
            {
                joinOperator = "or";
                length = 2;
                return true;
            }

            joinOperator = "and";
            length = 0;
            return false;
        }

        private static bool IsWordAt(string text, int index, string word)
        {
            if (index < 0 || index + word.Length > text.Length) return false;
            if (!string.Equals(text.Substring(index, word.Length), word, StringComparison.OrdinalIgnoreCase)) return false;

            var beforeOk = index == 0 || !IsIdentifierChar(text[index - 1]);
            var afterIndex = index + word.Length;
            var afterOk = afterIndex >= text.Length || !IsIdentifierChar(text[afterIndex]);
            return beforeOk && afterOk;
        }

        private static bool IsIdentifierChar(char ch)
        {
            return char.IsLetterOrDigit(ch) || ch == '_' || ch == '.';
        }

        private static bool IsEscaped(string text, int index)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            {
                slashCount++;
            }

            return slashCount % 2 == 1;
        }

        private static bool TryUnwrapParentheses(string expression, out string innerExpression)
        {
            var text = expression.Trim();
            innerExpression = text;
            if (!text.StartsWith("(", StringComparison.Ordinal) || !text.EndsWith(")", StringComparison.Ordinal))
            {
                return false;
            }

            var depth = 0;
            char? quote = null;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (quote != null)
                {
                    if (ch == quote && !IsEscaped(text, i))
                    {
                        quote = null;
                    }
                    continue;
                }

                if (ch == '\'' || ch == '"')
                {
                    quote = ch;
                    continue;
                }

                if (ch == '(')
                {
                    depth++;
                    continue;
                }

                if (ch == ')')
                {
                    depth--;
                    if (depth == 0 && i < text.Length - 1)
                    {
                        return false;
                    }
                }
            }

            if (depth != 0) return false;

            innerExpression = text[1..^1].Trim();
            return true;
        }

        private static FilterConditionItem? ParseFilterConditionAtom(string expression)
        {
            var text = expression.Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;

            var issetMatch = Regex.Match(text, @"^(?<not>not\s+)?isset\s*\(\s*(?<attr>[^\)]+)\s*\)$", RegexOptions.IgnoreCase);
            if (issetMatch.Success)
            {
                return new FilterConditionItem
                {
                    Attribute = NormalizeAttributeToken(issetMatch.Groups["attr"].Value),
                    Operator = issetMatch.Groups["not"].Success ? "not isset" : "isset",
                    Value = string.Empty
                };
            }

            var likeMatch = Regex.Match(text, @"^(?<attr>.+?)\s+(?<op>not\s+like|like)\s+(?<value>.+)$", RegexOptions.IgnoreCase);
            if (likeMatch.Success)
            {
                return new FilterConditionItem
                {
                    Attribute = NormalizeAttributeToken(likeMatch.Groups["attr"].Value),
                    Operator = likeMatch.Groups["op"].Value.ToLowerInvariant(),
                    Value = Unquote(likeMatch.Groups["value"].Value)
                };
            }

            var compareMatch = Regex.Match(text, @"^(?<attr>.+?)\s*(?<op>>=|<=|<>|!=|==|=|>|<)\s*(?<value>.+)$", RegexOptions.IgnoreCase);
            if (compareMatch.Success)
            {
                return new FilterConditionItem
                {
                    Attribute = NormalizeAttributeToken(compareMatch.Groups["attr"].Value),
                    Operator = NormalizeOperatorForEditor(compareMatch.Groups["op"].Value),
                    Value = Unquote(compareMatch.Groups["value"].Value)
                };
            }

            return null;
        }

        private static string NormalizeOperatorForEditor(string value)
        {
            return value.Trim() switch
            {
                "<>" => "!=",
                "==" => "=",
                var op => op
            };
        }

        private static string NormalizeAttributeToken(string value)
        {
            var text = value.Trim();
            foreach (var prefix in new[] { "root.", "current.", "object." })
            {
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text[prefix.Length..];
                    break;
                }
            }

            if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
            {
                text = text[1..^1];
            }

            return text.Trim();
        }

        private static string Unquote(string value)
        {
            var text = value.Trim();
            if ((text.StartsWith("\"", StringComparison.Ordinal) && text.EndsWith("\"", StringComparison.Ordinal)) ||
                (text.StartsWith("'", StringComparison.Ordinal) && text.EndsWith("'", StringComparison.Ordinal)))
            {
                text = text[1..^1];
            }

            return text.Replace("\\\"", "\"");
        }
    }
}
