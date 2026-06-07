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
    /// <summary>
    /// Выравнивание выбранных выносок по координате явно указанной пользователем точки.
    /// Для MultiCAD используется MultiCAD selection/API, для стандартных мультивыносок — Teigha MLeader.
    /// </summary>
    public sealed class LeaderPointAlignmentService
    {
        private static readonly string[] AnchorPointProperties =
        {
            "TextLocation", "BlockPosition", "Origin", "TextPosition", "TextPos", "PntText",
            "Start", "End", "Location", "Position"
        };

        public LeaderAlignmentResult AlignSelectedMultiCadLeadersToPoint(LeaderAlignmentAxis axis)
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
                return new LeaderAlignmentResult { Message = "Не выбраны MultiCAD-выноски для выравнивания по точке." };
            }

            if (!TryGetReferencePoint(axis, out var referencePoint))
            {
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = "Указание точки отменено." };
            }

            try
            {
                StartMultiCadTransaction(objectManagerType);
                try
                {
                    var targets = new List<LeaderPointAlignmentTarget>();
                    foreach (var id in selectionIds)
                    {
                        var obj = GetMultiCadObject(objectManagerType, id);
                        if (obj == null || !IsLeaderCandidate(obj))
                        {
                            continue;
                        }

                        if (TryCreatePointTarget(obj, out var target))
                        {
                            targets.Add(target);
                        }
                    }

                    if (targets.Count == 0)
                    {
                        AbortMultiCadTransaction(objectManagerType);
                        return new LeaderAlignmentResult
                        {
                            SelectedCount = selectionIds.Count,
                            CandidateCount = 0,
                            Message = "Среди выбранных объектов не найдено MultiCAD-выносок с доступной точкой выравнивания."
                        };
                    }

                    ApplyReferencePoint(targets, axis, referencePoint);
                    EndMultiCadTransaction(objectManagerType);
                    UpdateMultiCadGraphics(objectManagerType);

                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selectionIds.Count,
                        CandidateCount = targets.Count,
                        AlignedCount = targets.Count,
                        Message = BuildSuccessMessage("MultiCAD", axis, targets.Count)
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
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = $"Ошибка выравнивания MultiCAD-выносок по точке: {ex.Message}" };
            }
        }

        public LeaderAlignmentResult AlignSelectedTeighaMLeadersToPoint(LeaderAlignmentAxis axis)
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
                return new LeaderAlignmentResult { Message = "Не выбраны мультивыноски для выравнивания по точке." };
            }

            if (!TryGetReferencePoint(axis, out var referencePoint))
            {
                return new LeaderAlignmentResult { SelectedCount = selection.Length, Message = "Указание точки отменено." };
            }

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var targets = new List<LeaderPointAlignmentTarget>();
                foreach (var id in selection)
                {
                    var obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (obj is MLeader mLeader && TryCreateMLeaderTarget(mLeader, out var target))
                    {
                        targets.Add(target);
                    }
                }

                if (targets.Count == 0)
                {
                    tr.Abort();
                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selection.Length,
                        CandidateCount = 0,
                        Message = "Среди выбранных объектов не найдено Teigha/nanoCAD мультивыносок с доступной точкой выравнивания."
                    };
                }

                ApplyReferencePoint(targets, axis, referencePoint);
                tr.Commit();
                editor.UpdateScreen();

                return new LeaderAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = targets.Count,
                    AlignedCount = targets.Count,
                    Message = BuildSuccessMessage("Teigha", axis, targets.Count)
                };
            }
        }

        private static bool TryGetReferencePoint(LeaderAlignmentAxis axis, out AlignmentPoint point)
        {
            point = default;
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return false;
            }

            NanoCadEditorFocusService.PrepareForEditorInput();
            var axisText = axis == LeaderAlignmentAxis.Horizontal ? "Y" : "X";
            var options = new PromptPointOptions($"\nУкажите точку, по координате {axisText} которой нужно выровнять выбранные выноски: ");
            var result = doc.Editor.GetPoint(options);
            if (result.Status != PromptStatus.OK)
            {
                return false;
            }

            point = new AlignmentPoint(result.Value.X, result.Value.Y, result.Value.Z);
            return true;
        }

        private static ObjectId[]? GetDbSelection(Editor editor)
        {
            NanoCadEditorFocusService.PrepareForEditorInput();
            var implied = editor.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                return implied.Value.GetObjectIds();
            }

            NanoCadEditorFocusService.PrepareForEditorInput();
            var options = new PromptSelectionOptions { MessageForAdding = "\nВыберите мультивыноски для выравнивания по точке: " };
            var picked = editor.GetSelection(options);
            return picked.Status == PromptStatus.OK && picked.Value != null && picked.Value.Count > 0
                ? picked.Value.GetObjectIds()
                : null;
        }

        private static void ApplyReferencePoint(IEnumerable<LeaderPointAlignmentTarget> targets, LeaderAlignmentAxis axis, AlignmentPoint referencePoint)
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

        private static string BuildSuccessMessage(string source, LeaderAlignmentAxis axis, int count)
        {
            var axisName = axis == LeaderAlignmentAxis.Horizontal ? "по горизонтали" : "по вертикали";
            return $"{source}-выноски: выравнивание {axisName} по указанной точке выполнено. Обработано объектов: {count}.";
        }

        private static bool TryCreateMLeaderTarget(MLeader mLeader, out LeaderPointAlignmentTarget target)
        {
            return TryCreatePointTarget(mLeader, "TextLocation", out target) ||
                   TryCreatePointTarget(mLeader, "BlockPosition", out target);
        }

        private static bool TryCreatePointTarget(object obj, out LeaderPointAlignmentTarget target)
        {
            foreach (var propertyName in AnchorPointProperties)
            {
                if (TryCreatePointTarget(obj, propertyName, out target))
                {
                    return true;
                }
            }

            target = default!;
            return false;
        }

        private static bool TryCreatePointTarget(object obj, string propertyName, out LeaderPointAlignmentTarget target)
        {
            var property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead || !property.CanWrite || !TryGetPoint(property.GetValue(obj), out var point))
            {
                target = default!;
                return false;
            }

            target = new LeaderPointAlignmentTarget(point, alignedPoint =>
            {
                property.SetValue(obj, CreatePointValue(property.PropertyType, alignedPoint));
                MarkObjectModified(obj);
            });
            return true;
        }

        private static bool IsLeaderCandidate(object obj)
        {
            var type = obj.GetType();
            var typeName = type.Name;
            var fullName = type.FullName ?? string.Empty;
            var rxName = obj is Entity entity ? entity.GetRXClass()?.Name ?? string.Empty : string.Empty;

            return typeName.Contains("McNote", StringComparison.OrdinalIgnoreCase) ||
                   fullName.Contains("McNote", StringComparison.OrdinalIgnoreCase) ||
                   typeName.Contains("Leader", StringComparison.OrdinalIgnoreCase) ||
                   fullName.Contains("Leader", StringComparison.OrdinalIgnoreCase) ||
                   rxName.Contains("mcsDbObjectNote", StringComparison.OrdinalIgnoreCase) ||
                   rxName.Contains("Leader", StringComparison.OrdinalIgnoreCase);
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

        private readonly struct LeaderPointAlignmentTarget
        {
            private readonly Action<AlignmentPoint> _apply;
            public AlignmentPoint Point { get; }
            public LeaderPointAlignmentTarget(AlignmentPoint point, Action<AlignmentPoint> apply) { Point = point; _apply = apply; }
            public void Apply(AlignmentPoint point) => _apply(point);
        }
    }
}
