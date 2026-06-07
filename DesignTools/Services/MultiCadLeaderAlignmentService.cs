using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

    public sealed class MultiCadLeaderAlignmentService
    {
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
            var multiCadResult = TryAlignSelectedMultiCadLeaders(axis);
            if (multiCadResult != null && multiCadResult.CandidateCount > 0)
            {
                return multiCadResult;
            }

            var fallbackResult = AlignSelectedDbLeadersFallback(axis);
            if (fallbackResult.CandidateCount > 0 || multiCadResult == null)
            {
                return fallbackResult;
            }

            return multiCadResult;
        }

        private static LeaderAlignmentResult? TryAlignSelectedMultiCadLeaders(LeaderAlignmentAxis axis)
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
                            Message = "Для выравнивания нужно минимум две распознанные MultiCAD-выноски или мультивыноски."
                        };
                    }

                    AlignTargets(targets, axis);
                    EndMultiCadTransaction(objectManagerType);
                    UpdateMultiCadGraphics(objectManagerType);

                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selectionIds.Count,
                        CandidateCount = targets.Count,
                        AlignedCount = targets.Count - 1,
                        Message = BuildSuccessMessage(axis, targets.Count, true)
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
                return new LeaderAlignmentResult { Message = $"Ошибка MultiCAD-выравнивания: {ex.Message}" };
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
                    if (obj is not Entity entity || !IsLeaderCandidate(entity))
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
                        Message = "Для выравнивания нужно минимум две распознанные выноски или мультивыноски."
                    };
                }

                AlignTargets(targets, axis);
                tr.Commit();
                editor.UpdateScreen();

                return new LeaderAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = targets.Count,
                    AlignedCount = targets.Count - 1,
                    Message = BuildSuccessMessage(axis, targets.Count, false)
                };
            }
        }

        private static string BuildSuccessMessage(LeaderAlignmentAxis axis, int count, bool isMultiCad)
        {
            var source = isMultiCad ? "MultiCAD-" : string.Empty;
            var axisName = axis == LeaderAlignmentAxis.Horizontal ? "по горизонтали" : "по вертикали";
            return $"{source}выравнивание {axisName} выполнено. Обработано объектов: {count}.";
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

        private static bool TryCreateTarget(object obj, out LeaderAlignmentTarget target)
        {
            if (obj is MLeader mLeader && TryCreateTeighaMLeaderTarget(mLeader, out target))
            {
                return true;
            }

            if (IsMultileaderCandidate(obj) && TryCreateMultileaderTarget(obj, out target))
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

        private static bool TryCreateTeighaMLeaderTarget(MLeader mLeader, out LeaderAlignmentTarget target)
        {
            if (TryGetTeighaMLeaderAnchor(mLeader, out var anchor) && TryCreateTeighaMLeaderMoveTarget(mLeader, anchor, out target))
            {
                return true;
            }

            if (TryCreatePropertyPointTarget(mLeader, "TextLocation", out target) || TryCreatePropertyPointTarget(mLeader, "BlockPosition", out target))
            {
                return true;
            }

            target = default!;
            return false;
        }

        private static bool TryGetTeighaMLeaderAnchor(MLeader mLeader, out AlignmentPoint anchor)
        {
            if (TryGetPointByProperty(mLeader, "TextLocation", out anchor) || TryGetPointByProperty(mLeader, "BlockPosition", out anchor))
            {
                return true;
            }

            try
            {
                var extents = mLeader.GetContentGeomExtents();
                anchor = new AlignmentPoint(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) / 2.0);
                return true;
            }
            catch
            {
                anchor = default;
                return false;
            }
        }

        private static bool TryCreateTeighaMLeaderMoveTarget(MLeader mLeader, AlignmentPoint anchor, out LeaderAlignmentTarget target)
        {
            var moveMethod = typeof(MLeader).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "MoveMLeader" && m.GetParameters().Length == 2);

            if (moveMethod == null)
            {
                target = default!;
                return false;
            }

            var parameters = moveMethod.GetParameters();
            var moveTypeValues = parameters[1].ParameterType.IsEnum
                ? Enum.GetValues(parameters[1].ParameterType).Cast<object>().ToArray()
                : new[] { Activator.CreateInstance(parameters[1].ParameterType)! };

            target = new LeaderAlignmentTarget(anchor, alignedPoint =>
            {
                var delta = new Vector3d(alignedPoint.X - anchor.X, alignedPoint.Y - anchor.Y, alignedPoint.Z - anchor.Z);
                Exception? lastError = null;

                foreach (var moveType in moveTypeValues)
                {
                    try
                    {
                        moveMethod.Invoke(mLeader, new object[] { delta, moveType });
                        mLeader.RecordGraphicsModified(true);
                        return;
                    }
                    catch (TargetInvocationException ex) { lastError = ex.InnerException ?? ex; }
                    catch (Exception ex) { lastError = ex; }
                }

                throw lastError ?? new InvalidOperationException("Не удалось выполнить MoveMLeader для мультивыноски.");
            });
            return true;
        }

        private static bool TryCreateMultileaderTarget(object obj, out LeaderAlignmentTarget target)
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

        private static bool TryGetPointByProperty(object obj, string propertyName, out AlignmentPoint point)
        {
            var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead)
            {
                point = default;
                return false;
            }
            return TryGetPoint(property.GetValue(obj), out point);
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
