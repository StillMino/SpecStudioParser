using System;
using System.Collections.Generic;

namespace SpecStudioParser.PositionNumbering.Models
{
    /// <summary>
    /// Настройки движка нумерации.
    /// </summary>
    public class NumberingProfile
    {
        /// <summary>Префикс номеров позиций (например "Поз.")</summary>
        public string Prefix { get; set; } = "";

        /// <summary>Начальный номер</summary>
        public int StartNumber { get; set; } = 1;

        /// <summary>Шаг нумерации</summary>
        public int Step { get; set; } = 1;

        /// <summary>Стратегия сортировки</summary>
        public SortMode SortMode { get; set; } = SortMode.TopToBottom_LeftToRight;

        /// <summary>Обновлять существующие номера или только расставить новые</summary>
        public bool OverwriteExisting { get; set; } = true;

        /// <summary>Имя атрибута блока, куда писать номер позиции (null = текст на выноске)</summary>
        public string? TargetAttributeTag { get; set; } = "POS";

        /// <summary>Имя слоя для фильтрации выносок (null = все слои)</summary>
        public string? LeaderLayerFilter { get; set; }

        /// <summary>Формат числа (например "000" для трёхзначной нумерации)</summary>
        public string? NumberFormat { get; set; }
    }

    public enum SortMode
    {
        /// <summary>Сверху вниз, слева направо (ГОСТ — для сборочных чертежей)</summary>
        TopToBottom_LeftToRight,
        /// <summary>Слева направо, сверху вниз</summary>
        LeftToRight_TopToBottom,
        /// <summary>По выбору пользователя (порядок кликов)</summary>
        SelectionOrder,
        /// <summary>По слою</summary>
        ByLayer
    }

    /// <summary>
    /// Информация об одной позиции на чертеже.
    /// </summary>
    public class PositionInfo
    {
        public string Handle { get; set; } = "";
        public string CurrentNumber { get; set; } = "";
        public string NewNumber { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public string Layer { get; set; } = "";
        public string BlockName { get; set; } = "";
        public bool IsLeader { get; set; }
        public bool IsBlockAttribute { get; set; }
    }

    /// <summary>
    /// Результат операции нумерации.
    /// </summary>
    public class NumberingResult
    {
        public int TotalProcessed { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public string Message { get; set; } = "";
        public List<PositionInfo> Positions { get; set; } = new();
    }
}
