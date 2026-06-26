using System;
using System.Collections.Generic;

namespace SpecStudioParser.DynamicBlockDoctor.Models
{
    /// <summary>
    /// Тип найденной проблемы в динамическом блоке.
    /// </summary>
    public enum BlockIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum BlockIssueType
    {
        /// <summary>Блок имеет динамические параметры AutoCAD (потенциальная несовместимость)</summary>
        DynamicParameters,
        /// <summary>Параметр видимости — может работать некорректно</summary>
        VisibilityParameter,
        /// <summary>Параметр растяжения — часто ломается</summary>
        StretchParameter,
        /// <summary>Ассоциативная штриховка внутри блока — может повреждать файл</summary>
        AssociativeHatch,
        /// <summary>Сложные массивы внутри блока</summary>
        ArrayParameter,
        /// <summary>Блок содержит прокси-объекты</summary>
        ProxyObjects,
        /// <summary>Атрибуты с заблокированными свойствами</summary>
        LockedAttributes,
        /// <summary>Блок содержит слишком много примитивов (тормозит)</summary>
        HeavyBlock,
        /// <summary>Несовместимые реакторы</summary>
        Reactors,
    }

    /// <summary>
    /// Описание проблемы одного блока.
    /// </summary>
    public class BlockIssue
    {
        public BlockIssueType Type { get; set; }
        public BlockIssueSeverity Severity { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Recommendation { get; set; } = "";
    }

    /// <summary>
    /// Полный отчёт по одному блоку на чертеже.
    /// </summary>
    public class BlockDiagnosticReport
    {
        public string BlockName { get; set; } = "";
        public string Handle { get; set; } = "";
        public string Layer { get; set; } = "";
        public bool IsDynamic { get; set; }
        public bool HasVisibilityStates { get; set; }
        public bool HasAssociativeHatch { get; set; }
        public bool HasProxyObjects { get; set; }
        public int EntityCount { get; set; }
        public int AttributeCount { get; set; }
        public int DynamicPropertyCount { get; set; }
        public List<BlockIssue> Issues { get; set; } = new();
        public double ApproxSizeKB { get; set; }

        public string WorstSeverity =>
            Issues.Any(i => i.Severity == BlockIssueSeverity.Error) ? "🔴 Error" :
            Issues.Any(i => i.Severity == BlockIssueSeverity.Warning) ? "🟡 Warning" :
            Issues.Count > 0 ? "🔵 Info" : "🟢 OK";
    }

    /// <summary>
    /// Сводный отчёт по всему чертежу.
    /// </summary>
    public class DrawingDiagnosticSummary
    {
        public int TotalBlocks { get; set; }
        public int DynamicBlocks { get; set; }
        public int BlocksWithIssues { get; set; }
        public int TotalIssues { get; set; }
        public int Errors { get; set; }
        public int Warnings { get; set; }
        public List<BlockDiagnosticReport> Reports { get; set; } = new();

        public string Summary =>
            $"Блоков: {TotalBlocks}, из них динамических: {DynamicBlocks}. " +
            $"Проблем: {TotalIssues} (ошибки: {Errors}, предупреждения: {Warnings}).";
    }
}
