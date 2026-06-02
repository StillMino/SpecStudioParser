using System;
using System.IO;
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

                    var datasetConfig = new DatasetConfig
                    {
                        Caption = tableEl.Attribute("caption")?.Value ?? "Набор данных",
                        FilterFormula = tableEl.Attribute("filter")?.Value ?? "1",
                        Aggregated = int.TryParse(tableEl.Attribute("aggregated")?.Value, out int agg) ? agg : 1
                    };

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
    }
}