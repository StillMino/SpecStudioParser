using HostMgd.ApplicationServices;
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
    /// Сервис выравнивания выносок.
    /// Сначала пытается использовать уже загруженный MultiCAD.NET API, затем переходит
    /// к резервному HostMgd/Teigha-пути. Важно: сервис не выполняет явную загрузку
    /// MultiCAD-сборок через Type.GetType("..., assembly"), потому что часть SDK-модулей
    /// nanoCAD не является одиночными .NET-сборками и может давать FileLoadException.
    /// </summary>
    public sealed class MultiCadLeaderAlignmentService
    {
        private static readonly string[] LeaderTypeMarkers =
        {
            "McNote",
            "McNotePosition",
            "McNoteChain",
            "McNoteComb",
            "McNoteLinearMark",
            "McMark",
            "McAnnotation",
            "McSpecLeader",
            "MLeader",
            "Leader",
            "Callout",
            "Выноска"
        };

        private static readonly string[] AnchorPointProperties =
        {
            "Origin",
            "TextPosition",
            "TextPos",
            "PntText",
            "Start",
            "End",
            "TextLocation",
            "BlockPosition",
            "Location",
            "Position"
        };

        public LeaderAlignmentResult AlignSelectedLeaders(LeaderAlignmentAxis axis)
        {
            var multiCadResult = TryAlignSelectedMultiCadLeaders(axis);
            if (multiCadResult != null)
            {
                return multiCadResult;
            }

            return AlignSelectedDbLeadersFallback(axis);
        }

        private static LeaderAlignmentResult? TryAlignSelectedMultiCadLeaders(LeaderAlignmentAxis axis)
        {
            Type? objectManagerType;
            try
            {
                objectManagerType = ResolveLoadedType("Multicad.DatabaseServices.McObjectManager");
            }
            catch (FileLoadException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }

            if (objectManagerType == null)
            {
                return null;
            }

            try
            {
                var selectionIds = GetCurrentMultiCadSelection(objectManagerType);
                if (selectionIds.Count == 0)
                {
                    return new LeaderAlignmentResult { Message = "Не выбраны выноски для выравнивания." };
                }

                StartMultiCadTransaction(objectManagerType);

                try
                {
                    var targets = new List<LeaderAlignmentTarget>();

                    foreach (var id in selectionIds)
                    {
                        var obj = GetMultiCadObject(objectManagerType, id);
                        if (obj == null || !IsLeaderCandidate(obj))
                        {
                            continue;
                        }

                        if (TryCreateTarget(obj, out var target))
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
                            Message = "Для выравнивания нужно минимум две распознанные MultiCAD-выноски."
                        };
                    }

                    AlignTargets(targets, axis);
                    EndMultiCadTransaction(objectManagerType);
                    UpdateMultiCadGraphics(objectManagerType);

                    var axisName = axis == LeaderAlignmentAxis.Horizontal ? "по горизонтали" : "по вертикали";
                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selectionIds.Count,
                        CandidateCount = targets.Count,
                        AlignedCount = targets.Count - 1,
                        Message = $"MultiCAD-выравнивание {axisName} выполнено. Обработано выносок: {targets.Count}."
                    };
                }
                catch
                {
                    AbortMultiCadTransaction(objectManagerType);
                    throw;
                }
            }
            catch (FileLoadException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
            catch (Exception ex)
            {
                return new LeaderAlignmentResult
                {
                    Message = $"Ошибка MultiCAD-выравнивания: {ex.Message}"
                };
            }
        }

        private static LeaderAlignmentResult AlignSelectedDbLeadersFallback(LeaderAlignmentAxis axis)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return new LeaderAlignmentResult { Message = "Нет активного документа nanoCAD." };
            }

            var editor = doc.Editor;
            var selection = GetDbSelection(editor);
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

                AlignTargets(targets, axis);
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

        private static void AlignTargets(IReadOnlyList<LeaderAlignmentTarget> targets, LeaderAlignmentAxis axis)
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

        private static List<object> GetCurrentMultiCadSelection(Type objectManagerType)
        {
            var result = new List<object>();

            var selectionSet = objectManagerType.GetProperty("SelectionSet", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var selectionSetType = selectionSet?.GetType()
                ?? objectManagerType.GetNestedType("SelectionSet", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

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
            var getObjectMethod = objectManagerType
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetObject" && m.GetParameters().Length == 1);

            if (getObjectMethod != null)
            {
                return getObjectMethod.Invoke(null, new[] { id });
            }

            var idGetObject = id.GetType().GetMethod("GetObject", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
            return idGetObject?.Invoke(id, Array.Empty<object>());
        }

        private static void StartMultiCadTransaction(Type objectManagerType)
        {
            InvokeTransactionMethod(objectManagerType, "Start");
        }

        private static void EndMultiCadTransaction(Type objectManagerType)
        {
            InvokeTransactionMethod(objectManagerType, "End");
        }

        private static void AbortMultiCadTransaction(Type objectManagerType)
        {
            InvokeTransactionMethod(objectManagerType, "Abort");
        }

        private static void UpdateMultiCadGraphics(Type objectManagerType)
        {
            InvokeTransactionMethod(objectManagerType, "UpdateGraphics");
            objectManagerType.GetMethod("UpdateAll", BindingFlags.Static | BindingFlags.Public, Type.EmptyTypes)?.Invoke(null, Array.Empty<object>());
        }

        private static void InvokeTransactionMethod(Type objectManagerType, string methodName)
        {
            var transactions = objectManagerType.GetProperty("Transactions", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var transactionsType = transactions?.GetType()
                ?? objectManagerType.GetNestedType("Transactions", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

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
                Type? type = null;
                try
                {
                    type = assembly.GetType(fullName, false, true);
                }
                catch (FileLoadException)
                {
                    continue;
                }
                catch (BadImageFormatException)
                {
                    continue;
                }
                catch (ReflectionTypeLoadException)
                {
                    continue;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static ObjectId[]? GetDbSelection(Editor editor)
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

        private static bool IsLeaderCandidate(object obj)
        {
            var type = obj.GetType();
            var typeName = type.Name;
            var fullName = type.FullName ?? string.Empty;
            var rxName = obj is Entity entity ? entity.GetRXClass()?.Name ?? string.Empty : string.Empty;

            return LeaderTypeMarkers.Any(marker =>
                typeName.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                fullName.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                rxName.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryCreateTarget(object obj, out LeaderAlignmentTarget target)
        {
            foreach (var propertyName in AnchorPointProperties)
            {
                var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanRead || !property.CanWrite)
                {
                    continue;
                }

                if (TryGetPoint(property.GetValue(obj), out var point))
                {
                    target = new LeaderAlignmentTarget(obj, property, property.PropertyType, point);
                    return true;
                }
            }

            target = default!;
            return false;
        }

        private static bool TryGetPoint(object? value, out AlignmentPoint point)
        {
            point = default;
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
                point = new AlignmentPoint(x, y, z);
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

            var value = Activator.CreateInstance(pointType)
                ?? throw new InvalidOperationException($"Не удалось создать точку типа {pointType.FullName}.");

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

            public AlignmentPoint(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        private readonly struct LeaderAlignmentTarget
        {
            private readonly object _target;
            private readonly PropertyInfo _property;
            private readonly Type _pointType;

            public AlignmentPoint Point { get; }

            public LeaderAlignmentTarget(object target, PropertyInfo property, Type pointType, AlignmentPoint point)
            {
                _target = target;
                _property = property;
                _pointType = pointType;
                Point = point;
            }

            public void Apply(AlignmentPoint point)
            {
                _property.SetValue(_target, CreatePointValue(_pointType, point));
            }
        }
    }
}
