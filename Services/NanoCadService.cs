using System;
using System.Collections.Generic;
using System.Linq;
using SpecStudioParser.Models;

using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.DatabaseServices;

namespace SpecStudioParser.Services
{
    public class NanoCadService
    {
        /// <summary>
        /// Сканирует объекты на основе переданной коллекции ObjectId (поддерживает фильтрацию выбора)
        /// </summary>
        public List<DwgObject> GetObjectsFromCollection(IEnumerable<ObjectId> ids)
        {
            var resultList = new List<DwgObject>();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return resultList;

            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId objId in ids)
                {
                    DBObject obj = tr.GetObject(objId, OpenMode.ForRead);

                    if (obj is Entity entity)
                    {
                        string className = entity.GetRXClass().Name;

                        var dwgObj = new DwgObject
                        {
                            Handle = entity.Handle.ToString(),
                            ObjectName = className,
                            Layer = entity.Layer,
                            ModelStudioId = "—",
                            Note = "Обычный примитив nanoCAD",
                            RawObjectId = objId
                        };

                        // Модульный фильтр распознавания объектов Model Studio CS / CSoft / Блоков
                        bool isModelStudio = className.StartsWith("MDS", StringComparison.OrdinalIgnoreCase) ||
                                             className.StartsWith("Wr", StringComparison.OrdinalIgnoreCase) ||
                                             className.StartsWith("mst", StringComparison.OrdinalIgnoreCase) ||
                                             className.StartsWith("linCS", StringComparison.OrdinalIgnoreCase) ||
                                             className.StartsWith("CAEC", StringComparison.OrdinalIgnoreCase) ||
                                             className.Contains("Studio") ||
                                             className.Contains("CSoft") ||
                                             entity is BlockReference;

                        if (isModelStudio)
                        {
                            try
                            {
                                dynamic acadEntity = entity.AcadObject;
                                if (acadEntity != null)
                                {
                                    dwgObj.ModelStudioId = "MS CS Объект";

                                    // Список базовых инженерных параметров для первоочередной проверки в COM API
                                    string[] checkParams = {
                                        "Part_Name", "Part_Tag", "Part_Material", "Part_Standard",
                                        "Part_Weight", "PART_GROUP", "PART_TYPE", "EXPLICATION_NUMBER",
                                        "AEC_PART_LENGTH", "DIM_HEIGHT"
                                    };

                                    // 1. Сбор параметров верхнего уровня COM-объекта
                                    foreach (var pName in checkParams)
                                    {
                                        try
                                        {
                                            // Используем позднее связывание для безопасного извлечения динамических свойств
                                            var val = Convert.ToString(Microsoft.VisualBasic.CompilerServices.NewLateBinding.LateGet(acadEntity, null, pName, new object[0], null, null, null));
                                            if (!string.IsNullOrEmpty(val))
                                            {
                                                dwgObj.AllAttributes[pName] = val;
                                            }
                                        }
                                        catch { }
                                    }

                                    // 2. Сбор параметров из вложенной структуры Element (если она есть)
                                    try
                                    {
                                        dynamic msElement = acadEntity.Element;
                                        if (msElement != null)
                                        {
                                            foreach (var pName in checkParams)
                                            {
                                                try
                                                {
                                                    var val = Convert.ToString(Microsoft.VisualBasic.CompilerServices.NewLateBinding.LateGet(msElement, null, pName, new object[0], null, null, null));
                                                    if (!string.IsNullOrEmpty(val) && !dwgObj.AllAttributes.ContainsKey(pName))
                                                    {
                                                        dwgObj.AllAttributes[pName] = val;
                                                    }
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                    catch { }

                                    // 3. Извлечение высотной координаты Z для формул отметок (свойство InsertionPoint или Bounds)
                                    try
                                    {
                                        dwgObj.AllAttributes["Z"] = Convert.ToString(acadEntity.InsertionPoint[2]);
                                    }
                                    catch
                                    {
                                        try
                                        {
                                            // Резервный метод определения координаты Z через габариты примитива
                                            dwgObj.AllAttributes["Z"] = Convert.ToString(entity.Bounds.Value.MaxPoint.Z);
                                        }
                                        catch
                                        {
                                            dwgObj.AllAttributes["Z"] = "0";
                                        }
                                    }

                                    // Корректировка отображаемого имени на основе полученных атрибутов
                                    if (dwgObj.AllAttributes.TryGetValue("Part_Name", out string? pNameVal) && !string.IsNullOrEmpty(pNameVal))
                                    {
                                        dwgObj.ObjectName = pNameVal;
                                    }
                                    else if (dwgObj.AllAttributes.TryGetValue("PART_TYPE", out string? pTypeVal) && !string.IsNullOrEmpty(pTypeVal))
                                    {
                                        dwgObj.ObjectName = pTypeVal;
                                    }
                                    else
                                    {
                                        dwgObj.ObjectName = className;
                                    }

                                    // Формируем краткую сводку для вывода в базовую колонку лог-анализатора
                                    List<string> propsSummary = new List<string>();
                                    if (dwgObj.AllAttributes.TryGetValue("Part_Tag", out string? tag)) propsSummary.Add($"Поз: {tag}");
                                    if (dwgObj.AllAttributes.TryGetValue("PART_GROUP", out string? grp)) propsSummary.Add($"Гр: {grp}");
                                    if (dwgObj.AllAttributes.TryGetValue("Z", out string? zCoord)) propsSummary.Add($"Z: {zCoord}");

                                    dwgObj.Note = propsSummary.Count > 0 ? string.Join(", ", propsSummary) : "Атрибуты собраны";
                                }
                            }
                            catch
                            {
                                dwgObj.Note = "Защищенный прокси-объект / Ошибка COM";
                            }
                        }

                        resultList.Add(dwgObj);
                    }
                }
                tr.Commit();
            }

            return resultList;
        }

        /// <summary>
        /// Возвращает объекты из пространства модели (весь чертеж)
        /// </summary>
        public List<DwgObject> GetAllModelSpaceObjects()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return new List<DwgObject>();

            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var ids = new List<ObjectId>();
                foreach (ObjectId id in ms) ids.Add(id);

                tr.Commit();
                return GetObjectsFromCollection(ids);
            }
        }

