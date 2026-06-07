using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Teigha.DatabaseServices;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.Services
{
    public sealed class SelectionDiagnosticsResult
    {
        public int DbSelectedCount { get; init; }
        public int MultiCadSelectedCount { get; init; }
        public string Summary { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
    }

    /// <summary>
    /// Диагностика выбранных объектов для доводки инструментов под реальные типы nanoCAD / Teigha / MultiCAD.
    /// Сервис ничего не изменяет в чертеже, только выводит типы и доступные геометрические точки.
    /// </summary>
    public sealed class SelectionDiagnosticsService
    {
        private static readonly string[] InterestingPointProperties =
        {
            "Origin",
            "TextLocation",
            "BlockPosition",
            "TextPosition",
            "TextPos",
            "PntText",
            "Start",
            "End",
            "Location",
            "Position",
            "InsertionPoint",
            "DimLinePoint"
        };

        private static readonly string[] InterestingContentProperties =
        {
            "MText",
            "Text",
            "Annotation",
            "Content",
            "Leader"
        };

        public SelectionDiagnosticsResult DiagnoseSelection()
        {
            var builder = new StringBuilder();
            builder.AppendLine("[DesignTools] Диагностика выбранных объектов");
            builder.AppendLine("------------------------------------------------------------");

            var dbCount = AppendDbSelectionDiagnostics(builder);
            var mcCount = AppendMultiCadSelectionDiagnostics(builder);

            var summary = $"Диагностика выполнена. Teigha/DB объектов: {dbCount}; MultiCAD объектов: {mcCount}.";
            return new SelectionDiagnosticsResult
            {
                DbSelectedCount = dbCount,
                MultiCadSelectedCount = mcCount,
                Summary = summary,
                Details = builder.ToString()
            };
        }

        private static int AppendDbSelectionDiagnostics(StringBuilder builder)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                builder.AppendLine("Teigha/DB: активный документ nanoCAD не найден.");
                return 0;
            }

            var editor = doc.Editor;
            var selection = editor.SelectImplied();
            if (selection.Status != PromptStatus.OK || selection.Value == null || selection.Value.Count == 0)
            {
                builder.AppendLine("Teigha/DB: текущее выделение отсутствует.");
                return 0;
            }

            var ids = selection.Value.GetObjectIds();
            builder.AppendLine($"Teigha/DB: выбрано объектов: {ids.Length}");

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var index = 1;
                foreach (var id in ids)
                {
                    try
                    {
                        var obj = tr.GetObject(id, OpenMode.ForRead, false);
                        AppendObjectDiagnostics(builder, index, "DB", obj);
                    }
                    catch (Exception ex)
                    {
                        builder.AppendLine($"  {index}. DB: ошибка чтения объекта {id}: {ex.Message}");
                    }

                    index++;
                }

                tr.Commit();
            }

            return ids.Length;
        }

        private static int AppendMultiCadSelectionDiagnostics(StringBuilder builder)
        {
            var objectManagerType = ResolveLoadedType("Multicad.DatabaseServices.McObjectManager");
            if (objectManagerType == null)
            {
                builder.AppendLine("MultiCAD: McObjectManager не найден среди загруженных сборок.");
                return 0;
            }

            var selectionIds = GetCurrentMultiCadSelection(objectManagerType);
            if (selectionIds.Count == 0)
            {
                builder.AppendLine("MultiCAD: текущее выделение отсутствует или не передано через McObjectManager.SelectionSet.");
                return 0;
            }

            builder.AppendLine($"MultiCAD: выбрано объектов: {selectionIds.Count}");
            var index = 1;
            foreach (var id in selectionIds)
            {
                try
                {
                    var obj = GetMultiCadObject(objectManagerType, id);
                    if (obj == null)
                    {
                        builder.AppendLine($"  {index}. MultiCAD: объект не получен для id {id}.");
                    }
                    else
                    {
                        AppendObjectDiagnostics(builder, index, "MultiCAD", obj);
                    }
                }
                catch (Exception ex)
                {
                    builder.AppendLine($"  {index}. MultiCAD: ошибка чтения объекта {id}: {ex.Message}");
                }

                index++;
            }

            return selectionIds.Count;
        }

        private static void AppendObjectDiagnostics(StringBuilder builder, int index, string source, object obj)
        {
            var type = obj.GetType();
            builder.AppendLine($"  {index}. {source}: {type.FullName}");

            if (obj is Entity entity)
            {
                builder.AppendLine($"     RXClass: {entity.GetRXClass()?.Name ?? "-"}; Layer: {SafeGet(() => entity.Layer)}; Handle: {SafeGet(() => entity.Handle.ToString())}");
            }

            var pointProperties = GetPointPropertyNames(obj).ToArray();
            builder.AppendLine(pointProperties.Length == 0
                ? "     Точки: не найдены среди известных свойств."
                : $"     Точки: {string.Join(", ", pointProperties)}");

            var contentProperties = GetReadablePropertyNames(obj, InterestingContentProperties).ToArray();
            if (contentProperties.Length > 0)
            {
                builder.AppendLine($"     Вложенное содержимое: {string.Join(", ", contentProperties)}");
            }

            var methods = GetInterestingMethodNames(obj).ToArray();
            if (methods.Length > 0)
            {
                builder.AppendLine($"     Методы: {string.Join(", ", methods)}");
            }
        }

        private static IEnumerable<string> GetPointPropertyNames(object obj)
        {
            foreach (var propertyName in InterestingPointProperties)
            {
                var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanRead)
                {
                    continue;
                }

                object? value = null;
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

        private static IEnumerable<string> GetReadablePropertyNames(object obj, IEnumerable<string> names)
        {
            foreach (var propertyName in names)
            {
                var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property != null && property.CanRead)
                {
                    yield return property.CanWrite ? propertyName : propertyName + " (read-only)";
                }
            }
        }

        private static IEnumerable<string> GetInterestingMethodNames(object obj)
        {
            var methods = obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
            foreach (var method in methods)
            {
                if (method.Name.Contains("Location", StringComparison.OrdinalIgnoreCase) ||
                    method.Name.Contains("Position", StringComparison.OrdinalIgnoreCase) ||
                    method.Name.Contains("Vertex", StringComparison.OrdinalIgnoreCase) ||
                    method.Name.Contains("Leader", StringComparison.OrdinalIgnoreCase))
                {
                    yield return method.Name;
                }
            }
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

        private static Type? ResolveLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false, true);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch (FileLoadException) { }
                catch (BadImageFormatException) { }
                catch (ReflectionTypeLoadException) { }
            }

            return null;
        }

        private static List<object> GetCurrentMultiCadSelection(Type objectManagerType)
        {
            var result = new List<object>();
            var selectionSet = objectManagerType.GetProperty("SelectionSet", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var selectionSetType = selectionSet?.GetType() ?? objectManagerType.GetNestedType("SelectionSet", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            object? currentSelection = null;
            if (selectionSet != null)
            {
                currentSelection = selectionSet.GetType().GetProperty("CurrentSelection", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)?.GetValue(selectionSet);
            }

            if (currentSelection == null && selectionSetType != null)
            {
                currentSelection = selectionSetType.GetProperty("CurrentSelection", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            }

            if (currentSelection is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                    {
                        result.Add(item);
                    }
                }
            }

            return result;
        }

        private static object? GetMultiCadObject(Type objectManagerType, object id)
        {
            var getObjectMethod = objectManagerType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetObject" && m.GetParameters().Length == 1);

            if (getObjectMethod != null)
            {
                return getObjectMethod.Invoke(null, new[] { id });
            }

            return id.GetType().GetMethod("GetObject", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)?.Invoke(id, Array.Empty<object>());
        }
    }
}
