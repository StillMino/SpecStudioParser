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

        public DimensionAlignmentResult DistributeSelectedDimensions(LeaderAlignmentAxis axis)
        {
            return ProcessSelectedDimensions(axis, DimensionOperation.Distribute);
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
                TryDisableDefaultTextPosition(obj);
                property.SetValue(obj, CreatePointValue(property.PropertyType, alignedPoint));
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

        private static void TryDisableDefaultTextPosition(object obj)
        {
            var property = obj.GetType().GetProperty("UsingDefaultTextPosition", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(bool))
            {
                return;
            }

            try
            {
                property.SetValue(obj, false);
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
