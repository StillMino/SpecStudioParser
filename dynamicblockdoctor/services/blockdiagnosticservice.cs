using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using HostMgd.ApplicationServices;
using CadApp = HostMgd.ApplicationServices.Application;
using SpecStudioParser.DynamicBlockDoctor.Models;

namespace SpecStudioParser.DynamicBlockDoctor.Services
{
    /// <summary>
    /// Диагностический сканер динамических блоков.
    /// Анализирует определения и вхождения блоков, выявляет потенциальные проблемы
    /// совместимости с nanoCAD.
    /// </summary>
    public sealed class BlockDiagnosticService
    {
        private const int HeavyBlockThreshold = 500; // кол-во примитивов
        private const double HeavyBlockSizeKB = 500; // оценочный размер

        /// <summary>
        /// Полная диагностика всех блоков в чертеже.
        /// </summary>
        public DrawingDiagnosticSummary DiagnoseDrawing()
        {
            var summary = new DrawingDiagnosticSummary();
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return summary;

            Database db = doc.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            // Собираем определения блоков
            var blockDefs = new List<(BlockTableRecord Def, List<BlockReference> Instances)>();

            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.Name.StartsWith("*")) continue; // пропускаем анонимные
                if (btr.IsLayout) continue; // пропускаем пространства

                var instances = new List<BlockReference>();

