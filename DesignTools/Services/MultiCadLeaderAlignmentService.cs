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

    public sealed class MultiCadLeaderAlignmentService
    {
        private enum DbTargetMode
        {
            AllSupported,
            TeighaMLeaderOnly
        }

        private enum LeaderOperation
        {
            Align,
            Distribute
        }

        private static readonly string[] LeaderTypeMarkers =
        {
            "McNote", "McNotePosition", "McNoteChain", "McNoteComb", "McNoteLinearMark",
            "McMark", "McAnnotation", "McSpecLeader", "MLeader", "MultiLeader", "Leader",
            "Callout", "Выноска", "Мультивыноска"
        };

        private static readonly string[] AnchorPointProperties =
        {
            "TextLocation", "BlockPosition", "Origin", "TextPosition", "TextPos", "PntText",
            "Start", "End", "Location", "Position"
        };

        private static readonly string[] NestedContentProperties = { "MText", "Text", "Annotation", "Content" };
        private static readonly string[] NestedPointProperties = { "Location", "TextLocation", "Position", "InsertionPoint" };

        public LeaderAlignmentResult AlignSelectedLeaders(LeaderAlignmentAxis axis)
        {
            var multiCadResult = TryProcessSelectedMultiCadLeaders(axis, LeaderOperation.Align);
            if (multiCadResult != null && multiCadResult.CandidateCount > 0)
            {
                return multiCadResult;
            }

            var fallbackResult = ProcessSelectedDbLeaders(axis, DbTargetMode.AllSupported, LeaderOperation.Align);
            if (fallbackResult.CandidateCount > 0 || multiCadResult == null)
            {
                return fallbackResult;
            }

            return multiCadResult;
        }

        public LeaderAlignmentResult AlignSelectedMultiCadLeaders(LeaderAlignmentAxis axis)
        {
            var result = TryProcessSelectedMultiCadLeaders(axis, LeaderOperation.Align);
            return result ?? new LeaderAlignmentResult
            {
                Message = "MultiCAD API недоступен в текущем сеансе nanoCAD."
            };
        }

        public LeaderAlignmentResult AlignSelectedTeighaMLeaders(LeaderAlignmentAxis axis)
        {
            return ProcessSelectedDbLeaders(axis, DbTargetMode.TeighaMLeaderOnly, LeaderOperation.Align);
        }

        public LeaderAlignmentResult DistributeSelectedMultiCadLeaders(LeaderAlignmentAxis axis)
        {
            var result = TryProcessSelectedMultiCadLeaders(axis, LeaderOperation.Distribute);
            return result ?? new LeaderAlignmentResult
            {
                Message = "MultiCAD API недоступен в текущем сеансе nanoCAD."
            };
        }

        public LeaderAlignmentResult DistributeSelectedTeighaMLeaders(LeaderAlignmentAxis axis)
        {
            return ProcessSelectedDbLeaders(axis, DbTargetMode.TeighaMLeaderOnly, LeaderOperation.Distribute);
        }

        private static LeaderAlignmentResult? TryProcessSelectedMultiCadLeaders(LeaderAlignmentAxis axis, LeaderOperation operation)
        {
            Type? objectManagerType;
            try
            {
                objectManagerType = ResolveLoadedType("Multicad.DatabaseServices.McObjectManager");
            }
            catch (FileLoadException) { return null; }
            catch (BadImageFormatException) { return null; }

            if (objectManagerType == null)
            {
                return null;
            }

            try
            {
                var selectionIds = GetCurrentMultiCadSelection(objectManagerType);
                if (selectionIds.Count == 0)
                {
                    return new LeaderAlignmentResult { Message = "Не выбраны MultiCAD-выноски для обработки." };
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

                        if (TryCreateMultiCadTarget(obj, out var target))
                        {
                            targets.Add(target);
                        }
                    }

                    if (!ValidateTargetCount(targets.Count, operation, "MultiCAD-выноски", out var validationMessage))
                    {
                        AbortMultiCadTransaction(objectManagerType);
                        return new LeaderAlignmentResult
                        {
                            SelectedCount = selectionIds.Count,
                            CandidateCount = targets.Count,
                            Message = validationMessage
                        };
                    }

                    ApplyOperation(targets, axis, operation);
                    EndMultiCadTransaction(objectManagerType);
                    UpdateMultiCadGraphics(objectManagerType);

                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selectionIds.Count,
                        CandidateCount = targets.Count,
                        AlignedCount = operation == LeaderOperation.Align ? targets.Count - 1 : targets.Count,
                        Message = BuildSuccessMessage(axis, targets.Count, "MultiCAD", operation)
                    };
                }
                catch
                {
                    AbortMultiCadTransaction(objectManagerType);
                    throw;
                }
            }
            catch (FileLoadException) { return null; }
            catch (BadImageFormatException) { return null; }
            catch (Exception ex)
            {
                return new LeaderAlignmentResult { Message = $"Ошибка MultiCAD-обработки: {ex.Message}" };
            }
        }

        private static LeaderAlignmentResult ProcessSelectedDbLeaders(LeaderAlignmentAxis axis, DbTargetMode mode, LeaderOperation operation)
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
                return new LeaderAlignmentResult { Message = "Не выбраны объекты для обработки." };
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

                    if (TryCreateDbTarget(entity, mode, out var target))
                    {
                        targets.Add(target);
                    }
                }

                var targetName = mode == DbTargetMode.TeighaMLeaderOnly ? "Teigha-мультивыноски" : "выноски или мультивыноски";
                if (!ValidateTargetCount(targets.Count, operation, targetName, out var validationMessage))
                {
                    tr.Abort();
                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selection.Length,
                        CandidateCount = targets.Count,
                        Message = validationMessage
                    };
                }

                ApplyOperation(targets, axis, operation);
                tr.Commit();
                editor.UpdateScreen();

                return new LeaderAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = targets.Count,
                    AlignedCount = operation == LeaderOperation.Align ? targets.Count - 1 : targets.Count,
                    Message = BuildSuccessMessage(axis, targets.Count, mode == DbTargetMode.TeighaMLeaderOnly ? "Teigha" : "", operation)
                };
            }
        }

        private static bool ValidateTargetCount(int count, LeaderOperation operation, string targetName, out string message)
        {
            var minimum = operation == LeaderOperation.Align ? 2 : 3;
            if (count >= minimum)
            {
                message = string.Empty;
                return true;
            }

            var operationName = operation == LeaderOperation.Align ? "выравнивания" : "распределения";
            message = $"Для {operationName} нужно минимум {minimum} распознанные {targetName}. Найдено: {count}.";
            return false;
        }

        private static string BuildSuccessMessage(LeaderAlignmentAxis axis, int count, string source, LeaderOperation operation)
        {
            var prefix = string.IsNullOrWhiteSpace(source) ? string.Empty : source + "-";
            var axisName = axis == LeaderAlignmentAxis.Horizontal ? "по горизонтали" : "по вертикали";
            var operationName = operation == LeaderOperation.Align ? "выравнивание" : "распределение";
            return $"{prefix}{operationName} {axisName} выполнено. Обработано объектов: {count}.";
        }

        private static void ApplyOperation(IReadOnlyList<LeaderAlignmentTarget> targets, LeaderAlignmentAxis axis, LeaderOperation operation)
        {
            if (operation == LeaderOperation.Distribute)
            {
                DistributeTargets(targets, axis);
                return;
            }

            AlignTargets(targets, axis);
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

        private static void DistributeTargets(IReadOnlyList<LeaderAlignmentTarget> targets, LeaderAlignmentAxis axis)
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

        private static bool TryCreateDbTarget(Entity entity, DbTargetMode mode, out LeaderAlignmentTarget target)
        {
            if (mode == DbTargetMode.TeighaMLeaderOnly)
            {
                if (entity is MLeader mLeader && TryCreateTeighaMLeaderContentTarget(mLeader, out target))
                {
                    return true;
                }

                target = default!;
                return false;
            }

            if (entity is MLeader mLeaderAll && TryCreateTeighaMLeaderContentTarget(mLeaderAll, out target))
            {
                return true;
            }

            if (!IsLeaderCandidate(entity))
            {
                target = default!;
                return false;
            }

            return TryCreateMultiCadTarget(entity, out target);
        }

        private static bool TryCreateMultiCadTarget(object obj, out LeaderAlignmentTarget target)
        {
            if (IsMultileaderCandidate(obj) && TryCreateReflectionMultileaderTarget(obj, out target))
            {
                return true;
            }

            foreach (var propertyName in AnchorPointProperties)
            {
                if (TryCreatePropertyPointTarget(obj, propertyName, out target))
                {
                    return true;
                }
            }

            target = default!;
            return false;
        }

        private static bool TryCreateTeighaMLeaderContentTarget(MLeader mLeader, out LeaderAlignmentTarget target)
        {
            if (TryCreatePropertyPointTarget(mLeader, "TextLocation", out target) ||
                TryCreatePropertyPointTarget(mLeader, "BlockPosition", out target))
            {
                return true;
            }

            if (TryCreateNestedContentPointTarget(mLeader, "MText", "Location", out target) ||
                TryCreateNestedContentPointTarget(mLeader, "MText", "InsertionPoint", out target))
            {
                return true;
            }

            target = default!;
            return false;
        }

        private static bool TryCreateReflectionMultileaderTarget(object obj, out LeaderAlignmentTarget target)
        {
            foreach (var propertyName in new[] { "TextLocation", "BlockPosition", "TextPosition", "Location", "Position" })
            {
                if (TryCreatePropertyPointTarget(obj, propertyName, out target))
                {
                    return true;
                }
            }

            foreach (var methodBaseName in new[] { "TextLocation", "BlockPosition", "ContentLocation", "DoglegPoint" })
            {
                if (TryCreateMethodPointTarget(obj, methodBaseName, out target))
                {
                    return true;
                }
            }

            foreach (var nestedPropertyName in NestedContentProperties)
            {
                var nestedProperty = obj.GetType().GetProperty(nestedPropertyName, BindingFlags.Instance | BindingFlags.Public);
                if (nestedProperty == null || !nestedProperty.CanRead) continue;

                var nestedValue = nestedProperty.GetValue(obj);
                if (nestedValue == null) continue;

                foreach (var nestedPointName in NestedPointProperties)
                {
                    if (TryCreateNestedPropertyPointTarget(obj, nestedProperty, nestedValue, nestedPointName, out target))
                    {
                        return true;
                    }
                }
            }

            target = default!;
            return false;
        }

        private static bool TryCreatePropertyPointTarget(object obj, string propertyName, out LeaderAlignmentTarget target)
        {
            var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead || !property.CanWrite || !TryGetPoint(property.GetValue(obj), out var point))
            {
                target = default!;
                return false;
            }

            target = new LeaderAlignmentTarget(point, alignedPoint =>
            {
                property.SetValue(obj, CreatePointValue(property.PropertyType, alignedPoint));
                MarkObjectModified(obj);
            });
            return true;
        }

        private static bool TryCreateNestedContentPointTarget(object owner, string contentPropertyName, string pointPropertyName, out LeaderAlignmentTarget target)
        {
            var nestedProperty = owner.GetType().GetProperty(contentPropertyName, BindingFlags.Instance | BindingFlags.Public);
            if (nestedProperty == null || !nestedProperty.CanRead)
            {
                target = default!;
                return false;
            }

            var nestedValue = nestedProperty.GetValue(owner);
            if (nestedValue == null)
            {
                target = default!;
                return false;
            }

            return TryCreateNestedPropertyPointTarget(owner, nestedProperty, nestedValue, pointPropertyName, out target);
        }

        private static bool TryCreateNestedPropertyPointTarget(object owner, PropertyInfo nestedProperty, object nestedValue, string pointPropertyName, out LeaderAlignmentTarget target)
        {
            var pointProperty = nestedValue.GetType().GetProperty(pointPropertyName, BindingFlags.Instance | BindingFlags.Public);
            if (pointProperty == null || !pointProperty.CanRead || !pointProperty.CanWrite || !TryGetPoint(pointProperty.GetValue(nestedValue), out var point))
            {
                target = default!;
                return false;
            }

            target = new LeaderAlignmentTarget(point, alignedPoint =>
            {
                pointProperty.SetValue(nestedValue, CreatePointValue(pointProperty.PropertyType, alignedPoint));
                if (nestedProperty.CanWrite)
                {
                    nestedProperty.SetValue(owner, nestedValue);
                }
                MarkObjectModified(owner);
            });
            return true;
        }

        private static bool TryCreateMethodPointTarget(object obj, string baseName, out LeaderAlignmentTarget target)
        {
            var type = obj.GetType();
            var getMethod = type.GetMethod("Get" + baseName, BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
            var setMethod = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "Set" + baseName && m.GetParameters().Length == 1);

            if (getMethod == null || setMethod == null)
            {
                target = default!;
                return false;
            }

            var value = getMethod.Invoke(obj, Array.Empty<object>());
            if (!TryGetPoint(value, out var point))
            {
                target = default!;
                return false;
            }

            var parameterType = setMethod.GetParameters()[0].ParameterType;
            target = new LeaderAlignmentTarget(point, alignedPoint =>
            {
                setMethod.Invoke(obj, new[] { CreatePointValue(parameterType, alignedPoint) });
                MarkObjectModified(obj);
            });
            return true;
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

        private static ObjectId[]? GetDbSelection(Editor editor)
        {
            var implied = editor.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                return implied.Value.GetObjectIds();
            }
            var options = new PromptSelectionOptions { MessageForAdding = "\nВыберите выноски для выравнивания: " };
            var picked = editor.GetSelection(options);
            return picked.Status == PromptStatus.OK && picked.Value != null && picked.Value.Count > 0 ? picked.Value.GetObjectIds() : null;
        }

        private static bool IsLeaderCandidate(object obj)
        {
            var type = obj.GetType();
            var typeName = type.Name;
            var fullName = type.FullName ?? string.Empty;
            var rxName = obj is Entity entity ? entity.GetRXClass()?.Name ?? string.Empty : string.Empty;
            return LeaderTypeMarkers.Any(marker => typeName.Contains(marker, StringComparison.OrdinalIgnoreCase) || fullName.Contains(marker, StringComparison.OrdinalIgnoreCase) || rxName.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsMultileaderCandidate(object obj)
        {
            var type = obj.GetType();
            var typeName = type.Name;
            var fullName = type.FullName ?? string.Empty;
            var rxName = obj is Entity entity ? entity.GetRXClass()?.Name ?? string.Empty : string.Empty;
            return typeName.Contains("MLeader", StringComparison.OrdinalIgnoreCase) || fullName.Contains("MLeader", StringComparison.OrdinalIgnoreCase) || typeName.Contains("MultiLeader", StringComparison.OrdinalIgnoreCase) || fullName.Contains("MultiLeader", StringComparison.OrdinalIgnoreCase) || rxName.Contains("MLeader", StringComparison.OrdinalIgnoreCase) || rxName.Contains("MULTILEADER", StringComparison.OrdinalIgnoreCase);
        }

        private static void MarkObjectModified(object obj)
        {
            try { obj.GetType().GetMethod("RecordGraphicsModified", BindingFlags.Instance | BindingFlags.Public, new[] { typeof(bool) })?.Invoke(obj, new object[] { true }); }
            catch { }
        }

        private static bool TryGetPoint(object? value, out AlignmentPoint point)
        {
            point = default;
            if (value == null) return false;
            var valueType = value.GetType();
            var xProp = valueType.GetProperty("X");
            var yProp = valueType.GetProperty("Y");
            var zProp = valueType.GetProperty("Z");
            if (xProp == null || yProp == null) return false;
            try
            {
                point = new AlignmentPoint(Convert.ToDouble(xProp.GetValue(value)), Convert.ToDouble(yProp.GetValue(value)), zProp == null ? 0.0 : Convert.ToDouble(zProp.GetValue(value)));
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

        private readonly struct AlignmentPoint
        {
            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public AlignmentPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        }

        private readonly struct LeaderAlignmentTarget
        {
            private readonly Action<AlignmentPoint> _apply;
            public AlignmentPoint Point { get; }
            public LeaderAlignmentTarget(AlignmentPoint point, Action<AlignmentPoint> apply) { Point = point; _apply = apply; }
            public void Apply(AlignmentPoint point) => _apply(point);
        }
    }
}
