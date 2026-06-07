using HostMgd.EditorInput;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Teigha.DatabaseServices;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.Services
{
    public sealed class DesignToolsStepDistributionService
    {
        private static readonly string[] LeaderTypeMarkers =
        {
            "McNote", "McNotePosition", "McNoteChain", "McNoteComb", "McNoteLinearMark",
            "McMark", "McAnnotation", "McSpecLeader", "Leader", "Выноска", "Мультивыноска"
        };

        private static readonly string[] LeaderAnchorPointProperties =
        {
            "TextLocation", "BlockPosition", "Origin", "TextPosition", "TextPos", "PntText",
            "Start", "End", "Location", "Position"
        };

        public DimensionAlignmentResult DistributeSelectedDimensionTextByStep(LeaderAlignmentAxis axis)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return new DimensionAlignmentResult { Message = "Нет активного документа nanoCAD." };
            }

            var editor = doc.Editor;
            var selection = GetDbSelection(editor, "\nВыберите размеры для распределения с шагом: ");
            if (selection == null || selection.Length == 0)
            {
                return new DimensionAlignmentResult { Message = "Не выбраны размеры для обработки." };
            }

            if (!TryGetStep(editor, axis, out var step))
            {
                return new DimensionAlignmentResult { SelectedCount = selection.Length, Message = "Указание шага отменено." };
            }

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var targets = new List<StepTarget>();
                foreach (var id in selection)
                {
                    var obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (!IsDimensionCandidate(obj))
                    {
                        continue;
                    }

                    if (TryCreatePropertyPointTarget(obj, "TextPosition", out var target, beforeApply: targetObject => TrySetDefaultTextPosition(targetObject, false), afterApply: targetObject =>
                        {
                            TryRecomputeDimensionBlock(targetObject);
                            MarkObjectModified(targetObject);
                        }))
                    {
                        targets.Add(target);
                    }
                }

                if (targets.Count < 2)
                {
                    tr.Abort();
                    return new DimensionAlignmentResult
                    {
                        SelectedCount = selection.Length,
                        CandidateCount = targets.Count,
                        Message = $"Для распределения с шагом нужно минимум 2 размера с доступной позицией текста. Найдено: {targets.Count}."
                    };
                }

                ApplyStepDistribution(targets, axis, step);
                tr.Commit();
                editor.UpdateScreen();

                return new DimensionAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = targets.Count,
                    ProcessedCount = targets.Count,
                    Message = $"Размеры: текст распределен с шагом {FormatStep(step)}. Обработано объектов: {targets.Count}."
                };
            }
        }

        public LeaderAlignmentResult DistributeSelectedLeadersByStep(DesignToolsLeaderSource source, LeaderAlignmentAxis axis)
        {
            return source == DesignToolsLeaderSource.MultiCad
                ? DistributeSelectedMultiCadLeadersByStep(axis)
                : DistributeSelectedTeighaMLeadersByStep(axis);
        }

        private static LeaderAlignmentResult DistributeSelectedTeighaMLeadersByStep(LeaderAlignmentAxis axis)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return new LeaderAlignmentResult { Message = "Нет активного документа nanoCAD." };
            }

            var editor = doc.Editor;
            var selection = GetDbSelection(editor, "\nВыберите мультивыноски для распределения с шагом: ");
            if (selection == null || selection.Length == 0)
            {
                return new LeaderAlignmentResult { Message = "Не выбраны мультивыноски для обработки." };
            }

            if (!TryGetStep(editor, axis, out var step))
            {
                return new LeaderAlignmentResult { SelectedCount = selection.Length, Message = "Указание шага отменено." };
            }

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var targets = new List<StepTarget>();
                foreach (var id in selection)
                {
                    var obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (obj is not MLeader mLeader)
                    {
                        continue;
                    }

                    if (TryCreatePropertyPointTarget(mLeader, "TextLocation", out var target, afterApply: MarkObjectModified) ||
                        TryCreatePropertyPointTarget(mLeader, "BlockPosition", out target, afterApply: MarkObjectModified))
                    {
                        targets.Add(target);
                    }
                }

                if (targets.Count < 2)
                {
                    tr.Abort();
                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selection.Length,
                        CandidateCount = targets.Count,
                        Message = $"Для распределения с шагом нужно минимум 2 мультивыноски с доступной позицией текста. Найдено: {targets.Count}."
                    };
                }

                ApplyStepDistribution(targets, axis, step);
                tr.Commit();
                editor.UpdateScreen();

                return new LeaderAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = targets.Count,
                    AlignedCount = targets.Count,
                    Message = $"Мультивыноски: текст распределен с шагом {FormatStep(step)}. Обработано объектов: {targets.Count}."
                };
            }
        }

        private static LeaderAlignmentResult DistributeSelectedMultiCadLeadersByStep(LeaderAlignmentAxis axis)
        {
            Type? objectManagerType;
            try
            {
                objectManagerType = ResolveLoadedType("Multicad.DatabaseServices.McObjectManager");
            }
            catch (FileLoadException) { return new LeaderAlignmentResult { Message = "MultiCAD API недоступен в текущем сеансе nanoCAD." }; }
            catch (BadImageFormatException) { return new LeaderAlignmentResult { Message = "MultiCAD API недоступен в текущем сеансе nanoCAD." }; }

            if (objectManagerType == null)
            {
                return new LeaderAlignmentResult { Message = "MultiCAD API недоступен в текущем сеансе nanoCAD." };
            }

            var selectionIds = GetCurrentMultiCadSelection(objectManagerType);
            if (selectionIds.Count == 0)
            {
                return new LeaderAlignmentResult { Message = "Для MultiCAD-выносок выберите объекты до запуска команды." };
            }

            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var editor = doc?.Editor;
            if (editor == null)
            {
                return new LeaderAlignmentResult { Message = "Нет активного документа nanoCAD." };
            }

            if (!TryGetStep(editor, axis, out var step))
            {
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = "Указание шага отменено." };
            }

            try
            {
                StartMultiCadTransaction(objectManagerType);
                try
                {
                    var targets = new List<StepTarget>();
                    foreach (var id in selectionIds)
                    {
                        var obj = GetMultiCadObject(objectManagerType, id);
                        if (obj == null || !IsLeaderCandidate(obj))
                        {
                            continue;
                        }

                        if (TryCreateLeaderPointTarget(obj, out var target))
                        {
                            targets.Add(target);
                        }
                    }

                    if (targets.Count < 2)
                    {
                        AbortMultiCadTransaction(objectManagerType);
                        return new LeaderAlignmentResult
                        {
                            SelectedCount = selectionIds.Count,
                            CandidateCount = targets.Count,
                            Message = $"Для распределения с шагом нужно минимум 2 MultiCAD-выноски с доступной точкой текста. Найдено: {targets.Count}."
                        };
                    }

                    ApplyStepDistribution(targets, axis, step);
                    EndMultiCadTransaction(objectManagerType);
                    UpdateMultiCadGraphics(objectManagerType);

                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selectionIds.Count,
                        CandidateCount = targets.Count,
                        AlignedCount = targets.Count,
                        Message = $"MultiCAD-выноски: текст распределен с шагом {FormatStep(step)}. Обработано объектов: {targets.Count}."
                    };
                }
                catch
                {
                    AbortMultiCadTransaction(objectManagerType);
                    throw;
                }
            }
            catch (Exception ex)
            {
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = $"Ошибка распределения MultiCAD-выносок с шагом: {ex.Message}" };
            }
        }

        private static void ApplyStepDistribution(IReadOnlyList<StepTarget> targets, LeaderAlignmentAxis axis, double step)
        {
            var ordered = axis == LeaderAlignmentAxis.Horizontal
                ? targets.OrderBy(t => t.Point.X).ToArray()
                : targets.OrderBy(t => t.Point.Y).ToArray();

            var first = ordered[0].Point;
            for (var index = 0; index < ordered.Length; index++)
            {
                var current = ordered[index].Point;
                var next = axis == LeaderAlignmentAxis.Horizontal
                    ? new AlignmentPoint(first.X + step * index, current.Y, current.Z)
                    : new AlignmentPoint(current.X, first.Y + step * index, current.Z);
                ordered[index].Apply(next);
            }
        }

        private static bool TryGetStep(Editor editor, LeaderAlignmentAxis axis, out double step)
        {
            step = 0.0;
            NanoCadEditorFocusService.PrepareForEditorInput();
            var first = editor.GetPoint(new PromptPointOptions("\nУкажите первую точку шага: "));
            if (first.Status != PromptStatus.OK)
            {
                return false;
            }

            NanoCadEditorFocusService.PrepareForEditorInput();
            var second = editor.GetPoint(new PromptPointOptions("\nУкажите вторую точку шага: "));
            if (second.Status != PromptStatus.OK)
            {
                return false;
            }

            step = axis == LeaderAlignmentAxis.Horizontal
                ? second.Value.X - first.Value.X
                : second.Value.Y - first.Value.Y;

            return Math.Abs(step) > 1e-9;
        }

        private static ObjectId[]? GetDbSelection(Editor editor, string prompt)
        {
            NanoCadEditorFocusService.PrepareForEditorInput();
            var implied = editor.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                return implied.Value.GetObjectIds();
            }

            NanoCadEditorFocusService.PrepareForEditorInput();
            var picked = editor.GetSelection(new PromptSelectionOptions { MessageForAdding = prompt });
            return picked.Status == PromptStatus.OK && picked.Value != null && picked.Value.Count > 0 ? picked.Value.GetObjectIds() : null;
        }

        private static bool TryCreateLeaderPointTarget(object obj, out StepTarget target)
        {
            foreach (var propertyName in LeaderAnchorPointProperties)
            {
                if (TryCreatePropertyPointTarget(obj, propertyName, out target, afterApply: MarkObjectModified))
                {
                    return true;
                }
            }

            target = default!;
            return false;
        }

        private static bool TryCreatePropertyPointTarget(object obj, string propertyName, out StepTarget target, Action<object>? beforeApply = null, Action<object>? afterApply = null)
        {
            target = default!;
            var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead || !property.CanWrite || !TryGetPoint(property.GetValue(obj), out var point))
            {
                return false;
            }

            target = new StepTarget(point, nextPoint =>
            {
                beforeApply?.Invoke(obj);
                property.SetValue(obj, CreatePointValue(property.PropertyType, nextPoint));
                afterApply?.Invoke(obj);
            });
            return true;
        }

        private static bool IsDimensionCandidate(object obj)
        {
            if (obj is Dimension)
            {
                return true;
            }

            var type = obj.GetType();
            var rxName = obj is Entity entity ? entity.GetRXClass()?.Name ?? string.Empty : string.Empty;
            return type.Name.Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                   (type.FullName ?? string.Empty).Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                   rxName.Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                   rxName.StartsWith("AcDb", StringComparison.OrdinalIgnoreCase) && rxName.Contains("Dim", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLeaderCandidate(object obj)
        {
            var type = obj.GetType();
            var typeName = type.Name;
            var fullName = type.FullName ?? string.Empty;
            var rxName = obj is Entity entity ? entity.GetRXClass()?.Name ?? string.Empty : string.Empty;
            return LeaderTypeMarkers.Any(marker => typeName.Contains(marker, StringComparison.OrdinalIgnoreCase) || fullName.Contains(marker, StringComparison.OrdinalIgnoreCase) || rxName.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private static void TrySetDefaultTextPosition(object obj, bool useDefault)
        {
            try
            {
                var property = obj.GetType().GetProperty("UsingDefaultTextPosition", BindingFlags.Instance | BindingFlags.Public);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                {
                    property.SetValue(obj, useDefault);
                }
            }
            catch { }
        }

        private static void TryRecomputeDimensionBlock(object obj)
        {
            try { obj.GetType().GetMethod("RecomputeDimensionBlock", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)?.Invoke(obj, Array.Empty<object>()); } catch { }
        }

        private static void MarkObjectModified(object obj)
        {
            try { obj.GetType().GetMethod("RecordGraphicsModified", BindingFlags.Instance | BindingFlags.Public, new[] { typeof(bool) })?.Invoke(obj, new object[] { true }); } catch { }
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
                    if (item != null) result.Add(item);
                }
            }
            return result;
        }

        private static object? GetMultiCadObject(Type objectManagerType, object id)
        {
            var getObjectMethod = objectManagerType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetObject" && m.GetParameters().Length == 1);
            if (getObjectMethod != null) return getObjectMethod.Invoke(null, new[] { id });
            return id.GetType().GetMethod("GetObject", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)?.Invoke(id, Array.Empty<object>());
        }

        private static void StartMultiCadTransaction(Type objectManagerType) => InvokeTransactionMethod(objectManagerType, "Start");
        private static void EndMultiCadTransaction(Type objectManagerType) => InvokeTransactionMethod(objectManagerType, "End");
        private static void AbortMultiCadTransaction(Type objectManagerType) => InvokeTransactionMethod(objectManagerType, "Abort");

        private static void UpdateMultiCadGraphics(Type objectManagerType)
        {
            InvokeTransactionMethod(objectManagerType, "UpdateGraphics");
            objectManagerType.GetMethod("UpdateAll", BindingFlags.Static | BindingFlags.Public, Type.EmptyTypes)?.Invoke(null, Array.Empty<object>());
        }

        private static void InvokeTransactionMethod(Type objectManagerType, string methodName)
        {
            var transactions = objectManagerType.GetProperty("Transactions", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var transactionsType = transactions?.GetType() ?? objectManagerType.GetNestedType("Transactions", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (transactions != null)
            {
                transactions.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public, Type.EmptyTypes)?.Invoke(transactions, Array.Empty<object>());
                return;
            }
            transactionsType?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public, Type.EmptyTypes)?.Invoke(null, Array.Empty<object>());
        }

        private static Type? ResolveLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false, true);
                    if (type != null) return type;
                }
                catch (FileLoadException) { }
                catch (BadImageFormatException) { }
                catch (ReflectionTypeLoadException) { }
            }
            return null;
        }

        private static bool TryGetPoint(object? value, out AlignmentPoint point)
        {
            point = default;
            if (value == null) return false;
            var type = value.GetType();
            var x = type.GetProperty("X");
            var y = type.GetProperty("Y");
            var z = type.GetProperty("Z");
            if (x == null || y == null) return false;
            try
            {
                point = new AlignmentPoint(Convert.ToDouble(x.GetValue(value)), Convert.ToDouble(y.GetValue(value)), z == null ? 0.0 : Convert.ToDouble(z.GetValue(value)));
                return true;
            }
            catch { return false; }
        }

        private static object CreatePointValue(Type pointType, AlignmentPoint point)
        {
            var ctor3 = pointType.GetConstructor(new[] { typeof(double), typeof(double), typeof(double) });
            if (ctor3 != null) return ctor3.Invoke(new object[] { point.X, point.Y, point.Z });
            var ctor2 = pointType.GetConstructor(new[] { typeof(double), typeof(double) });
            if (ctor2 != null) return ctor2.Invoke(new object[] { point.X, point.Y });
            var value = Activator.CreateInstance(pointType) ?? throw new InvalidOperationException($"Не удалось создать точку типа {pointType.FullName}.");
            pointType.GetProperty("X")?.SetValue(value, point.X);
            pointType.GetProperty("Y")?.SetValue(value, point.Y);
            pointType.GetProperty("Z")?.SetValue(value, point.Z);
            return value;
        }

        private static string FormatStep(double step) => step.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private readonly struct AlignmentPoint
        {
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public AlignmentPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        }

        private readonly struct StepTarget
        {
            private readonly Action<AlignmentPoint> _apply;
            public AlignmentPoint Point { get; }
            public StepTarget(AlignmentPoint point, Action<AlignmentPoint> apply) { Point = point; _apply = apply; }
            public void Apply(AlignmentPoint point) => _apply(point);
        }
    }
}
