using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using HostMgd.ApplicationServices;
using CadApp = HostMgd.ApplicationServices.Application;
using SpecStudioParser.PositionNumbering.Models;

namespace SpecStudioParser.PositionNumbering.Services
{
    /// <summary>
    /// Движок автонумерации позиций на чертеже.
    /// Находит выноски (MLeader) и блоки с атрибутами, определяет их пространственное положение,
    /// присваивает номера по заданной стратегии сортировки.
    /// </summary>
    public sealed class PositionNumberingService
    {
        /// <summary>
        /// Сканирует чертёж и возвращает все найденные позиции (выноски и блоки с атрибутом POS).
        /// </summary>
        public List<PositionInfo> ScanPositions(NumberingProfile profile)
        {
            var positions = new List<PositionInfo>();
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return positions;

            Database db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BlockTableRecord.ModelSpace)) return positions;

            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                string dxfName = id.ObjectClass.DxfName;

                // 1. MLeader (выноски)
                if (dxfName == "MULTILEADER" || dxfName == "MLINE")
                {
                    try
                    {
                        var ml = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (!string.IsNullOrEmpty(profile.LeaderLayerFilter) &&
                            !ml.Layer.Equals(profile.LeaderLayerFilter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var info = ExtractFromMLeader(id, tr);
                        if (info != null) positions.Add(info);
                    }
                    catch { }
                    continue;
                }

                // 2. BlockReference с атрибутом-позицией
                if (dxfName == "INSERT")
                {
                    try
                    {
                        var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                        var blockDef = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        if (blockDef.Name.StartsWith("*")) continue;

                        var info = ExtractFromBlock(br, tr, profile.TargetAttributeTag);
                        if (info != null) positions.Add(info);
                    }
                    catch { }
                }
            }

            tr.Commit();
            return positions;
        }

        /// <summary>
        /// Применяет нумерацию к позициям на чертеже.
        /// </summary>
        public NumberingResult ApplyNumbering(List<PositionInfo> positions, NumberingProfile profile)
        {
            var result = new NumberingResult();

            // Сортировка
            var sorted = SortPositions(positions, profile.SortMode);

            // Нумерация
            int counter = profile.StartNumber;
            foreach (var pos in sorted)
            {
                string num = FormatNumber(counter, profile);
                pos.NewNumber = num;
                counter += profile.Step;
            }

            result.Positions = sorted;
            result.TotalProcessed = sorted.Count;

            // Запись на чертёж
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                result.Message = "Нет активного документа.";
                return result;
            }

            using var docLock = doc.LockDocument();
            Database db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            foreach (var pos in sorted)
            {
                try
                {
                    var objId = pos.HandleToObjectId(db);
                    if (objId == ObjectId.Null) continue;

                    var dbObj = tr.GetObject(objId, OpenMode.ForWrite);

                    if (pos.IsBlockAttribute && dbObj is BlockReference br)
                    {
                        // Запись в атрибут блока
                        foreach (ObjectId arId in br.AttributeCollection)
                        {
                            var ar = (AttributeReference)tr.GetObject(arId, OpenMode.ForWrite);
                            if (ar.Tag.Equals(profile.TargetAttributeTag ?? "POS", StringComparison.OrdinalIgnoreCase))
                            {
                                if (profile.OverwriteExisting || string.IsNullOrEmpty(ar.TextString))
                                {
                                    ar.TextString = pos.NewNumber;
                                    result.Updated++;
                                }
                                else
                                {
                                    result.Skipped++;
                                }
                                break;
                            }
                        }
                    }
                    else if (pos.IsLeader && dbObj is Entity leaderEntity)
                    {
                        // Запись в текст выноски
                        UpdateLeaderText(leaderEntity, pos.NewNumber, profile);
                        result.Updated++;
                    }
                }
                catch (Exception ex)
                {
                    doc.Editor.WriteMessage($"\n[PositionNumbering]: Ошибка записи позиции {pos.Handle}: {ex.Message}");
                    result.Skipped++;
                }
            }