        /// <summary>
        /// Извлекает список всех доступных классов объектов для динамического построения списков в панели "Быстрый выбор"
        /// </summary>
        public Dictionary<string, List<string>> ExtractAvailableProperties(List<DwgObject> objects)
        {
            var map = new Dictionary<string, List<string>>();

            // Инвариантный набор базовых свойств
            var baseProps = new List<string> { "Наименование", "Слой", "Марка/Позиция", "Тип объекта", "Свойства COM" };
            map["<Все объекты>"] = baseProps;

            foreach (var obj in objects)
            {
                if (string.IsNullOrEmpty(obj.ObjectName)) continue;

                if (!map.ContainsKey(obj.ObjectName))
                {
                    map[obj.ObjectName] = new List<string>(baseProps);
                }
            }

            return map;
        }

        /// <summary>
        /// Выполняет логическую сверку параметров объекта по заданному критерию фильтрации (Аналог БВЫБОР nanoCAD)
        /// </summary>
        public bool IsObjectMatchCriterion(DwgObject obj, string targetClass, string property, string condition, string value)
        {
            if (targetClass != "<Все объекты>" && obj.ObjectName != targetClass)
                return false;

            if (string.IsNullOrEmpty(value)) return true;

            string actualValue = property switch
            {
                "Наименование" => obj.ObjectName,
                "Слой" => obj.Layer,
                "Тип объекта" => obj.ModelStudioId,
                "Марка/Позиция" => obj.AllAttributes.ContainsKey("Part_Tag") ? obj.AllAttributes["Part_Tag"] : "",
                "Свойства COM" => obj.Note,
                _ => obj.Note
            };

            actualValue ??= "";
            value = value.Trim();

            return condition switch
            {
                "=" => actualValue.Equals(value, StringComparison.OrdinalIgnoreCase),
                "!=" => !actualValue.Equals(value, StringComparison.OrdinalIgnoreCase),
                "Содержит" => actualValue.Contains(value, StringComparison.OrdinalIgnoreCase),
                "Не содержит" => !actualValue.Contains(value, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        /// <summary>
        /// Изменяет свойства объекта Model Studio CS на чертеже (Инструмент обратной записи)
        /// </summary>
        public bool UpdateModelStudioProperties(ObjectId objId, string newName, string newTag)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;

            bool success = false;
            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                DBObject obj = tr.GetObject(objId, OpenMode.ForWrite);
                if (obj is Entity entity)
                {
                    try
                    {
                        dynamic acadEntity = entity.AcadObject;
                        if (acadEntity != null)
                        {
                            try { acadEntity.Part_Name = newName; } catch { }
                            try { acadEntity.Part_Tag = newTag; } catch { }

                            try
                            {
                                dynamic msElement = acadEntity.Element;
                                if (msElement != null)
                                {
                                    msElement.Name = newName;
                                    try { msElement.Part_Tag = newTag; } catch { }
                                }
                            }
                            catch { }

                            success = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        doc.Editor.WriteMessage($"\n[SpecStudio Error]: Ошибка записи COM-атрибутов: {ex.Message}");
                    }
                }
                tr.Commit();
            }
            return success;
        }

        /// <summary>
        /// Активирует подсветку и фокус на объекте внутри пространства nanoCAD
        /// </summary>
        public void SelectAndHighlightInCad(ObjectId objId)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            try
            {
                if (objId == ObjectId.Null) return;
                ed.SetImpliedSelection(new[] { objId });
                ed.UpdateScreen();
            }
            catch { }
        }
    }
}