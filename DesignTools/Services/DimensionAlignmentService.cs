using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Teigha.DatabaseServices;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.Services
{
    public sealed class DimensionAlignmentResult
    {
        public int SelectedCount { get; init; }
        public int CandidateCount { get; init; }
        public int ProcessedCount { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class DimensionAlignmentService
    {
        private enum DimensionOperation
        {
            Align,
            Distribute
        }

        public DimensionAlignmentResult AlignSelectedDimensions(LeaderAlignmentAxis axis)
        {
            return ProcessSelectedDimensions(axis, DimensionOperation.Align);
        }

        public DimensionAlignmentResult AlignSelectedDimensionsToPoint(LeaderAlignmentAxis axis)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return new DimensionAlignmentResult { Message = "Нет активного документа nanoCAD." };
            }

            var editor = doc.Editor;
            var selection = GetDbSelection(editor);
            if (selection == null || selection.Length == 0)
            {
                return new DimensionAlignmentResult { Message = "Не выбраны размеры для обработки." };
            }

            if (!TryGetReferencePoint(editor, axis, out var referencePoint))
            {
                return new DimensionAlignmentResult { SelectedCount = selection.Length, Message = "Указание точки отменено." };
            }

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var targets = new List<DimensionAlignmentTarget>();
                foreach (var id in selection)
                {
                    var obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (TryCreateDimensionTextTarget(obj, out var target))
                    {
                        targets.Add(target);
                    }
                }

                if (targets.Count == 0)
                {
                    tr.Abort();
                    return new DimensionAlignmentResult
                    {
                        SelectedCount = selection.Length,
                        CandidateCount = 0,
                        Message = "Среди выбранных объектов не найдено размеров с доступной позицией текста."
                    };
                }

                AlignTargetsToPoint(targets, axis, referencePoint);
                tr.Commit();
                editor.UpdateScreen();

                var axisName = axis == LeaderAlignmentAxis.Horizontal ? "по горизонтали" : "по вертикали";
                return new DimensionAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = targets.Count,
                    ProcessedCount = targets.Count,
                    Message = $"Размеры: выравнивание текста {axisName} по указанной точке выполнено. Обработано объектов: {targets.Count}."
                };
            }
        }

        public DimensionAlignmentResult DistributeSelectedDimensions(LeaderAlignmentAxis axis)
        {
            return ProcessSelectedDimensions(axis, DimensionOperation.Distribute);
        }

        public DimensionAlignmentResult ResetSelectedDimensionTextPositions()
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return new DimensionAlignmentResult { Message = "Нет активного документа nanoCAD." };
            }

            var editor = doc.Editor;
            var selection = GetDbSelection(editor);
            if (selection == null || selection.Length == 0)
            {
                return new DimensionAlignmentResult { Message = "Не выбраны размеры для обработки." };
            }

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var processed = 0;
                foreach (var id in selection)
                {
                    var obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (!IsDimensionCandidate(obj))
                    {
                        continue;
                    }

                    if (TrySetDefaultTextPosition(obj, true))
                    {
                        TryRecomputeDimensionBlock(obj);
                        MarkObjectModified(obj);
                        processed++;
                    }
                }

                if (processed == 0)
                {
                    tr.Abort();
                    return new DimensionAlignmentResult
                    {
                        SelectedCount = selection.Length,
                        CandidateCount = 0,
                        Message = "Среди выбранных объектов не найдено размеров с доступным свойством UsingDefaultTextPosition."
                    };
                }

                tr.Commit();
                editor.UpdateScreen();

                return new DimensionAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = processed,
                    ProcessedCount = processed,
                    Message = $"Размеры: текст возвращен в стандартное положение. Обработано объектов: {processed}."
                };
            }
        }

        private static DimensionAlignmentResult ProcessSelectedDimensions(LeaderAlignmentAxis axis, DimensionOperation operation)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return new DimensionAlignmentResult { Message = "Нет активного документа nanoCAD." };
            }

            var editor = doc.Editor;
            var selection = GetDbSelection(editor);
            if (selection == null || selection.Length == 0)
            {
                return new DimensionAlignmentResult { Message = "Не выбраны размеры для обработки." };
            }

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var targets = new List<DimensionAlignmentTarget>();

                foreach (var id in selection)
                {
                    var obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (TryCreateDimensionTextTarget(obj, out var target))
                    {
                        targets.Add(target);
                    }
                }

                var minimum = operation == DimensionOperation.Align ? 2 : 3;
                if (targets.Count < minimum)
                {
                    tr.Abort();
                    var operationName = operation == DimensionOperation.Align ? "выравнивания" : "распределения";
                    return new DimensionAlignmentResult
                    {
                        SelectedCount = selection.Length,
                        CandidateCount = targets.Count,
                        Message = $"Для {operationName} нужно минимум {minimum} распознанных размера с доступной позицией текста. Найдено: {targets.Count}."
                    };
                }

                if (operation == DimensionOperation.Align)
                {
                    AlignTargets(targets, axis);
                }
                else
                {
                    DistributeTargets(targets, axis);
                }

                tr.Commit();
                editor.UpdateScreen();

                var axisName = axis == LeaderAlignmentAxis.Horizontal ? "по горизонтали" : "по вертикали";
                var operationNameSuccess = operation == DimensionOperation.Align ? "выравнивание" : "распределение";
                return new DimensionAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = targets.Count,
                    ProcessedCount = operation == DimensionOperation.Align ? targets.Count - 1 : targets.Count,
                    Message = $"Размеры: {operationNameSuccess} {axisName} выполнено. Обработано объектов: {targets.Count}."
                };
            }
        }

        private static ObjectId[]? GetDbSelection(Editor editor)
        {
            var implied = editor.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                return implied.Value.GetObjectIds();
            }

            var options = new PromptSelectionOptions { MessageForAdding = "\nВыберите размеры для обработки: " };
            var picked = editor.GetSelection(options);
            return picked.Status == PromptStatus.OK && picked.Value != null && picked.Value.Count > 0
                ? picked.Value.GetObjectIds()
                : null;
        }

        private static bool TryGetReferencePoint(Editor editor, LeaderAlignmentAxis axis, out AlignmentPoint point)
        {
            point = default;
            var axisText = axis == LeaderAlignmentAxis.Horizontal ? "Y" : "X";
            var options = new PromptPointOptions($"\nУкажите точку, по координате {axisText} которой нужно выровнять текст размеров: ");
            var result = editor.GetPoint(options);
            if (result.Status != PromptStatus.OK)
            {
                return false;
            }

            point = new AlignmentPoint(result.Value.X, result.Value.Y, result.Value.Z);
            return true;
        }

        private static bool TryCreateDimensionTextTarget(object obj, out DimensionAlignmentTarget target)
        {
            target = default!;

            if (!IsDimensionCandidate(obj))
            {
                return false;
            }

            var property = obj.GetType().GetProperty("TextPosition", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead || !property.CanWrite)
            {
                return false;
            }

            var value = property.GetValue(obj);
            if (!TryGetPoint(value, out var point))
            {
                return false;
            }

            target = new DimensionAlignmentTarget(point, alignedPoint =>
            {
                TrySetDefaultTextPosition(obj, false);
                property.SetValue(obj, CreatePointValue(property.PropertyType, alignedPoint));
                TryRecomputeDimensionBlock(obj);
                MarkObjectModified(obj);
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
            var typeName = type.Name;
            var fullName = type.FullName ?? string.Empty;
            var rxName = obj is Entity entity ? entity.GetRXClass()?.Name ?? string.Empty : string.Empty;

            return typeName.Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                   fullName.Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                   rxName.Contains("Dimension", StringComparison.OrdinalIgnoreCase) ||
                   rxName.StartsWith("AcDb", StringComparison.OrdinalIgnoreCase) && rxName.Contains("Dim", StringComparison.OrdinalIgnoreCase);
        }

        private static void AlignTargets(IReadOnlyList<DimensionAlignmentTarget> targets, LeaderAlignmentAxis axis)
        {
            var basePoint = targets[0].Point;
            foreach (var target in targets.Skip(1))
            {
                var current = target.Point;
                var aligned = axis == LeaderAlignmentAxis.Horizontal
                    ? new AlignmentPoint(current.X, basePoint.Y, current.Z)
                    : new AlignmentPoint(basePoint.X, current.Y, current.Z);

                target.Apply(aligned);
            }
        }

        private static void AlignTargetsToPoint(IReadOnlyList<DimensionAlignmentTarget> targets, LeaderAlignmentAxis axis, AlignmentPoint referencePoint)
        {
            foreach (var target in targets)
            {
                var current = target.Point;
                var aligned = axis == LeaderAlignmentAxis.Horizontal
                    ? new AlignmentPoint(current.X, referencePoint.Y, current.Z)
                    : new AlignmentPoint(referencePoint.X, current.Y, current.Z);

                target.Apply(aligned);
            }
        }

        private static void DistributeTargets(IReadOnlyList<DimensionAlignmentTarget> targets, LeaderAlignmentAxis axis)
        {
            var ordered = axis == LeaderAlignmentAxis.Horizontal
                ? targets.OrderBy(t => t.Point.X).ToArray()
                : targets.OrderBy(t => t.Point.Y).ToArray();

            var first = ordered.First().Point;
            var last = ordered.Last().Point;
            var step = axis == LeaderAlignmentAxis.Horizontal
                ? (last.X - first.X) / (ordered.Length - 1)
                : (last.Y - first.Y) / (ordered.Length - 1);

            for (var i = 0; i < ordered.Length; i++)
            {
                var current = ordered[i].Point;
                var distributed = axis == LeaderAlignmentAxis.Horizontal
                    ? new AlignmentPoint(first.X + step * i, current.Y, current.Z)
                    : new AlignmentPoint(current.X, first.Y + step * i, current.Z);

                ordered[i].Apply(distributed);
            }
        }

        private static bool TrySetDefaultTextPosition(object obj, bool useDefault)
        {
            var property = obj.GetType().GetProperty("UsingDefaultTextPosition", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(bool))
            {
                return false;
            }

            try
            {
                property.SetValue(obj, useDefault);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryRecomputeDimensionBlock(object obj)
        {
            try
            {
                obj.GetType().GetMethod("RecomputeDimensionBlock", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)?.Invoke(obj, Array.Empty<object>());
            }
            catch
            {
            }
        }

        private static void MarkObjectModified(object obj)
        {
            try
            {
                obj.GetType().GetMethod("RecordGraphicsModified", BindingFlags.Instance | BindingFlags.Public, new[] { typeof(bool) })?.Invoke(obj, new object[] { true });
            }
            catch
            {
            }
        }

        private static bool TryGetPoint(object? value, out AlignmentPoint point)
        {
            point = default;
            if (value == null)
            {
                return false;
            }

            var type = value.GetType();
            var x = type.GetProperty("X");
            var y = type.GetProperty("Y");
            var z = type.GetProperty("Z");
            if (x == null || y == null)
            {
                return false;
            }

            try
            {
                point = new AlignmentPoint(
                    Convert.ToDouble(x.GetValue(value)),
                    Convert.ToDouble(y.GetValue(value)),
                    z == null ? 0.0 : Convert.ToDouble(z.GetValue(value)));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object CreatePointValue(Type pointType, AlignmentPoint point)
        {
            var ctor3 = pointType.GetConstructor(new[] { typeof(double), typeof(double), typeof(double) });
            if (ctor3 != null)
            {
                return ctor3.Invoke(new object[] { point.X, point.Y, point.Z });
            }

            var ctor2 = pointType.GetConstructor(new[] { typeof(double), typeof(double) });
            if (ctor2 != null)
            {
                return ctor2.Invoke(new object[] { point.X, point.Y });
            }

            var value = Activator.CreateInstance(pointType) ?? throw new InvalidOperationException($"Не удалось создать точку типа {pointType.FullName}.");
            pointType.GetProperty("X")?.SetValue(value, point.X);
            pointType.GetProperty("Y")?.SetValue(value, point.Y);
            pointType.GetProperty("Z")?.SetValue(value, point.Z);
            return value;
        }

        private readonly struct AlignmentPoint
        {
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public AlignmentPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        }

        private readonly struct DimensionAlignmentTarget
        {
            private readonly Action<AlignmentPoint> _apply;
            public AlignmentPoint Point { get; }
            public DimensionAlignmentTarget(AlignmentPoint point, Action<AlignmentPoint> apply) { Point = point; _apply = apply; }
            public void Apply(AlignmentPoint point) => _apply(point);
        }
    }
}
