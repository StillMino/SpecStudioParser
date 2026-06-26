using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using CadApp = HostMgd.ApplicationServices.Application;
using SpecStudioParser.PositionNumbering.Models;

namespace SpecStudioParser.PositionNumbering.Services
{
    /// <summary>
    /// Движок автонумерации позиций на чертеже.
    /// </summary>
    public sealed class PositionNumberingService
    {
        /// <summary>
        /// Сканирует чертёж и возвращает все найденные позиции.
        /// </summary>
        public List<PositionInfo> ScanPositions(NumberingProfile profile)
        {
            var positions = new List<PositionInfo>();
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return positions;

            Database db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BlockTableRecord.ModelSpace))
            {
                tr.Commit();
                return positions;
            }

            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                string dxfName;
                try { dxfName = id.ObjectClass.DxfName; } catch { continue; }

                // 1. MLeader (выноски)
                if (dxfName == "MULTILEADER")
                {
                    try
                    {
                        var ml = (MLeader)tr.GetObject(id, OpenMode.ForRead);
                        if (!string.IsNullOrEmpty(profile.LeaderLayerFilter) &&
                            !ml.Layer.Equals(profile.LeaderLayerFilter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var info = ExtractFromMLeader(ml);
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
                pos.NewNumber = FormatNumber(counter, profile);
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

            Database db = doc.Database;
            using var docLock = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();

            foreach (var pos in sorted)
            {
                try
                {
                    ObjectId objId = ResolveHandle(pos.Handle, db);
                    if (objId == ObjectId.Null || objId.IsErased) continue;

                    var dbObj = tr.GetObject(objId, OpenMode.ForWrite);
                    if (dbObj == null) continue;

                    if (pos.IsBlockAttribute && dbObj is BlockReference br)
                    {
                        bool written = false;
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
                                written = true;
                                break;
                            }
                        }
                        if (!written) result.Skipped++;
                    }
                    else if (pos.IsLeader && dbObj is MLeader ml)
                    {
                        // Перезапись текста MLeader через MText
                        if (ml.MText != null)
                        {
                            if (profile.OverwriteExisting || string.IsNullOrEmpty(GetMLeaderText(ml)))
                            {
                                ml.MText.Contents = pos.NewNumber;
                                result.Updated++;
                            }
                            else
                            {
                                result.Skipped++;
                            }
                        }
                        else
                        {
                            // MLeader без MText — пробуем через ContentType
                            try
                            {
                                ml.MText = new MText { Contents = pos.NewNumber };
                                result.Updated++;
                            }
                            catch { result.Skipped++; }
                        }
                    }
                    else
                    {
                        result.Skipped++;
                    }
                }
                catch (Exception ex)
                {
                    doc.Editor.WriteMessage($"\n[PositionNumbering]: Ошибка записи {pos.Handle}: {ex.Message}");
                    result.Skipped++;
                }
            }

            tr.Commit();
            result.Message = $"Обработано: {result.TotalProcessed}, обновлено: {result.Updated}, пропущено: {result.Skipped}.";
            return result;
        }

        // ─── Извлечение позиций ──────────────────────────────────────────

        private PositionInfo? ExtractFromMLeader(MLeader ml)
        {
            try
            {
                var ext = ml.Bounds;
                Point3d pt = ext.HasValue ? ext.Value.MinPoint : Point3d.Origin;

                string text = GetMLeaderText(ml);

                return new PositionInfo
                {
                    Handle = ml.Handle.ToString(),
                    CurrentNumber = ExtractNumberFromText(text),
                    X = pt.X,
                    Y = pt.Y,
                    Layer = ml.Layer,
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
                    positions.ToList(),

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
            if (string.IsNullOrEmpty(text)) return "";
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
            return match.Success ? match.Value : text.Trim();
        }

        private static string GetMLeaderText(MLeader ml)
        {
            try
            {
                if (ml.MText != null)
                    return ml.MText.Contents ?? "";
            }
            catch { }
            return "";
        }

        private static ObjectId ResolveHandle(string handleStr, Database db)
        {
            try
            {
                // Handle может быть в hex или decimal
                // Пробуем hex (стандартный формат Handle в DWG)
                long handleVal = Convert.ToInt64(handleStr, 16);
                Handle h = new Handle(handleVal);
                if (db.TryGetObjectId(h, out ObjectId id))
                    return id;

                // Пробуем decimal
                handleVal = long.Parse(handleStr);
                h = new Handle(handleVal);
                if (db.TryGetObjectId(h, out ObjectId id2))
                    return id2;
            }
            catch { }
            return ObjectId.Null;
        }
    }
}
