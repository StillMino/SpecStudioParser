using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Teigha.DatabaseServices;
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
        /// Сканирует весь ModelSpace (или выборку) и возвращает данные для выгрузки.
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
                if (id.ObjectClass.DxfName != "INSERT") continue;

                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                var blockDef = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                string bName = blockDef.Name;

                // Пропускаем анонимные / пространственные ссылки
                if (bName.StartsWith("*")) continue;

                // Фильтр по имени блока
                if (!string.IsNullOrWhiteSpace(blockNameFilter) &&
                    !bName.Equals(blockNameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var row = new ExportRow
                {
                    BlockName = bName,
                    Layer = br.Layer,
                    Handle = br.Handle.ToString()
                };

                // Системные псевдо-атрибуты
                row.Values["$BLOCK_NAME"] = bName;
                row.Values["$LAYER"] = br.Layer;
                row.Values["$HANDLE"] = br.Handle.ToString();
                row.Values["$X"] = Math.Round(br.Position.X, 2).ToString("F2");
                row.Values["$Y"] = Math.Round(br.Position.Y, 2).ToString("F2");
                row.Values["$Z"] = Math.Round(br.Position.Z, 2).ToString("F2");
                row.Values["$SCALE"] = br.ScaleFactors.X.ToString("F3");

                // Реальные атрибуты
                foreach (ObjectId arId in br.AttributeCollection)
                {
                    var ar = (AttributeReference)tr.GetObject(arId, OpenMode.ForRead);
                    string tag = ar.Tag;
                    row.Values[tag] = ar.TextString;

                    if (!data.AllAttributeTags.Contains(tag))
                        data.AllAttributeTags.Add(tag);
                }

                // Динамические свойства (если есть)
                CollectDynamicProperties(br, tr, row);

                data.Rows.Add(row);

                if (!data.BlockNames.Contains(bName))
                    data.BlockNames.Add(bName);
            }

            tr.Commit();

            if (data.Rows.Count == 0) return data;

            // Пост-обработка: добавляем отсутствующие теги во все строки
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
        /// Собирает динамические свойства BlockReference (видимость, состояния параметров).
        /// </summary>
        private void CollectDynamicProperties(BlockReference br, Transaction tr, ExportRow row)
        {
            try
            {
                // Проверяем, является ли блок динамическим
                dynamic acadBr = br.AcadObject;
                if (acadBr == null) return;

                // Получаем DynamicBlockReferencePropertyCollection через COM
                try
                {
                    dynamic dynProps = acadBr.GetDynamicBlockProperties();
                    if (dynProps != null)
                    {
                        int count = dynProps.Count;
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                dynamic prop = dynProps.Item(i);
                                string propName = Convert.ToString(prop.PropertyName);
                                string propValue = Convert.ToString(prop.Value);

                                if (!string.IsNullOrEmpty(propName))
                                {
                                    string dynTag = $"DYN.{propName}";
                                    row.Values[dynTag] = propValue ?? "";

                                    if (!row.Values.ContainsKey(dynTag))
                                    {
                                        // ничего, уже записано выше
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch
                {
                    // Блок не динамический — это нормально
                }
            }
            catch
            {
                // COM недоступен или блок без динамических свойств
            }
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
                if (id.ObjectClass.DxfName != "INSERT") continue;
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                var blockDef = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                if (!blockDef.Name.StartsWith("*") && !names.Contains(blockDef.Name))
                    names.Add(blockDef.Name);
            }

            tr.Commit();
            return names;
        }
    }
}
