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

        private static readonly string[] ShelfMirrorProperties =
        {
            "IsShelfFlipped", "IsFlipped", "ShelfDirection", "MirrorShelf",
            "FlipShelfDirection", "IsReversed", "ShelfFlipped", "Flipped"
        };

        private static readonly string[] ShelfMirrorMethods =
        {
            "FlipShelf", "MirrorShelf", "ToggleShelfDirection", "SetShelfDirection",
            "ReverseShelf", "FlipDirection", "SetFlipped"
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

        public LeaderAlignmentResult GroupAlignSelectedLeaders(DesignToolsLeaderSource source, LeaderAlignmentAxis axis)
        {
            return source == DesignToolsLeaderSource.MultiCad
                ? GroupAlignMultiCadLeaders(axis)
                : GroupAlignTeighaMLeaders(axis);
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

        private static LeaderAlignmentResult GroupAlignMultiCadLeaders(LeaderAlignmentAxis axis)
        {
            Type? objectManagerType;
            try { objectManagerType = ResolveLoadedType("Multicad.DatabaseServices.McObjectManager"); }
            catch (FileLoadException) { return new LeaderAlignmentResult { Message = "MultiCAD API недоступен в текущем сеансе nanoCAD." }; }
            catch (BadImageFormatException) { return new LeaderAlignmentResult { Message = "MultiCAD API недоступен в текущем сеансе nanoCAD." }; }

            if (objectManagerType == null)
                return new LeaderAlignmentResult { Message = "MultiCAD API недоступен в текущем сеансе nanoCAD." };

            var selectionIds = GetCurrentMultiCadSelection(objectManagerType);
            if (selectionIds.Count == 0)
                return new LeaderAlignmentResult { Message = "Для группового выравнивания выберите MultiCAD-выноски до запуска команды." };

            // Строим цели до запроса якоря — нужны для Enter-умолчания.
            var targets = new List<StepTarget>();
            foreach (var id in selectionIds)
            {
                var obj = GetMultiCadObject(objectManagerType, id);
                if (obj == null || !IsLeaderCandidate(obj)) continue;
                if (TryCreateLeaderPointTarget(obj, out var target))
                    targets.Add(target);
            }

            if (targets.Count < 2)
                return new LeaderAlignmentResult
                {
                    SelectedCount = selectionIds.Count,
                    CandidateCount = targets.Count,
                    Message = $"Для группового выравнивания нужно минимум 2 MultiCAD-выноски. Найдено: {targets.Count}."
                };

            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var editor = doc?.Editor;
            if (editor == null)
                return new LeaderAlignmentResult { Message = "Нет активного документа nanoCAD." };

            if (!TryGetAnchorPoint(editor, targets, axis, out var anchor))
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = "Указание опорной точки отменено." };

            if (!TryGetGroupAlignStep(editor, axis, out var step))
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = "Указание шага отменено." };

            if (!ShowGroupAlignPreview(editor, targets, axis, step, anchor))
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, CandidateCount = targets.Count, Message = "Групповое выравнивание отменено пользователем." };

            try
            {
                StartMultiCadTransaction(objectManagerType);
                try
                {
                    ApplyGroupAlign(targets, axis, step, anchor);
                    EndMultiCadTransaction(objectManagerType);
                    UpdateMultiCadGraphics(objectManagerType);

                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selectionIds.Count,
                        CandidateCount = targets.Count,
                        AlignedCount = targets.Count,
                        Message = $"MultiCAD-выноски: группа выровнена по оси с шагом {FormatStep(step)}. Обработано: {targets.Count}."
                    };
                }
                catch { AbortMultiCadTransaction(objectManagerType); throw; }
            }
            catch (Exception ex)
            {
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = $"Ошибка группового выравнивания: {ex.Message}" };
            }
        }

        private static LeaderAlignmentResult GroupAlignTeighaMLeaders(LeaderAlignmentAxis axis)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return new LeaderAlignmentResult { Message = "Нет активного документа nanoCAD." };

            var editor = doc.Editor;
            var selection = GetDbSelection(editor, "\nВыберите мультивыноски для группового выравнивания: ");
            if (selection == null || selection.Length == 0)
                return new LeaderAlignmentResult { Message = "Не выбраны мультивыноски для обработки." };

            // Строим цели в read-транзакции, затем закрываем.
            List<StepTarget> targets;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                targets = new List<StepTarget>();
                foreach (var id in selection)
                {
                    var obj = tr.GetObject(id, OpenMode.ForRead, false);
                    if (obj is not MLeader mLeader) continue;
                    if (TryCreatePropertyPointTarget(mLeader, "TextLocation", out var target, afterApply: null)
                        || TryCreatePropertyPointTarget(mLeader, "BlockPosition", out target, afterApply: null))
                        targets.Add(target);
                }
                tr.Abort();
            }

            if (targets.Count < 2)
                return new LeaderAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = targets.Count,
                    Message = $"Для группового выравнивания нужно минимум 2 мультивыноски. Найдено: {targets.Count}."
                };

            if (!TryGetAnchorPoint(editor, targets, axis, out var anchor))
                return new LeaderAlignmentResult { SelectedCount = selection.Length, CandidateCount = targets.Count, Message = "Указание опорной точки отменено." };

            if (!TryGetGroupAlignStep(editor, axis, out var step))
                return new LeaderAlignmentResult { SelectedCount = selection.Length, CandidateCount = targets.Count, Message = "Указание шага отменено." };

            if (!ShowGroupAlignPreview(editor, targets, axis, step, anchor))
                return new LeaderAlignmentResult { SelectedCount = selection.Length, CandidateCount = targets.Count, Message = "Групповое выравнивание отменено пользователем." };

            // Транзакция для записи
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var writableTargets = new List<StepTarget>();
                foreach (var id in selection)
                {
                    var obj = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (obj is not MLeader mLeader) continue;
                    if (TryCreatePropertyPointTarget(mLeader, "TextLocation", out var target, afterApply: MarkObjectModified)
                        || TryCreatePropertyPointTarget(mLeader, "BlockPosition", out target, afterApply: MarkObjectModified))
                        writableTargets.Add(target);
                }

                ApplyGroupAlign(writableTargets, axis, step, anchor);
                tr.Commit();
                editor.UpdateScreen();

                return new LeaderAlignmentResult
                {
                    SelectedCount = selection.Length,
                    CandidateCount = writableTargets.Count,
                    AlignedCount = writableTargets.Count,
                    Message = $"Мультивыноски: группа выровнена по оси с шагом {FormatStep(step)}. Обработано: {writableTargets.Count}."
                };
            }
        }

        /// <summary>
        /// Предпросмотр: векторы смещения + кресты-маркеры в целевых точках.
        /// Синий вектор от текущей позиции к целевой. Зелёный от якоря к цели. Красные кресты.
        /// </summary>
        private static bool ShowGroupAlignPreview(Editor editor, IReadOnlyList<StepTarget> targets, LeaderAlignmentAxis axis, double step, AlignmentPoint anchor)
        {
            var (ordered, targetPoints) = ComputeTargetPoints(targets, axis, step, anchor);
            var collisionCount = DetectCollisions(ordered, targetPoints);

            for (var i = 0; i < ordered.Length; i++)
            {
                var from = ordered[i].Point;
                var to = targetPoints[i];

                // Синий вектор: текущая → целевая позиция
                editor.DrawVector(new Point3d(from.X, from.Y, from.Z), new Point3d(to.X, to.Y, to.Z), 5, false);
                // Зелёный вектор: якорь → целевая позиция
                editor.DrawVector(new Point3d(anchor.X, anchor.Y, anchor.Z), new Point3d(to.X, to.Y, to.Z), 3, false);

                // Красный крест-маркер в целевой позиции
                var half = Math.Max(Math.Abs(step) * 0.3, 2.0);
                editor.DrawVector(new Point3d(to.X - half, to.Y, to.Z), new Point3d(to.X + half, to.Y, to.Z), 1, false);
                editor.DrawVector(new Point3d(to.X, to.Y - half, to.Z), new Point3d(to.X, to.Y + half, to.Z), 1, false);
            }

            // Предупреждение о коллизиях
            if (collisionCount > 0)
            {
                editor.WriteMessage($"\n⚠ Обнаружено наложений текста: {collisionCount}. Проверьте результат.");
            }

            // Запрос подтверждения
            NanoCadEditorFocusService.PrepareForEditorInput();
            var keyOpts = new PromptKeywordOptions($"\nПринять результат? [Да/Нет] <Да>: ")
            {
                AllowNone = true
            };
            keyOpts.Keywords.Add("Да", "Да", "Да");
            keyOpts.Keywords.Add("Нет", "Нет", "Нет");
            keyOpts.Keywords.Default = "Да";

            var result = editor.GetKeywords(keyOpts);
            return result.Status == PromptStatus.OK && string.Equals(result.StringResult, "Да", StringComparison.OrdinalIgnoreCase)
                   || result.Status == PromptStatus.None;
        }

        /// <summary>
        /// Вычисляет целевые позиции для группового выравнивания.
        /// </summary>
        private static (StepTarget[] ordered, List<AlignmentPoint> targetPoints) ComputeTargetPoints(
            IReadOnlyList<StepTarget> targets, LeaderAlignmentAxis axis, double step, AlignmentPoint anchor)
        {
            var ordered = axis == LeaderAlignmentAxis.Horizontal
                ? targets.OrderBy(t => Math.Abs(t.Point.X - anchor.X)).ToArray()
                : targets.OrderBy(t => Math.Abs(t.Point.Y - anchor.Y)).ToArray();

            var targetPoints = new List<AlignmentPoint>(ordered.Length);
            for (var i = 0; i < ordered.Length; i++)
            {
                var current = ordered[i].Point;
                var p = axis == LeaderAlignmentAxis.Horizontal
                    ? new AlignmentPoint(anchor.X + step * i, anchor.Y, current.Z)
                    : new AlignmentPoint(anchor.X, anchor.Y + step * i, current.Z);
                targetPoints.Add(p);
            }

            return (ordered, targetPoints);
        }

        /// <summary>
        /// Детекция коллизий: проверка перекрытия bounding box'ов текста.
        /// Возвращает количество найденных пересечений.
        /// </summary>
        private static int DetectCollisions(StepTarget[] ordered, List<AlignmentPoint> targetPoints)
        {
            const double defaultTextWidth = 40.0;
            const double defaultTextHeight = 8.0;
            var count = 0;

            for (var i = 0; i < targetPoints.Count; i++)
            {
                for (var j = i + 1; j < targetPoints.Count; j++)
                {
                    var a = targetPoints[i];
                    var b = targetPoints[j];
                    var spacing = Math.Abs(Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)));

                    // Вертикальная группа: перекрытие по Y при одинаковом X
                    if (Math.Abs(a.X - b.X) < 0.1 && spacing < defaultTextHeight)
                        count++;
                    // Горизонтальная группа: перекрытие по X при одинаковом Y
                    else if (Math.Abs(a.Y - b.Y) < 0.1 && spacing < defaultTextWidth * 0.6)
                        count++;
                    // Общий случай: перекрытие bounding box
                    else
                    {
                        var overlapX = Math.Max(0.0, defaultTextWidth - Math.Abs(a.X - b.X));
                        var overlapY = Math.Max(0.0, defaultTextHeight - Math.Abs(a.Y - b.Y));
                        if (overlapX > 0 && overlapY > 0) count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Групповое выравнивание: все цели получают общую координату по опорной оси (от якоря),
        /// и равномерно распределяются по второй оси с заданным шагом.
        /// </summary>
        private static void ApplyGroupAlign(IReadOnlyList<StepTarget> targets, LeaderAlignmentAxis axis, double step, AlignmentPoint? anchor = null)
        {
            var anchorPoint = anchor ?? targets[0].Point;
            var (ordered, _) = ComputeTargetPoints(targets, axis, step, anchorPoint);
            for (var index = 0; index < ordered.Length; index++)
            {
                var aligned = axis == LeaderAlignmentAxis.Horizontal
                    ? new AlignmentPoint(anchorPoint.X + step * index, anchorPoint.Y, ordered[index].Point.Z)
                    : new AlignmentPoint(anchorPoint.X, anchorPoint.Y + step * index, ordered[index].Point.Z);
                ordered[index].Apply(aligned);
            }
        }

        // ========= Интерактивное групповое выравнивание через McTransientGraphics =========

        public LeaderAlignmentResult RunGroupAlignJig(DesignToolsCommandState state)
        {
            var axis = state.Axis;
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var editor = doc?.Editor;
            if (editor == null)
                return new LeaderAlignmentResult { Message = "Нет активного документа nanoCAD." };

            Type? objectManagerType;
            try { objectManagerType = ResolveLoadedType("Multicad.DatabaseServices.McObjectManager"); }
            catch { return new LeaderAlignmentResult { Message = "MultiCAD API недоступен." }; }

            if (objectManagerType == null)
                return new LeaderAlignmentResult { Message = "MultiCAD API недоступен." };

            var selectionIds = state.MultiCadSelectionIds ?? new List<object>();
            if (selectionIds.Count == 0)
                return new LeaderAlignmentResult { Message = "Нет кэшированных ID выносок. Запустите команду из панели DesignTools." };

            var targets = new List<StepTarget>();
            foreach (var id in selectionIds)
            {
                var obj = GetMultiCadObject(objectManagerType, id);
                if (obj == null || !IsLeaderCandidate(obj)) continue;
                if (TryCreateLeaderPointTarget(obj, out var target))
                    targets.Add(target);
            }

            if (targets.Count < 2)
                return new LeaderAlignmentResult
                {
                    SelectedCount = selectionIds.Count,
                    CandidateCount = targets.Count,
                    Message = $"Для группового выравнивания нужно минимум 2 выноски. Найдено: {targets.Count}."
                };

            // Фаза 1: опорная точка
            var anchorOpts = new PromptPointOptions("\nУкажите опорную точку или выноску [Enter - первая]: ")
            {
                AllowNone = true
            };
            var anchorResult = editor.GetPoint(anchorOpts);
            AlignmentPoint anchor;
            if (anchorResult.Status == PromptStatus.None || anchorResult.Status == PromptStatus.Cancel)
                anchor = targets[0].Point;
            else if (anchorResult.Status == PromptStatus.OK)
                anchor = new AlignmentPoint(anchorResult.Value.X, anchorResult.Value.Y, anchorResult.Value.Z);
            else
                return new LeaderAlignmentResult { Message = "Групповое выравнивание отменено." };

            // Инициализируем McTransientGraphics
            var transientGfx = CreateMcTransientGraphics();
            if (transientGfx == null)
                return new LeaderAlignmentResult { Message = "MultiCAD Graphics API недоступен." };

            object? previewHandle = null;
            try
            {
                // Фаза 2: шаг с предпросмотром
                var distOpts = new PromptDistanceOptions("\nУкажите шаг [число / Enter - применить]: ")
                {
                    BasePoint = new Point3d(anchor.X, anchor.Y, anchor.Z),
                    UseBasePoint = true,
                    AllowNone = true
                };
                var distResult = editor.GetDistance(distOpts);
                if (distResult.Status != PromptStatus.OK && distResult.Status != PromptStatus.None)
                {
                    HideMcTransientGraphics(transientGfx);
                    return new LeaderAlignmentResult { Message = "Групповое выравнивание отменено." };
                }

                var step = distResult.Status == PromptStatus.OK ? distResult.Value : 0.0;
                if (Math.Abs(step) < 1e-9)
                {
                    HideMcTransientGraphics(transientGfx);
                    return new LeaderAlignmentResult { Message = "Групповое выравнивание отменено (шаг не задан)." };
                }

                // Вычисляем целевые позиции и показываем предпросмотр
                var ordered = axis == LeaderAlignmentAxis.Horizontal
                    ? targets.OrderBy(t => Math.Abs(t.Point.X - anchor.X)).ToArray()
                    : targets.OrderBy(t => Math.Abs(t.Point.Y - anchor.Y)).ToArray();

                var targetPoints = new AlignmentPoint[ordered.Length];
                for (var i = 0; i < ordered.Length; i++)
                {
                    targetPoints[i] = axis == LeaderAlignmentAxis.Horizontal
                        ? new AlignmentPoint(anchor.X + step * i, anchor.Y, ordered[i].Point.Z)
                        : new AlignmentPoint(anchor.X, anchor.Y + step * i, ordered[i].Point.Z);
                }

                try
                {
                    previewHandle = ShowMcTransientPreview(transientGfx, targets.Select(t => t.Point).ToArray(), targetPoints, anchor);
                    editor.WriteMessage($"\n[DesignTools]: Предпросмотр построен ({((System.Collections.IList)previewHandle)?.Count ?? 0} объектов).\n");
                }
                catch (Exception ex)
                {
                    editor.WriteMessage($"\n[DesignTools]: Ошибка построения предпросмотра: {ex.Message}\n");
                    HideMcTransientGraphics(transientGfx);
                    return new LeaderAlignmentResult { Message = $"Ошибка предпросмотра: {ex.Message}" };
                }

                // Фаза 3: подтверждение
                var confirmOpts = new PromptKeywordOptions("\nПрименить групповое выравнивание? [Да/Нет] <Д>: ")
                {
                    AllowNone = true
                };
                confirmOpts.Keywords.Add("Да", "Да", "Да");
                confirmOpts.Keywords.Add("Нет", "Нет", "Нет");
                confirmOpts.Keywords.Default = "Да";
                var confirmResult = editor.GetKeywords(confirmOpts);

                if (confirmResult.Status == PromptStatus.Cancel ||
                    (confirmResult.Status == PromptStatus.Keyword && confirmResult.StringResult == "Нет"))
                {
                    HideMcTransientGraphics(transientGfx);
                    return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, CandidateCount = targets.Count, Message = "Групповое выравнивание отменено." };
                }

                // Применяем
                HideMcTransientGraphics(transientGfx);
                try
                {
                    StartMultiCadTransaction(objectManagerType);
                    try
                    {
                        for (var i = 0; i < ordered.Length; i++)
                            ordered[i].Apply(targetPoints[i]);
                        EndMultiCadTransaction(objectManagerType);
                        UpdateMultiCadGraphics(objectManagerType);

                        return new LeaderAlignmentResult
                        {
                            SelectedCount = selectionIds.Count,
                            CandidateCount = targets.Count,
                            AlignedCount = targets.Count,
                            Message = $"MultiCAD-выноски: группа выровнена по оси с шагом {FormatStep(step)}. Обработано: {targets.Count}."
                        };
                    }
                    catch { AbortMultiCadTransaction(objectManagerType); throw; }
                }
                catch (Exception ex)
                {
                    return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = $"Ошибка группового выравнивания: {ex.Message}" };
                }
            }
            catch
            {
                if (previewHandle != null)
                    HideMcTransientGraphics(transientGfx);
                throw;
            }
        }

        private static object? CreateMcTransientGraphics()
        {
            try
            {
                var type = ResolveLoadedType("Multicad.Graphics.McTransientGraphics");
                return type != null ? Activator.CreateInstance(type) : null;
            }
            catch { return null; }
        }

        private static void HideMcTransientGraphics(object transientGfx)
        {
            try { transientGfx.GetType().GetMethod("HideAll")?.Invoke(transientGfx, null); }
            catch { /* best effort */ }
        }

        private static object? ShowMcTransientPreview(object transientGfx, AlignmentPoint[] currentPoints, AlignmentPoint[] targetPoints, AlignmentPoint anchor)
        {
            var point3dType = ResolveLoadedType("Multicad.Geometry.Point3d")
                ?? throw new InvalidOperationException("Multicad.Geometry.Point3d не найден");
            var lineSeg3dType = ResolveLoadedType("Multicad.Geometry.LineSeg3d")
                ?? throw new InvalidOperationException("Multicad.Geometry.LineSeg3d не найден");
            var entityGeometryType = ResolveLoadedType("Multicad.Geometry.EntityGeometry")
                ?? throw new InvalidOperationException("Multicad.Geometry.EntityGeometry не найден");

            // Ищем Show-метод и берём ТОЧНЫЙ тип параметра из его сигнатуры,
            // чтобы избежать .NET type identity mismatch (та же ошибка: List<T> из разных context).
            MethodInfo? showMethod = null;
            Type? listType = null;
            Type? elemType = null;

            foreach (var m in transientGfx.GetType().GetMethods())
            {
                if (m.Name != "Show" || m.GetParameters().Length != 1) continue;
                var pt = m.GetParameters()[0].ParameterType;
                if (pt.IsByRef) pt = pt.GetElementType()!;
                // Нас интересует overload, принимающий List<EntityGeometry>
                if (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(List<>))
                {
                    showMethod = m;
                    listType = pt;
                    elemType = pt.GetGenericArguments()[0];
                    break;
                }
            }

            if (showMethod == null || listType == null || elemType == null)
                throw new InvalidOperationException("McTransientGraphics.Show(List<EntityGeometry>) не найден");

            // Если EntityGeometry из ResolveLoadedType не совпадает с типом из Show-сигнатуры,
            // используем тип из сигнатуры для создания геометрии.
            var geomType = elemType;

            var geomList = Activator.CreateInstance(listType)!;
            var addMethod = listType.GetMethod("Add")
                ?? throw new InvalidOperationException("List.Add не найден");

            Func<double, double, double, object> createPoint = (x, y, z) =>
                Activator.CreateInstance(point3dType, x, y, z)!;

            for (var i = 0; i < targetPoints.Length && i < currentPoints.Length; i++)
            {
                var from = currentPoints[i];
                var to = targetPoints[i];

                // Синяя линия: текущая → целевая
                var blueLine = Activator.CreateInstance(lineSeg3dType,
                    createPoint(from.X, from.Y, from.Z),
                    createPoint(to.X, to.Y, to.Z));
                var blueGeom = Activator.CreateInstance(geomType, blueLine)!;
                TrySetColor(geomType, blueGeom, System.Drawing.Color.Blue);
                addMethod.Invoke(geomList, new[] { blueGeom });

                // Зелёная линия: якорь → целевая
                var greenLine = Activator.CreateInstance(lineSeg3dType,
                    createPoint(anchor.X, anchor.Y, anchor.Z),
                    createPoint(to.X, to.Y, to.Z));
                var greenGeom = Activator.CreateInstance(geomType, greenLine)!;
                TrySetColor(geomType, greenGeom, System.Drawing.Color.Green);
                addMethod.Invoke(geomList, new[] { greenGeom });

                // Красный крест-маркер в целевой точке
                var half = Math.Max(Math.Abs(targetPoints[0].X - anchor.X) * 0.3, 2.0);
                var hLine = Activator.CreateInstance(lineSeg3dType,
                    createPoint(to.X - half, to.Y, to.Z),
                    createPoint(to.X + half, to.Y, to.Z));
                var vLine = Activator.CreateInstance(lineSeg3dType,
                    createPoint(to.X, to.Y - half, to.Z),
                    createPoint(to.X, to.Y + half, to.Z));
                var hGeom = Activator.CreateInstance(geomType, hLine)!;
                var vGeom = Activator.CreateInstance(geomType, vLine)!;
                TrySetColor(geomType, hGeom, System.Drawing.Color.Red);
                TrySetColor(geomType, vGeom, System.Drawing.Color.Red);
                addMethod.Invoke(geomList, new[] { hGeom });
                addMethod.Invoke(geomList, new[] { vGeom });
            }

            showMethod.Invoke(transientGfx, new[] { geomList });
            return geomList;
        }

        private static void TrySetColor(Type entityGeometryType, object geomEnt, System.Drawing.Color color)
        {
            try
            {
                entityGeometryType.GetProperty("Color")?.SetValue(geomEnt, color);
            }
            catch
            {
                // MultiCAD Color может быть не System.Drawing.Color — пробуем по имени
                try
                {
                    var colorProp = entityGeometryType.GetProperty("Color");
                    if (colorProp != null)
                    {
                        var mcColorType = colorProp.PropertyType;
                        // Пробуем FromArgb
                        var fromArgb = mcColorType.GetMethod("FromArgb", new[] { typeof(int) });
                        if (fromArgb != null)
                        {
                            var mcColor = fromArgb.Invoke(null, new object[] { color.ToArgb() });
                            colorProp.SetValue(geomEnt, mcColor);
                        }
                    }
                }
                catch { /* оставляем цвет по умолчанию */ }
            }
        }

        // ========= SmartGroup (умная группа) =========

        public LeaderAlignmentResult SmartGroupAlignSelectedLeaders(DesignToolsLeaderSource source, LeaderAlignmentAxis axis)
        {
            return source == DesignToolsLeaderSource.MultiCad
                ? SmartGroupAlignMultiCadLeaders(axis)
                : SmartGroupAlignTeighaMLeaders(axis);
        }

        /// <summary>
        /// Умная группа для MultiCAD: групповое выравнивание + авто-зеркало полок + проверка коллизий.
        /// </summary>
        private LeaderAlignmentResult SmartGroupAlignMultiCadLeaders(LeaderAlignmentAxis axis)
        {
            Type? objectManagerType;
            try { objectManagerType = ResolveLoadedType("Multicad.DatabaseServices.McObjectManager"); }
            catch (FileLoadException) { return new LeaderAlignmentResult { Message = "MultiCAD API недоступен." }; }
            catch (BadImageFormatException) { return new LeaderAlignmentResult { Message = "MultiCAD API недоступен." }; }

            if (objectManagerType == null)
                return new LeaderAlignmentResult { Message = "MultiCAD API недоступен." };

            var selectionIds = GetCurrentMultiCadSelection(objectManagerType);
            if (selectionIds.Count == 0)
                return new LeaderAlignmentResult { Message = "Выберите MultiCAD-выноски до запуска команды." };

            var targets = new List<StepTarget>();
            var leaderObjects = new List<object>();
            foreach (var id in selectionIds)
            {
                var obj = GetMultiCadObject(objectManagerType, id);
                if (obj == null || !IsLeaderCandidate(obj)) continue;
                if (TryCreateLeaderPointTarget(obj, out var target))
                {
                    targets.Add(target);
                    leaderObjects.Add(obj);
                }
            }

            if (targets.Count < 2)
                return new LeaderAlignmentResult
                {
                    SelectedCount = selectionIds.Count,
                    CandidateCount = targets.Count,
                    Message = $"Для умной группы нужно минимум 2 MultiCAD-выноски. Найдено: {targets.Count}."
                };

            var doc = CadApp.DocumentManager.MdiActiveDocument;
            var editor = doc?.Editor;
            if (editor == null) return new LeaderAlignmentResult { Message = "Нет активного документа." };

            if (!TryGetAnchorPoint(editor, targets, axis, out var anchor))
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = "Указание опорной точки отменено." };

            if (!TryGetGroupAlignStep(editor, axis, out var step))
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = "Указание шага отменено." };

            // Вычисляем целевые позиции
            var (ordered, targetPoints) = ComputeTargetPoints(targets, axis, step, anchor);

            // Проверка коллизий
            var collisionCount = DetectCollisions(ordered, targetPoints);

            // Корректируем полки, которые «сломались»
            var fixedShelves = 0;
            for (var i = 0; i < ordered.Length; i++)
            {
                if (leaderObjects.Count > i)
                {
                    if (FixShelfOrientation(leaderObjects[i], targetPoints[i], axis))
                        fixedShelves++;
                }
            }

            // Предпросмотр с маркерами коллизий
            if (!ShowGroupAlignPreview(editor, targets, axis, step, anchor))
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, CandidateCount = targets.Count, Message = "Умная группа отменена пользователем." };

            try
            {
                StartMultiCadTransaction(objectManagerType);
                try
                {
                    ApplyGroupAlign(targets, axis, step, anchor);
                    EndMultiCadTransaction(objectManagerType);
                    UpdateMultiCadGraphics(objectManagerType);

                    var extra = string.Empty;
                    if (fixedShelves > 0) extra += $" | Зеркалировано полок: {fixedShelves}";
                    if (collisionCount > 0) extra += $" | ⚠ Наложений текста: {collisionCount}";

                    return new LeaderAlignmentResult
                    {
                        SelectedCount = selectionIds.Count,
                        CandidateCount = targets.Count,
                        AlignedCount = targets.Count,
                        Message = $"Умная группа: MultiCAD-выноски выровнены с шагом {FormatStep(step)}. Обработано: {targets.Count}.{extra}"
                    };
                }
                catch { AbortMultiCadTransaction(objectManagerType); throw; }
            }
            catch (Exception ex)
            {
                return new LeaderAlignmentResult { SelectedCount = selectionIds.Count, Message = $"Ошибка умной группы: {ex.Message}" };
            }
        }

        private static LeaderAlignmentResult SmartGroupAlignTeighaMLeaders(LeaderAlignmentAxis axis)
        {
            return new LeaderAlignmentResult { Message = "Умная группа для Teigha MLeader будет реализована позже." };
        }

        /// <summary>
        /// Проверяет ориентацию полки выноски: если после смещения полка оказывается
        /// с обратной стороны от начала выноски (Origin) — зеркалит её.
        /// </summary>
        private static bool FixShelfOrientation(object leaderObj, AlignmentPoint newTextPoint, LeaderAlignmentAxis axis)
        {
            try
            {
                var type = leaderObj.GetType();

                // Получаем Origin (начало выноски)
                var originProp = type.GetProperty("Origin", BindingFlags.Instance | BindingFlags.Public);
                if (originProp == null) return false;

                if (!TryGetPoint(originProp.GetValue(leaderObj), out var origin))
                    return false;

                // Текущая позиция текста
                if (!TryGetPoint(GetCurrentTextPoint(leaderObj), out var currentText))
                    return false;

                // Направление полки ДО смещения
                var shelfWasRight = axis == LeaderAlignmentAxis.Vertical
                    ? currentText.X > origin.X   // текст справа от начала
                    : currentText.Y > origin.Y;

                // Направление полки ПОСЛЕ смещения
                var shelfWillBeRight = axis == LeaderAlignmentAxis.Vertical
                    ? newTextPoint.X > origin.X
                    : newTextPoint.Y > origin.Y;

                // Если направление изменилось — полка «переломилась», зеркалим
                if (shelfWasRight != shelfWillBeRight)
                {
                    return MirrorLeaderShelf(leaderObj);
                }
            }
            catch { /* best-effort */ }
            return false;
        }

        private static object? GetCurrentTextPoint(object leaderObj)
        {
            var type = leaderObj.GetType();
            foreach (var propName in LeaderAnchorPointProperties)
            {
                var prop = type.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.CanRead && TryGetPoint(prop.GetValue(leaderObj), out _))
                    return prop.GetValue(leaderObj);
            }
            return null;
        }

        private static bool MirrorLeaderShelf(object leaderObj)
        {
            var type = leaderObj.GetType();

            // Пробуем свойство-флаг
            foreach (var propName in ShelfMirrorProperties)
            {
                var prop = type.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if (prop == null) continue;

                if (prop.CanRead && prop.CanWrite && prop.PropertyType == typeof(bool))
                {
                    var current = (bool?)prop.GetValue(leaderObj);
                    prop.SetValue(leaderObj, !(current ?? false));
                    return true;
                }

                // Enum-свойство (ShelfDirection = Left/Right)
                if (prop.CanRead && prop.CanWrite && prop.PropertyType.IsEnum)
                {
                    var current = prop.GetValue(leaderObj);
                    var values = Enum.GetValues(prop.PropertyType);
                    if (values.Length == 2)
                    {
                        var next = values.GetValue(0)?.Equals(current) == true ? values.GetValue(1) : values.GetValue(0);
                        prop.SetValue(leaderObj, next);
                        return true;
                    }
                }

                // Int-свойство (-1/0/1)
                if (prop.CanRead && prop.CanWrite && prop.PropertyType == typeof(int))
                {
                    var current = (int?)prop.GetValue(leaderObj) ?? 0;
                    prop.SetValue(leaderObj, -current);
                    return true;
                }
            }

            // Пробуем метод-переключатель
            foreach (var methodName in ShelfMirrorMethods)
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
                if (method != null)
                {
                    method.Invoke(leaderObj, Array.Empty<object>());
                    return true;
                }

                // Метод с bool-параметром: SetShelfDirection(true/false), SetFlipped(true/false)
                var boolMethod = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, new[] { typeof(bool) });
                if (boolMethod != null)
                {
                    boolMethod.Invoke(leaderObj, new object[] { true });
                    return true;
                }
            }

            return false;
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

        private static bool TryGetAnchorPoint(Editor editor, IReadOnlyList<StepTarget> targets, LeaderAlignmentAxis axis, out AlignmentPoint anchor)
        {
            anchor = default;
            var firstTarget = targets.Count > 0 ? targets[0].Point : default;

            NanoCadEditorFocusService.PrepareForEditorInput();
            var options = new PromptPointOptions("\nУкажите опорную точку или выноску [Enter — первая]: ")
            {
                AllowNone = true
            };
            var result = editor.GetPoint(options);

            if (result.Status == PromptStatus.None || result.Status == PromptStatus.Cancel)
            {
                // Enter/Cancel — якорь от первого объекта
                anchor = firstTarget;
                return true;
            }

            if (result.Status == PromptStatus.OK)
            {
                anchor = new AlignmentPoint(result.Value.X, result.Value.Y, result.Value.Z);
                return true;
            }

            anchor = firstTarget;
            return true;
        }

        /// <summary>
        /// Запрашивает шаг: две точки мышью или числовой ввод с клавиатуры.
        /// Отрицательный шаг допускается (обратное направление).
        /// </summary>
        private static bool TryGetGroupAlignStep(Editor editor, LeaderAlignmentAxis axis, out double step)
        {
            step = 0.0;

            // Сначала пробуем GetDistance — обрабатывает и число, и две точки.
            NanoCadEditorFocusService.PrepareForEditorInput();
            var distOpts = new PromptDistanceOptions("\nУкажите шаг [число или две точки]: ")
            {
                AllowNone = true
            };
            var distanceResult = editor.GetDistance(distOpts);

            if (distanceResult.Status == PromptStatus.OK)
            {
                // GetDistance всегда положительный — знак определим ниже по направлению
                step = distanceResult.Value;
                return Math.Abs(step) > 1e-9;
            }

            // Пользователь нажал Enter без ввода — явный запрос числа
            if (distanceResult.Status == PromptStatus.None || distanceResult.Status == PromptStatus.Cancel)
            {
                NanoCadEditorFocusService.PrepareForEditorInput();
                var doubleOpts = new PromptDoubleOptions("\nВведите числовое значение шага: ")
                {
                    AllowNone = true
                };
                var doubleResult = editor.GetDouble(doubleOpts);

                if (doubleResult.Status == PromptStatus.OK)
                {
                    step = doubleResult.Value;
                    return Math.Abs(step) > 1e-9;
                }

                // Повторное Enter — две точки
                return TryGetStep(editor, axis, out step);
            }

            return false;
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
