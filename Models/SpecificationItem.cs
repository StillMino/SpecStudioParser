namespace SpecStudioParser.Models
{
    public class SpecificationItem
    {
        public string Position { get; set; }          // № п/п
        public string Name { get; set; } = "";       // Наименование и техническая характеристика
        public string Mark { get; set; } = "";       // Тип, марка, обозначение документа
        public string Quantity { get; set; }            // Количество
        public string Note { get; set; } = "";       // Примечание (например, слои или материалы)
    }
}