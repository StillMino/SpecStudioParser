using HostMgd.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Teigha.DatabaseServices;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.Services
{
    public sealed class DimensionDiagnosticsResult
    {
        public int SelectedCount { get; init; }
        public int DimensionCount { get; init; }
        public string Summary { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
    }

    /// <summary>
    /// Подробная диагностика выбранных Teigha/nanoCAD размеров.
    /// Нужна для уточнения, какие типы размеров безопасно двигать через TextPosition.
    /// </summary>
    public sealed class DimensionDiagnosticsService
    {
        private static readonly string[] DimensionPointProperties =
        {
            "TextPosition",
            "DimLinePoint",
            "XLine1Point",
            "XLine2Point",
            "Center",
            "ChordPoint",
            "ArcPoint",
            "JogPoint",
            "Leader1Point",
            "Leader2Point",
            "ExtLine1Point",
            "ExtLine2Point"
        };

        private static readonly string[] DimensionBooleanProperties =
        {
            "UsingDefaultTextPosition",
            "Dimtmove",
            "Annotative"
        };

        public DimensionDiagnosticsResult DiagnoseSelectedDimensions()
        {
            var builder = new StringBuilder();
            builder.AppendLine("[DesignTools] Диагностика размеров");
            builder.AppendLine("------------------------------------------------------------");

            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                builder.AppendLine("Активный документ nanoCAD не найден.");
                return new DimensionDiagnosticsResult { Summary = "Нет активного документа nanoCAD.", Details = builder.ToString() };
            }

            var editor = doc.Editor;
            var selection = editor.SelectImplied();
            if (selection.Status != PromptStatus.OK || selection.Value == null || selection.Value.Count == 0)
            {
                builder.AppendLine("Текущее выделение отсутствует.");
                return new DimensionDiagnosticsResult { Summary = "Нет текущего выделения.", Details = builder.ToString() };
            }

            var ids = selection.Value.GetObjectIds();
            builder.AppendLine($"Выбрано DB-объектов: {ids.Length}");

            var dimensionCount = 0;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var index = 1;
                foreach (var id in ids)
                {
                    try
                    {
                        var obj = tr.GetObject(id, OpenMode.ForRead, false);
                        if (!IsDimensionCandidate(obj))
                        {
                            AppendNonDimension(builder, index, obj);
                        }
                        else
                        {
                            dimensionCount++;
                            AppendDimension(builder, index, obj);
                        }
                    }
                    catch (Exception ex)
                    {
                        builder.AppendLine($"  {index}. Ошибка чтения объекта {id}: {ex.Message}");
                    }

                    index++;
                }

                tr.Commit();
            }

            var summary = $"Диагностика размеров выполнена. Размеров: {dimensionCount}; всего выбрано: {ids.Length}.";
            return new DimensionDiagnosticsResult
            {
                SelectedCount = ids.Length,
                DimensionCount = dimensionCount,
                Summary = summary,
                Details = builder.ToString()
            };
        }

        private static void AppendNonDimension(StringBuilder builder, int index, object obj)
        {
            var entity = obj as Entity;
            builder.AppendLine($"  {index}. Не размер: {obj.GetType().FullName}");
            if (entity != null)
            {
                builder.AppendLine($"     RXClass: {entity.GetRXClass()?.Name ?? "-"}; Layer: {SafeGet(() => entity.Layer)}; Handle: {SafeGet(() => entity.Handle.ToString())}");
            }
        }

        private static void AppendDimension(StringBuilder builder, int index, object obj)
        {
            var type = obj.GetType();
            var entity = obj as Entity;
            builder.AppendLine($"  {index}. Размер: {type.FullName}");
            if (entity != null)
            {
                builder.AppendLine($"     RXClass: {entity.GetRXClass()?.Name ?? "-"}; Layer: {SafeGet(() => entity.Layer)}; Handle: {SafeGet(() => entity.Handle.ToString())}");
            }

            AppendProperty(builder, obj, "TextPosition");
            AppendProperty(builder, obj, "UsingDefaultTextPosition");
            AppendProperty(builder, obj, "DimensionText");
            AppendProperty(builder, obj, "TextRotation");
            AppendProperty(builder, obj, "HorizontalRotation");

            var writablePoints = GetPointProperties(obj).ToArray();
            builder.AppendLine(writablePoints.Length == 0
                ? "     Point-свойства: не найдены среди известных свойств."
                : $"     Point-свойства: {string.Join(", ", writablePoints)}");

            var writableFlags = GetReadableProperties(obj, DimensionBooleanProperties).ToArray();
            if (writableFlags.Length > 0)
            {
                builder.AppendLine($"     Флаги: {string.Join(", ", writableFlags)}");
            }

            var dimensionMethods = GetInterestingMethods(obj).ToArray();
            if (dimensionMethods.Length > 0)
            {
                builder.AppendLine($"     Методы: {string.Join(", ", dimensionMethods)}");
            }
        }

        private static void AppendProperty(StringBuilder builder, object obj, string propertyName)
        {
            var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead)
            {
                builder.AppendLine($"     {propertyName}: недоступно");
                return;
            }

            try
            {
                var value = property.GetValue(obj);
                var valueText = LooksLikePoint(value) ? FormatPoint(value) : value?.ToString() ?? "null";
                var access = property.CanWrite ? "read/write" : "read-only";
                builder.AppendLine($"     {propertyName}: {valueText} ({access})");
            }
            catch (Exception ex)
            {
                builder.AppendLine($"     {propertyName}: ошибка чтения: {ex.Message}");
            }
        }

        private static IEnumerable<string> GetPointProperties(object obj)
        {
            foreach (var propertyName in DimensionPointProperties)
            {
                var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanRead)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (LooksLikePoint(value))
                {
                    yield return property.CanWrite ? propertyName : propertyName + " (read-only)";
                }
            }
        }

        private static IEnumerable<string> GetReadableProperties(object obj, IEnumerable<string> propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property != null && property.CanRead)
                {
                    yield return property.CanWrite ? propertyName : propertyName + " (read-only)";
                }
            }
        }

        private static IEnumerable<string> GetInterestingMethods(object obj)
        {
            return obj.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Point", StringComparison.OrdinalIgnoreCase))
                .Select(method => method.Name)
                .Distinct();
        }

        private static bool IsDimensionCandidate(object obj)
        {
            if (obj is Dimension)
            {
                return true;
            }

            var type = obj.GetType();
            var typeName = type.Name;
            var fullName = type.FullName ?? string.Empty;
            var rxName = obj is Entity entity ? entity.GetRXClass()?.Name ?? string.Empty : string.Empty;

            return typeName.Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                   fullName.Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                   rxName.Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                   rxName.StartsWith("AcDb", StringComparison.OrdinalIgnoreCase) && rxName.Contains("Dim", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikePoint(object? value)
        {
            if (value == null)
            {
                return false;
            }

            var type = value.GetType();
            return type.GetProperty("X") != null && type.GetProperty("Y") != null;
        }

        private static string FormatPoint(object? value)
        {
            if (value == null)
            {
                return "null";
            }

            var type = value.GetType();
            try
            {
                var x = Convert.ToDouble(type.GetProperty("X")?.GetValue(value));
                var y = Convert.ToDouble(type.GetProperty("Y")?.GetValue(value));
                var zProperty = type.GetProperty("Z");
                var z = zProperty == null ? 0.0 : Convert.ToDouble(zProperty.GetValue(value));
                return $"X={x:0.###}; Y={y:0.###}; Z={z:0.###}";
            }
            catch
            {
                return value.ToString() ?? "null";
            }
        }

        private static string SafeGet(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return "-";
            }
        }
    }
}
