using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;

namespace SpecStudioParser.Models
{
    /// <summary>
    /// Класс, представляющий графический объект чертежа в памяти плагина
    /// </summary>
    public partial class DwgObject : ObservableObject
    {
        // Базовые свойства объекта
        public string Handle { get; set; } = "";

        [ObservableProperty]
        private string _objectName = ""; // Сделали реактивным для поддержки редактирования в UI

        public string Layer { get; set; } = "";
        public string ModelStudioId { get; set; } = "";

        [ObservableProperty]
        private string _note = "";       // Сделали реактивным для вывода статусов/параметров

        // Технические свойства для двусторонней связи с nanoCAD
        public ObjectId RawObjectId { get; set; } // Идентификатор объекта в БД nanoCAD
        public Dictionary<string, string> AllAttributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        [ObservableProperty]
        private bool _isSelectedInGrid;  // Флаг выделения строки в таблице Avalonia
    }
}