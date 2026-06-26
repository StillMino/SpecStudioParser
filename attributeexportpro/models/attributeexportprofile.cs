using System.Collections.Generic;

namespace SpecStudioParser.AttributeExportPro.Models
{
    /// <summary>
    /// Профиль шаблона выгрузки атрибутов — сохраняется/загружается как XML.
    /// </summary>
    public class AttributeExportProfile
    {
        public string Name { get; set; } = "Новый профиль";
        public string? TargetBlockName { get; set; }
        public List<ExportColumnMapping> Columns { get; set; } = new();
        public ExportFormat Format { get; set; } = ExportFormat.Xlsx;
        public bool GroupByBlockName { get; set; } = true;
        public bool IncludeHeader { get; set; } = true;
        public bool SortByName { get; set; } = true;
        public string? OutputPath { get; set; }
    }

    public class ExportColumnMapping
    {
        /// <summary>Имя атрибута в блоке (TAG), например "MARK", "DESCRIPTION" или служебное: $BLOCK_NAME, $LAYER, $HANDLE, $X, $Y, $Z</summary>
        public string SourceAttribute { get; set; } = "";
        /// <summary>Заголовок колонки в выгрузке</summary>
        public string DisplayHeader { get; set; } = "";
    }

    public enum ExportFormat
    {
        Xlsx,
        Csv
    }
}
