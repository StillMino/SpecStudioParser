using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.Services
{
    public enum LeaderAlignmentAxis
    {
        Horizontal,
        Vertical
    }

    public sealed class LeaderAlignmentResult
    {
        public int SelectedCount { get; init; }
        public int CandidateCount { get; init; }
        public int AlignedCount { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// Сервис выравнивания выносок. Сделан как отдельный адаптер, чтобы дальше заменить
    /// reflection-слой на прямые вызовы MultiCAD.NET API для конкретных символьных примитивов.
    /// Сейчас команда работает с распознанными объектами выносок в активном документе nanoCAD
    /// и выравнивает их текстовую/блочную точку размещения.
    /// </summary>
    public sealed class MultiCadLeaderAlignmentService
    {
        private static readonly string[] LeaderTypeMarkers =
        {
            "MLeader",
            "Leader",
            "McLeader",
            "McMLeader",
            "McNote",
            "McCallout",
            "Callout",
            "Выноска"
        };

        private static readonly string[] AnchorPointProperties =
        {
            "TextLocation",
            "BlockPosition",
            "Location",
            "Position"
        };

        public LeaderAlignmentResult AlignSelectedLeaders(LeaderAlignmentAxis axis)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return new LeaderAlignmentResult { Message = "Нет активного документа nanoCAD." };
            }

            var editor = doc.Editor;
            var selection = GetSelection(editor);
            if (selection == null || selection.Length == 0)
            {
                return new LeaderAlignmentResult { Message = "Не выбраны выноски для выравнивания." };
            }

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var targets = new List<LeaderAlignmentTarget>();

                foreach (var id in selection)
                {
                    var obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (obj is not Entity entity)
                    {
                        continue;
                    }

                    if (!IsLeaderCandidate(entity))
                    {
                        continue;
                    }

                    if (TryCreateTarget(entity, out var target))
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
                        Message = "Для выравнивания нужно минимум две распознанные выноски."
                    };
                }

                var basePoint = targets[0].Point;
                foreach (var target in targets.Skip(1))
                {
                    var current = target.Point;
                    var aligned = axis == LeaderAlignmentAxis.Horizontal
                        ? new Point3d(current.X, basePoint.Y, current.Z)
                        : new Point3d(basePoint.X, current.Y, current.Z);

                    target.Apply(aligned);
                }

                tr.Commit();
                editor.UpdateScreen();

                var axisName = axis == LeaderAlignmentAxis.Horizontal ? "по горизонтали" : "по вертикали";
                return new LeaderAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = targets.Count,
                    AlignedCount = targets.Count - 1,
                    Message = $"Выравнивание {axisName} выполнено. Обработано выносок: {targets.Count}."
                };
            }
        }

        private static ObjectId[]? GetSelection(Editor editor)
        {
            var implied = editor.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                return implied.Value.GetObjectIds();
            }

            var options = new PromptSelectionOptions
            {
                MessageForAdding = "\nВыберите выноски для выравнивания: "
            };

            var picked = editor.GetSelection(options);
            if (picked.Status == PromptStatus.OK && picked.Value != null && picked.Value.Count > 0)
            {
                return picked.Value.GetObjectIds();
            }

            return null;
        }

        private static bool IsLeaderCandidate(Entity entity)
        {
            var typeName = entity.GetType().Name;
            var rxName = entity.GetRXClass()?.Name ?? string.Empty;

            return LeaderTypeMarkers.Any(marker =>
                typeName.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                rxName.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryCreateTarget(Entity entity, out LeaderAlignmentTarget target)
        {
            foreach (var propertyName in AnchorPointProperties)
            {
                if (TryGetPointProperty(entity, propertyName, out var point) &&
                    TryGetWritableProperty(entity, propertyName, out var property))
                {
                    target = new LeaderAlignmentTarget(entity, property, point);
                    return true;
                }
            }

            target = default!;
            return false;
        }

        private static bool TryGetWritableProperty(object obj, string propertyName, out PropertyInfo property)
        {
            property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!;
            return property != null && property.CanWrite;
        }

        private static bool TryGetPointProperty(object obj, string propertyName, out Point3d point)
        {
            point = default;
            var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead)
            {
                return false;
            }

            var value = property.GetValue(obj);
            if (value is Point3d point3d)
            {
                point = point3d;
                return true;
            }

            if (value == null)
            {
                return false;
            }

            var valueType = value.GetType();
            var xProp = valueType.GetProperty("X");
            var yProp = valueType.GetProperty("Y");
            var zProp = valueType.GetProperty("Z");

            if (xProp == null || yProp == null)
            {
                return false;
            }

            try
            {
                var x = Convert.ToDouble(xProp.GetValue(value));
                var y = Convert.ToDouble(yProp.GetValue(value));
                var z = zProp == null ? 0.0 : Convert.ToDouble(zProp.GetValue(value));
                point = new Point3d(x, y, z);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private readonly struct LeaderAlignmentTarget
        {
            private readonly Entity _entity;
            private readonly PropertyInfo _property;

            public Point3d Point { get; }

            public LeaderAlignmentTarget(Entity entity, PropertyInfo property, Point3d point)
            {
                _entity = entity;
                _property = property;
                Point = point;
            }

            public void Apply(Point3d point)
            {
                _property.SetValue(_entity, point);
            }
        }
    }
}
