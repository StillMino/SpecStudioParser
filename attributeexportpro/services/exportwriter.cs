using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SpecStudioParser.AttributeExportPro.Models;

namespace SpecStudioParser.AttributeExportPro.Services
{
    /// <summary>
    /// Экспорт данных в CSV — без внешних зависимостей.
    /// </summary>
    public sealed class ExportWriter
    {
        /// <summary>
        /// Записывает CSV с разделителем ; (русская локаль).
        /// </summary>
        public string WriteCsv(ExportData data, AttributeExportProfile profile, string filePath)
        {
            var sb = new StringBuilder();
            var columns = profile.Columns.Count > 0
                ? profile.Columns
                : AutoGenerateColumns(data);

            // Заголовок
            if (profile.IncludeHeader)
            {
                sb.AppendLine(string.Join(";", columns.Select(c => EscapeCsv(c.DisplayHeader))));
            }

            // Сортировка
            var rows = profile.SortByName
                ? data.Rows.OrderBy(r => r.BlockName).ThenBy(r => r.Handle).ToList()
                : data.Rows;

            // Группировка
            if (profile.GroupByBlockName)
            {
                foreach (var grp in rows.GroupBy(r => r.BlockName))
                {
                    foreach (var row in grp)
                    {
                        WriteRow(sb, columns, row);
                    }
                    sb.AppendLine(); // пустая строка между группами
                }
            }
            else
            {
                foreach (var row in rows)
                {
                    WriteRow(sb, columns, row);
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return filePath;
        }

        /// <summary>
        /// Минимальный Excel-совместимый XML (SpreadsheetML 2003) — не требует внешних NuGet.
        /// Открывается в Excel и LibreOffice.
        /// </summary>
        public string WriteXlsxAsXml(ExportData data, AttributeExportProfile profile, string filePath)
        {
            var columns = profile.Columns.Count > 0
                ? profile.Columns
                : AutoGenerateColumns(data);

            var rows = profile.SortByName
                ? data.Rows.OrderBy(r => r.BlockName).ThenBy(r => r.Handle).ToList()
                : data.Rows;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
            sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
            sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
            sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            sb.AppendLine(" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");

            // Стили
            sb.AppendLine("<Styles>");
            sb.AppendLine("  <Style ss:ID=\"Header\"><Font ss:Bold=\"1\"/><Interior ss:Color=\"#D9E1F2\" ss:Pattern=\"Solid\"/></Style>");
            sb.AppendLine("</Styles>");

            sb.AppendLine("<Worksheet ss:Name=\"Экспорт атрибутов\">");
            sb.AppendLine("<Table>");

            // Заголовок
            if (profile.IncludeHeader)
            {
                sb.AppendLine("<Row>");
                foreach (var col in columns)
                {
                    sb.AppendLine($"  <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">{EscapeXml(col.DisplayHeader)}</Data></Cell>");
                }
                sb.AppendLine("</Row>");
            }

            // Данные
            foreach (var row in rows)
            {
                sb.AppendLine("<Row>");
                foreach (var col in columns)
                {
                    var val = GetCellValue(row, col.SourceAttribute);
                    sb.AppendLine($"  <Cell><Data ss:Type=\"String\">{EscapeXml(val)}</Data></Cell>");
                }
                sb.AppendLine("</Row>");
            }

            sb.AppendLine("</Table>");
            sb.AppendLine("</Worksheet>");
            sb.AppendLine("</Workbook>");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            return filePath;
        }

        // ─── Вспомогательные методы ──────────────────────────────────────

        internal static List<ExportColumnMapping> AutoGenerateColumns(ExportData data)
        {
            var columns = new List<ExportColumnMapping>
            {
                new() { SourceAttribute = "$BLOCK_NAME", DisplayHeader = "Имя блока" },
                new() { SourceAttribute = "$LAYER", DisplayHeader = "Слой" },
                new() { SourceAttribute = "$HANDLE", DisplayHeader = "Handle" },
            };

            foreach (var tag in data.AllAttributeTags)
            {
                columns.Add(new ExportColumnMapping { SourceAttribute = tag, DisplayHeader = tag });
            }

            columns.Add(new() { SourceAttribute = "$X", DisplayHeader = "X" });
            columns.Add(new() { SourceAttribute = "$Y", DisplayHeader = "Y" });
            columns.Add(new() { SourceAttribute = "$Z", DisplayHeader = "Z" });
            columns.Add(new() { SourceAttribute = "$SCALE", DisplayHeader = "Масштаб" });

            return columns;
        }

        private static void WriteRow(StringBuilder sb, List<ExportColumnMapping> columns, ExportRow row)
        {
            var parts = columns.Select(c => EscapeCsv(GetCellValue(row, c.SourceAttribute)));
            sb.AppendLine(string.Join(";", parts));
        }

        internal static string GetCellValue(ExportRow row, string sourceAttr)
        {
            return row.Values.TryGetValue(sourceAttr, out var val) ? val ?? "" : "";
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return System.Security.SecurityElement.Escape(value);
        }

        // ─── XML-профиль (сохранение/загрузка) ──────────────────────────

        public void SaveProfile(AttributeExportProfile profile, string filePath)
        {
            var xml = new XElement("AttributeExportProfile",
                new XElement("Name", profile.Name),
                new XElement("TargetBlockName", profile.TargetBlockName ?? ""),
                new XElement("Format", profile.Format.ToString()),
                new XElement("GroupByBlockName", profile.GroupByBlockName),
                new XElement("IncludeHeader", profile.IncludeHeader),
                new XElement("SortByName", profile.SortByName),
                new XElement("OutputPath", profile.OutputPath ?? ""),
                new XElement("Columns",
                    profile.Columns.Select(c => new XElement("Column",
                        new XAttribute("source", c.SourceAttribute),
                        new XAttribute("header", c.DisplayHeader)
                    ))
                )
            );
            xml.Save(filePath);
        }

        public AttributeExportProfile LoadProfile(string filePath)
        {
            var xml = XElement.Load(filePath);
            var profile = new AttributeExportProfile
            {
                Name = (string?)xml.Element("Name") ?? "Загруженный профиль",
                TargetBlockName = (string?)xml.Element("TargetBlockName"),
                Format = Enum.TryParse<ExportFormat>((string?)xml.Element("Format"), out var fmt) ? fmt : ExportFormat.Xlsx,
                GroupByBlockName = (bool?)xml.Element("GroupByBlockName") ?? true,
                IncludeHeader = (bool?)xml.Element("IncludeHeader") ?? true,
                SortByName = (bool?)xml.Element("SortByName") ?? true,
                OutputPath = (string?)xml.Element("OutputPath"),
                Columns = new List<ExportColumnMapping>()
            };

            var colsElem = xml.Element("Columns");
            if (colsElem != null)
            {
                foreach (var colElem in colsElem.Elements("Column"))
                {
                    profile.Columns.Add(new ExportColumnMapping
                    {
                        SourceAttribute = (string?)colElem.Attribute("source") ?? "",
                        DisplayHeader = (string?)colElem.Attribute("header") ?? ""
                    });
                }
            }

            return profile;
        }
    }
}
