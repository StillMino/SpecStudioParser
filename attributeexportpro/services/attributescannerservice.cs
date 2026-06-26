using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using HostMgd.ApplicationServices;
using CadApp = HostMgd.ApplicationServices.Application;
using SpecStudioParser.AttributeExportPro.Models;

namespace SpecStudioParser.AttributeExportPro.Services
{
    /// <summary>
    /// Сервис сканирования чертежа: находит вхождения блоков, извлекает атрибуты и геометрию.
    /// </summary>
    public sealed class AttributeScannerService
    {
        /// <summary>
        /// Сканирует весь ModelSpace и возвращает данные для выгрузки.
        /// </summary>
        public ExportData ScanBlocks(string? blockNameFilter = null)
        {
            var data = new ExportData();
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return data;

            Database db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BlockTableRecord.ModelSpace)) return data;

            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference)))) continue;

                BlockReference br;
                try { br = (BlockReference)tr.GetObject(id, OpenMode.ForRead); }
                catch { continue; }

                BlockTableRecord blockDef;
                try { blockDef = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead); }
                catch { continue; }

                string bName = blockDef.Name;
                if (bName.StartsWith("*")) continue;

                if (!string.IsNullOrWhiteSpace(blockNameFilter) &&
                    !bName.Equals(blockNameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var row = new ExportRow
                {
                    BlockName = bName,
                    Layer = br.Layer,
                    Handle = br.Handle.ToString()
                };

                // Системные свойства
                row.Values["$BLOCK_NAME"] = bName;
                row.Values["$LAYER"] = br.Layer;
                row.Values["$HANDLE"] = br.Handle.ToString();
                row.Values["$X"] = Math.Round(br.Position.X, 2).ToString("F2");
                row.Values["$Y"] = Math.Round(br.Position.Y, 2).ToString("F2");
                row.Values["$Z"] = Math.Round(br.Position.Z, 2).ToString("F2");
                row.Values["$SCALE"] = br.ScaleFactors.X.ToString("F3");
                row.Values["$ROTATION"] = Math.Round(br.Rotation * 180.0 / Math.PI, 1).ToString("F1");

                // Реальные атрибуты из BlockReference.AttributeCollection
                foreach (ObjectId arId in br.AttributeCollection)
                {
                    try
                    {
                        var ar = (AttributeReference)tr.GetObject(arId, OpenMode.ForRead);
                        string tag = ar.Tag;
                        row.Values[tag] = ar.TextString;

                        if (!data.AllAttributeTags.Contains(tag))
                            data.AllAttributeTags.Add(tag);
                    }
                    catch { }
                }

                // Constant-атрибуты из определения блока (ATTDEF с Constant=true)
                foreach (ObjectId defId in blockDef)
                {
                    if (defId.ObjectClass.DxfName != "ATTDEF") continue;
                    try
                    {
                        var attDef = (AttributeDefinition)tr.GetObject(defId, OpenMode.ForRead);
                        if (attDef.Constant)
                        {
                            string tag = attDef.Tag;
                            if (!row.Values.ContainsKey(tag))
                            {
                                row.Values[tag] = attDef.TextString;
                                if (!data.AllAttributeTags.Contains(tag))
                                    data.AllAttributeTags.Add(tag);
                            }
                        }
                    }
                    catch { }
                }

                // Динамические свойства через нативный Teigha API
                try
                {
                    if (br.IsDynamicBlock)
                    {
                        var dynProps = br.DynamicBlockReferencePropertyCollection;
                        if (dynProps != null)
                        {
                            foreach (DynamicBlockReferenceProperty prop in dynProps)
                            {
                                try
                                {
                                    string dynTag = $"DYN.{prop.PropertyName}";
                                    row.Values[dynTag] = Convert.ToString(prop.Value) ?? "";
                                    if (!data.AllAttributeTags.Contains(dynTag))
                                        data.AllAttributeTags.Add(dynTag);
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }

                // EffectiveName через COM
                try
                {
                    dynamic? acadObj = br.AcadObject;
                    if (acadObj != null)
                    {
                        string effName = acadObj.EffectiveName;
                        row.Values["$EFFECTIVE_NAME"] = effName ?? "";
                    }
                }
                catch { }

                data.Rows.Add(row);
                if (!data.BlockNames.Contains(bName))
                    data.BlockNames.Add(bName);
            }

            tr.Commit();

            // Выравниваем теги
            foreach (var tag in data.AllAttributeTags)
            {
                foreach (var row in data.Rows)
                {
                    if (!row.Values.ContainsKey(tag))
                        row.Values[tag] = "";
                }
            }

            return data;
        }

        /// <summary>
        /// Возвращает уникальные имена блоков в ModelSpace.
        /// </summary>
        public List<string> GetUniqueBlockNames()
        {
            var names = new List<string>();
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return names;

            Database db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BlockTableRecord.ModelSpace)) return names;

            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference)))) continue;
                try
                {
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    var blockDef = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    if (!blockDef.Name.StartsWith("*") && !names.Contains(blockDef.Name))
                        names.Add(blockDef.Name);
                }
                catch { }
            }

            tr.Commit();
            return names;
        }
    }
}