            tr.Commit();
            result.Message = $"Обработано: {result.TotalProcessed}, обновлено: {result.Updated}, пропущено: {result.Skipped}.";
            return result;
        }

        // ─── Извлечение позиций ──────────────────────────────────────────

        private PositionInfo? ExtractFromMLeader(ObjectId id, Transaction tr)
        {
            try
            {
                var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                var ext = entity.Bounds;
                Point3d pt = ext.HasValue ? ext.Value.MinPoint : Point3d.Origin;

                // Текст выноски
                string text = "";
                if (entity is MLeader ml)
                {
                    text = ml.MText?.Text ?? "";
                }

                return new PositionInfo
                {
                    Handle = entity.Handle.ToString(),
                    CurrentNumber = ExtractNumberFromText(text),
                    X = pt.X,
                    Y = pt.Y,
                    Layer = entity.Layer,
                    IsLeader = true,
                    BlockName = "—"
                };
            }
            catch { return null; }
        }

        private PositionInfo? ExtractFromBlock(BlockReference br, Transaction tr, string? targetTag)
        {
            try
            {
                string currentNum = "";
                bool hasTargetAttr = false;

                foreach (ObjectId arId in br.AttributeCollection)
                {
                    var ar = (AttributeReference)tr.GetObject(arId, OpenMode.ForRead);
                    if (targetTag != null && ar.Tag.Equals(targetTag, StringComparison.OrdinalIgnoreCase))
                    {
                        currentNum = ar.TextString;
                        hasTargetAttr = true;
                        break;
                    }
                }

                // Если нет целевого атрибута — пропускаем
                if (!hasTargetAttr) return null;

                var blockDef = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

                return new PositionInfo
                {
                    Handle = br.Handle.ToString(),
                    CurrentNumber = currentNum,
                    X = br.Position.X,
                    Y = br.Position.Y,
                    Layer = br.Layer,
                    BlockName = blockDef.Name,
                    IsBlockAttribute = true
                };
            }
            catch { return null; }
        }

        // ─── Сортировка ───────────────────────────────────────────────────

        private static List<PositionInfo> SortPositions(List<PositionInfo> positions, SortMode mode)
        {
            return mode switch
            {
                SortMode.TopToBottom_LeftToRight =>
                    positions.OrderByDescending(p => p.Y)
                             .ThenBy(p => p.X)
                             .ToList(),

                SortMode.LeftToRight_TopToBottom =>
                    positions.OrderBy(p => p.X)
                             .ThenByDescending(p => p.Y)
                             .ToList(),

                SortMode.ByLayer =>
                    positions.OrderBy(p => p.Layer)
                             .ThenByDescending(p => p.Y)
                             .ThenBy(p => p.X)
                             .ToList(),

                SortMode.SelectionOrder =>
                    positions.ToList(), // как есть

                _ => positions.OrderByDescending(p => p.Y).ThenBy(p => p.X).ToList()
            };
        }

        // ─── Вспомогательные методы ───────────────────────────────────────

        private static string FormatNumber(int num, NumberingProfile profile)
        {
            string prefix = string.IsNullOrEmpty(profile.Prefix) ? "" : profile.Prefix + " ";
            string body = string.IsNullOrEmpty(profile.NumberFormat)
                ? num.ToString()
                : num.ToString(profile.NumberFormat);
            return prefix + body;
        }

        private static string ExtractNumberFromText(string text)
        {
            // Пытаемся извлечь число из текста выноски
            if (string.IsNullOrEmpty(text)) return "";
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
            return match.Success ? match.Value : text.Trim();
        }

        private static void UpdateLeaderText(Entity entity, string newNumber, NumberingProfile profile)
        {
            if (entity is MLeader ml && ml.MText != null)
            {
                ml.MText.Contents = newNumber;
            }
        }

        private static string GetMLeaderContentType(MLeader ml)
        {
            return ml.ContentType.ToString();
        }
    }

    /// <summary>
    /// Методы расширения для PositionInfo.
    /// </summary>
    public static class PositionInfoExtensions
    {
        public static ObjectId HandleToObjectId(this PositionInfo pos, Database db)
        {
            try
            {
                if (long.TryParse(pos.Handle, out long handleValue))
                {
                    Handle h = new Handle(handleValue);
                    if (db.TryGetObjectId(h, out ObjectId id))
                        return id;
                }
            }
            catch { }
            return ObjectId.Null;
        }
    }
}
