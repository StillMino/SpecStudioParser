using System.Collections.Generic;

namespace SpecStudioParser.AttributeExportPro.Models
{
    /// <summary>
    /// Одна строка данных выгрузки — словарь «заголовок колонки → значение».
    /// </summary>
    public class ExportRow
    {
        public string BlockName { get; set; } = "";
        public string Layer { get; set; } = "";
        public string Handle { get; set; } = "";
        public Dictionary<string, string> Values { get; set; } = new();
    }

    /// <summary>
    /// Результат сбора данных с чертежа.
    /// </summary>
    public class ExportData
    {
        public List<string> BlockNames { get; set; } = new();
        public List<string> AllAttributeTags { get; set; } = new();
        public List<ExportRow> Rows { get; set; } = new();
    }
}