                // Ищем вхождения в ModelSpace
                if (bt.Has(BlockTableRecord.ModelSpace))
                {
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.ObjectClass.DxfName == "INSERT")
                        {
                            var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                            if (br.BlockTableRecord == btrId)
                                instances.Add(br);
                        }
                    }
                }

                if (instances.Count > 0)
                    blockDefs.Add((btr, instances));
            }

            // Диагностика каждого определения
            var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (def, instances) in blockDefs)
            {
                if (!processedNames.Add(def.Name)) continue;
                var report = DiagnoseBlockDefinition(def, instances, tr);
                summary.Reports.Add(report);
            }

            // Сводная статистика
            summary.TotalBlocks = summary.Reports.Count;
            summary.DynamicBlocks = summary.Reports.Count(r => r.IsDynamic);
            summary.BlocksWithIssues = summary.Reports.Count(r => r.Issues.Count > 0);
            summary.TotalIssues = summary.Reports.Sum(r => r.Issues.Count);
            summary.Errors = summary.Reports.Sum(r => r.Issues.Count(i => i.Severity == BlockIssueSeverity.Error));
            summary.Warnings = summary.Reports.Sum(r => r.Issues.Count(i => i.Severity == BlockIssueSeverity.Warning));

            tr.Commit();
            return summary;
        }

        /// <summary>
        /// Диагностика одного определения блока.
        /// </summary>
        private BlockDiagnosticReport DiagnoseBlockDefinition(
            BlockTableRecord btr, List<BlockReference> instances, Transaction tr)
        {
            var report = new BlockDiagnosticReport
            {
                BlockName = btr.Name,
                Layer = instances.First().Layer,
                Handle = instances.First().Handle.ToString(),
                AttributeCount = 0,
                EntityCount = 0
            };

            // Подсчёт примитивов и атрибутов в определении
            int entityCount = 0;
            int attrDefCount = 0;
            bool hasHatch = false;
            bool hasProxy = false;

            foreach (ObjectId id in btr)
            {
                entityCount++;

                string dxfName = id.ObjectClass.DxfName;
                if (dxfName == "ATTDEF")
                    attrDefCount++;
                if (dxfName == "HATCH")
                {
                    var hatch = (Hatch)tr.GetObject(id, OpenMode.ForRead);
                    hasHatch = hasHatch || hatch.Associative;
                }
                if (dxfName.StartsWith("ACDB_PROXY") || dxfName.Contains("PROXY"))
                {
                    hasProxy = true;
                }
            }

            report.EntityCount = entityCount;
            report.AttributeCount = attrDefCount;
            report.HasAssociativeHatch = hasHatch;
            report.HasProxyObjects = hasProxy;

            // Проверка на динамический блок через COM
            bool isDynamic = false;
            int dynPropCount = 0;
            bool hasVisibility = false;
            bool hasStretch = false;
            bool hasArray = false;

            try
            {
                if (instances.Count > 0)
                {
                    dynamic acadBr = instances[0].AcadObject;
                    if (acadBr != null)
                    {
                        try { isDynamic = Convert.ToBoolean(acadBr.IsDynamicBlock); }
                        catch { }

                        if (isDynamic)
                        {
                            try
                            {
                                dynamic dynProps = acadBr.GetDynamicBlockProperties();
                                dynPropCount = dynProps.Count;
                                for (int i = 0; i < dynPropCount; i++)
                                {
                                    try
                                    {
                                        dynamic prop = dynProps.Item(i);
                                        string propName = Convert.ToString(prop.PropertyName);
                                        if (propName.Contains("Visibility", StringComparison.OrdinalIgnoreCase))
                                            hasVisibility = true;
                                        if (propName.Contains("Stretch", StringComparison.OrdinalIgnoreCase))
                                            hasStretch = true;
                                        if (propName.Contains("Array", StringComparison.OrdinalIgnoreCase))
                                            hasArray = true;
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            report.IsDynamic = isDynamic;
            report.DynamicPropertyCount = dynPropCount;
            report.HasVisibilityStates = hasVisibility;

            // ─── Формирование списка проблем ─────────────────────────────

            if (isDynamic)
            {
                report.Issues.Add(new BlockIssue
                {
                    Type = BlockIssueType.DynamicParameters,
                    Severity = BlockIssueSeverity.Warning,
                    Title = "Динамический блок AutoCAD",
                    Description = $"Блок содержит {dynPropCount} динамических параметров. " +
                                  "nanoCAD поддерживает динамические блоки ограниченно.",
                    Recommendation = "Проверьте работоспособность блока. При проблемах — «заморозьте» " +
                                     "(конвертируйте в статический с текущим состоянием)."
                });
            }

            if (hasVisibility)
            {
                report.Issues.Add(new BlockIssue
                {
                    Type = BlockIssueType.VisibilityParameter,
                    Severity = BlockIssueSeverity.Error,
                    Title = "Параметр видимости",
                    Description = "Параметры видимости AutoCAD могут не работать в nanoCAD или работать некорректно.",
                    Recommendation = "Создайте отдельные блоки для каждого состояния видимости."
                });
            }

            if (hasStretch)
            {
                report.Issues.Add(new BlockIssue
                {
                    Type = BlockIssueType.StretchParameter,
                    Severity = BlockIssueSeverity.Error,
                    Title = "Параметр растяжения",
                    Description = "Параметры растяжения часто ломаются в nanoCAD — геометрия искажается.",
                    Recommendation = "Замените растяжение на несколько размеров блоков или используйте масштабирование."
                });
            }

            if (hasArray)
            {
                report.Issues.Add(new BlockIssue
                {
                    Type = BlockIssueType.ArrayParameter,
                    Severity = BlockIssueSeverity.Warning,
                    Title = "Параметр массива",
                    Description = "Ассоциативные массивы внутри динамических блоков могут потерять связь.",
                    Recommendation = "«Взорвите» массив до простых копий перед использованием в nanoCAD."
                });
            }

            if (hasHatch)
            {
                report.Issues.Add(new BlockIssue
                {
                    Type = BlockIssueType.AssociativeHatch,
                    Severity = BlockIssueSeverity.Error,
                    Title = "Ассоциативная штриховка в блоке",
                    Description = "Ассоциативная штриховка внутри определения блока — " +
                                  "известная причина повреждения файлов DWG в nanoCAD.",
                    Recommendation = "Удалите ассоциативность штриховки (_HATCHEDIT → снять галочку) " +
                                     "или вынесите штриховку из блока."
                });
            }

            if (hasProxy)
            {
                report.Issues.Add(new BlockIssue
                {
                    Type = BlockIssueType.ProxyObjects,
                    Severity = BlockIssueSeverity.Warning,
                    Title = "Прокси-объекты в блоке",
                    Description = "Блок содержит прокси-объекты из вертикальных продуктов Autodesk " +
                                  "(Architecture, MEP, Plant). Они не отображаются в nanoCAD.",
                    Recommendation = "Замерите прокси-объекты на стандартные примитивы (LINE, ARC, PLINE) " +
                                     "через _EXPORTTOAUTOCAD в исходном AutoCAD."
                });
            }

            if (entityCount > HeavyBlockThreshold)
            {
                report.Issues.Add(new BlockIssue
                {
                    Type = BlockIssueType.HeavyBlock,
                    Severity = BlockIssueSeverity.Warning,
                    Title = "Тяжёлый блок",
                    Description = $"Определение блока содержит {entityCount} примитивов. " +
                                  "Это замедляет отрисовку и регенерацию чертежа.",
                    Recommendation = "Разделите блок на более мелкие части или оптимизируйте геометрию."
                });
            }

            // Оценка размера
            report.ApproxSizeKB = Math.Round(entityCount * 0.15, 1); // грубая оценка

            return report;
        }

        /// <summary>
        /// «Замораживает» динамический блок — конвертирует текущее состояние в статический блок.
        /// </summary>
        public string FreezeDynamicBlock(string blockHandle)
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return "Нет активного документа.";

            Database db = doc.Database;
            using var docLock = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();

            try
            {
                if (!long.TryParse(blockHandle, out long handleVal))
                    return "Неверный Handle.";

                Handle h = new Handle(handleVal);
                if (!db.TryGetObjectId(h, out ObjectId brId))
                    return "Объект не найден.";

                var br = (BlockReference)tr.GetObject(brId, OpenMode.ForWrite);
                string originalName = ((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)).Name;
                string frozenName = originalName + "_FROZEN";

                // Попытка 1: нативный Teigha API
                bool converted = false;
                try
                {
                    br.ConvertToStaticBlock(frozenName);
                    converted = true;
                }
                catch (Exception exConvert)
                {
                    doc.Editor.WriteMessage($"\n[BlockDoctor] ConvertToStaticBlock не сработал: {exConvert.Message}. Пытаемся через COM...");
                }

                // Попытка 2: fallback через COM (AcadObject.ConvertToStaticBlock)
                if (!converted)
                {
                    try
                    {
                        dynamic? acadObj = br.AcadObject;
                        if (acadObj != null)
                        {
                            acadObj.ConvertToStaticBlock(frozenName);
                            converted = true;
                        }
                    }
                    catch (Exception exCom)
                    {
                        return $"Оба метода не сработали. Teigha: ошибка. COM: {exCom.Message}";
                    }
                }

                if (!converted)
                    return "Не удалось заморозить блок — оба метода не сработали.";

                // Обновляем графику
                try { br.RecordGraphicsModified(true); } catch { }

                tr.Commit();

                return $"Блок «{originalName}» заморожён → «{frozenName}». " +
                       "Динамические параметры удалены, геометрия зафиксирована.";
            }
            catch (Exception ex)
            {
                tr.Abort();
                return $"Ошибка заморозки блока: {ex.Message}";
            }
        }
    }
}
