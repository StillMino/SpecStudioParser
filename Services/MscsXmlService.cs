using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpecStudioParser.Models;

namespace SpecStudioParser.Services
{
    public static class MscsXmlService
    {
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

                    foreach (var condition in ParseFilterConditions(filterFormula))
                    {
                        datasetConfig.FilterConditions.Add(condition);
                    }

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

        private static FilterConditionItem[] ParseFilterConditions(string? filterFormula)
        {
            if (string.IsNullOrWhiteSpace(filterFormula) || filterFormula.Trim() == "1")
            {
                return Array.Empty<FilterConditionItem>();
            }

            return Regex.Split(filterFormula, @"\s+and\s+", RegexOptions.IgnoreCase)
                .Select(ParseFilterConditionAtom)
                .Where(condition => condition != null)
                .Cast<FilterConditionItem>()
                .ToArray();
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
                    Operator = likeMatch.Groups["op"].Value,
                    Value = Unquote(likeMatch.Groups["value"].Value)
                };
            }

            var compareMatch = Regex.Match(text, @"^(?<attr>.+?)\s*(?<op>>=|<=|<>|!=|==|=|>|<)\s*(?<value>.+)$", RegexOptions.IgnoreCase);
            if (compareMatch.Success)
            {
                return new FilterConditionItem
                {
                    Attribute = NormalizeAttributeToken(compareMatch.Groups["attr"].Value),
                    Operator = compareMatch.Groups["op"].Value,
                    Value = Unquote(compareMatch.Groups["value"].Value)
                };
            }

            return null;
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
                return text[1..^1];
            }

            return text;
        }
    }
}