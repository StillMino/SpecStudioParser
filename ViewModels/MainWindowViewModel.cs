using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using SpecStudioParser.Models;
using SpecStudioParser.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Teigha.DatabaseServices;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private NanoCadService _nanoCadService;
        private List<DwgObject> _rawCache = new();
        private HashSet<string> _manuallyAddedHandles = new();

        private string _connectionStatus = "Готов";
        private bool _isManualAddModeActive = false;
        private ReportProfile _activeProfile = new();

        private int _settingsTargetTabIdx = 0;
        private bool _isDatasetEditorActive = false;
        private DatasetConfig? _selectedDataset;
        private string _typeSearchText = "";

        // Переменные для управления файловой структурой XML
        private string _rootProfilesPath = "";
        private string _selectedXmlFile = "";
        private bool _isProfileLoaded = false;

        public Action<List<string>>? RequestSelectionUpdate;
        public Action? OnColumnsStructureChanged;

        public ObservableCollection<DwgObject> GridItems { get; set; } = new();
        public ObservableCollection<Dictionary<string, object>> SpecificationRows { get; set; } = new();

        public ObservableCollection<CheckableTypeItem> AllTypeItems { get; set; } = new();
        public ObservableCollection<CheckableTypeItem> FilteredTypeItems { get; set; } = new();
        public ObservableCollection<string> AvailableAttributes { get; set; } = new();

        // Коллекции для UI управления файлами
        public ObservableCollection<string> AvailableXmlFiles { get; set; } = new();

        #region Свойства

        public string RootProfilesPath
        {
            get => _rootProfilesPath;
            set
            {
                _rootProfilesPath = value;
                OnPropertyChanged();
                RefreshAvailableXmlFilesList();
            }
        }

        public string SelectedXmlFile
        {
            get => _selectedXmlFile;
            set
            {
                _selectedXmlFile = value;
                OnPropertyChanged();
            }
        }

        public bool IsProfileLoaded
        {
            get => _isProfileLoaded;
            set
            {
                _isProfileLoaded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsProfileNotLoaded));
            }
        }

        public bool IsProfileNotLoaded => !IsProfileLoaded;

        public ReportProfile ActiveProfile
        {
            get => _activeProfile;
            set { _activeProfile = value; OnPropertyChanged(); }
        }

        public int SettingsTargetTabIdx
        {
            get => _settingsTargetTabIdx;
            set { _settingsTargetTabIdx = value; OnPropertyChanged(); }
        }

        public bool IsDatasetEditorActive
        {
            get => _isDatasetEditorActive;
            set { _isDatasetEditorActive = value; OnPropertyChanged(); }
        }

        public DatasetConfig? SelectedDataset
        {
            get => _selectedDataset;
            set
            {
                _selectedDataset = value;
                OnPropertyChanged();
                IsDatasetEditorActive = value != null;

                if (value != null)
                {
                    if (AllTypeItems != null && AllTypeItems.Any())
                    {
                        RefreshCheckableTypesStates();
                    }
                }
                OnColumnsStructureChanged?.Invoke();
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; OnPropertyChanged(); }
        }

        public bool IsManualAddModeActive
        {
            get => _isManualAddModeActive;
            set
            {
                _isManualAddModeActive = value;
                OnPropertyChanged();
                ApplyFilteringLogics();
            }
        }

        public string TypeSearchText
        {
            get => _typeSearchText;
            set
            {
                _typeSearchText = value;
                OnPropertyChanged();
                ApplyTypeSearchFilter();
            }
        }

        #endregion

        #region Команды

        public ICommand ScanAllCommand { get; }
        public ICommand ScanSelectedCommand { get; }
        public ICommand RefreshDrawingDataCommand { get; }
        public ICommand OpenSettingsWindowCommand { get; }
        public ICommand ImportXmlCommand { get; }
        public ICommand GenerateSpecificationCommand { get; }

        public ICommand AddDatasetCommand { get; }
        public ICommand RemoveDatasetCommand { get; }
        public ICommand MoveDatasetUpCommand { get; }
        public ICommand MoveDatasetDownCommand { get; }
        public ICommand ConfigureSelectedDatasetCommand { get; }
        public ICommand BackToDatasetListCommand { get; }

        public ICommand AddConditionCommand { get; }
        public ICommand RemoveConditionCommand { get; }
        public ICommand RefreshCadTypesCommand { get; }

        public ICommand SelectAllTypesCmd { get; }
        public ICommand UnselectAllTypesCmd { get; }
        public ICommand InvertTypesCmd { get; }

        public ICommand AddColumnCommand { get; }
        public ICommand RemoveColumnCommand { get; }
        public ICommand MoveColumnUpCommand { get; }
        public ICommand MoveColumnDownCommand { get; }

        public ICommand SaveXmlToDesktopCommand { get; }
        public ICommand ApplySettingsCommand { get; }

        // КОМАНДЫ ДЛЯ СТАРТОВОГО МЕНЮ
        public ICommand CreateNewProfileCommand { get; }
        public ICommand LoadSelectedXmlCommand { get; }
        public ICommand SaveCurrentProfileCommand { get; }
        public ICommand CloseProfileCommand { get; }
        public ICommand ChangeRootFolderCommand { get; }

        #endregion

        public MainWindowViewModel()
        {
            _nanoCadService = new NanoCadService();

            ScanAllCommand = new RelayCommand(ScanAllDrawing);
            ScanSelectedCommand = new RelayCommand(ScanSelectedOnly);
            GenerateSpecificationCommand = new RelayCommand(ExecuteApplySettings);
            OpenSettingsWindowCommand = new RelayCommand(ExecuteOpenSettings);
            ImportXmlCommand = new RelayCommand(ExecuteImportXml);

            AddDatasetCommand = new RelayCommand(ExecuteAddDataset);
            RemoveDatasetCommand = new RelayCommand<DatasetConfig>(ExecuteRemoveDataset);
            MoveDatasetUpCommand = new RelayCommand<DatasetConfig>(ExecuteMoveDatasetUp);
            MoveDatasetDownCommand = new RelayCommand<DatasetConfig>(ExecuteMoveDatasetDown);
            ConfigureSelectedDatasetCommand = new RelayCommand<DatasetConfig>(ExecuteConfigureDataset);
            BackToDatasetListCommand = new RelayCommand(() => { SelectedDataset = null; SettingsTargetTabIdx = 0; });

            AddConditionCommand = new RelayCommand(ExecuteAddCondition);
            RemoveConditionCommand = new RelayCommand<FilterConditionItem>(ExecuteRemoveCondition);
            RefreshCadTypesCommand = new RelayCommand(LoadCadTypesFromDrawing);

            SelectAllTypesCmd = new RelayCommand(() => SetAllTypesSelection(true));
            UnselectAllTypesCmd = new RelayCommand(() => SetAllTypesSelection(false));
            InvertTypesCmd = new RelayCommand(ExecuteInvertTypes);

            AddColumnCommand = new RelayCommand(ExecuteAddColumn);
            RemoveColumnCommand = new RelayCommand<ReportColumnConfig>(ExecuteRemoveColumn);
            MoveColumnUpCommand = new RelayCommand<ReportColumnConfig>(ExecuteMoveColumnUp);
            MoveColumnDownCommand = new RelayCommand<ReportColumnConfig>(ExecuteMoveColumnDown);

            SaveXmlToDesktopCommand = new RelayCommand(ExecuteSaveXmlToDesktop);
            ApplySettingsCommand = new RelayCommand(ExecuteApplySettings);

            // Инициализация команд управления жизненным циклом XML файлов
            CreateNewProfileCommand = new RelayCommand(ExecuteCreateNewProfile);
            LoadSelectedXmlCommand = new RelayCommand(ExecuteLoadSelectedXml);
            SaveCurrentProfileCommand = new RelayCommand(ExecuteSaveCurrentProfile);
            CloseProfileCommand = new RelayCommand(() => { IsProfileLoaded = false; SelectedXmlFile = ""; });
            ChangeRootFolderCommand = new AsyncRelayCommand(ExecuteChangeRootFolder);

            // Автоматическое определение папки по умолчанию в "Документах"
            InitializeDefaultFolder();
        }

        #region Логика работы с директорией профилей

        private void InitializeDefaultFolder()
        {
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string defaultPath = Path.Combine(docs, "SpecStudioProfiles");

                if (!Directory.Exists(defaultPath))
                {
                    Directory.CreateDirectory(defaultPath);
                }
                _rootProfilesPath = defaultPath;
                RefreshAvailableXmlFilesList();
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Ошибка создания папки профилей: {ex.Message}";
            }
        }

        public void RefreshAvailableXmlFilesList()
        {
            AvailableXmlFiles.Clear();
            if (!Directory.Exists(RootProfilesPath)) return;

            var files = Directory.GetFiles(RootProfilesPath, "*.xml")
                                 .Select(Path.GetFileName)
                                 .Where(f => f != null)
                                 .OrderBy(f => f);

            foreach (var file in files)
            {
                AvailableXmlFiles.Add(file!);
            }
        }

        private void ExecuteCreateNewProfile()
        {
            ActiveProfile = new ReportProfile { Name = "Новая спецификация САПР" };
            var defaultDataset = new DatasetConfig { Caption = "Новый набор данных" };
            defaultDataset.Columns.Add(new ReportColumnConfig { Caption = "Наименование", DataFormula = "[PART_NAME]", Visible = 1 });
            ActiveProfile.Datasets.Add(defaultDataset);

            SelectedDataset = defaultDataset;
            SelectedXmlFile = "Новый_профиль.xml";
            IsProfileLoaded = true;

            RebuildFilterFormulaFromConditions();
        }

        private void ExecuteLoadSelectedXml()
        {
            if (string.IsNullOrEmpty(SelectedXmlFile))
            {
                ConnectionStatus = "Ошибка: Выберите файл из списка!";
                return;
            }

            string fullPath = Path.Combine(RootProfilesPath, SelectedXmlFile);
            if (!File.Exists(fullPath)) return;

            try
            {
                ActiveProfile = MscsXmlService.LoadFromMscsXml(fullPath);
                if (ActiveProfile.Datasets.Any())
                {
                    SelectedDataset = ActiveProfile.Datasets.First();
                }

                IsProfileLoaded = true;
                ConnectionStatus = $"Спецификация успешно загружена: {SelectedXmlFile}";
                LoadSetDataStructuresSafe();
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Ошибка разбора структуры XML профиля: {ex.Message}";
            }
        }

        private void ExecuteSaveCurrentProfile()
        {
            if (string.IsNullOrEmpty(SelectedXmlFile)) return;

            // Корректируем расширение файла принудительно
            string fileName = SelectedXmlFile;
            if (!fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                fileName += ".xml";

            try
            {
                string fullPath = Path.Combine(RootProfilesPath, fileName);
                XDocument xDoc = BuildXmlDataStructure();
                xDoc.Save(fullPath);

                ConnectionStatus = $"Профиль сохранен в базу: {fileName}";
                RefreshAvailableXmlFilesList();
                SelectedXmlFile = fileName;
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Ошибка сохранения XML: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task ExecuteChangeRootFolder()
        {
            var dialog = new OpenFolderDialog();
            var activeWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.Windows.FirstOrDefault(w => w.IsActive)
                : null;

            var result = await dialog.ShowAsync(activeWindow!);
            if (!string.IsNullOrEmpty(result))
            {
                RootProfilesPath = result;
            }
        }

        private XDocument BuildXmlDataStructure()
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("Report",
                    new XElement("DatasetProfile",
                        ActiveProfile.Datasets.Select(ds =>
                            new XElement("Dataset",
                                new XAttribute("assemblyGrouping", "0"),
                                new XAttribute("assemblyFilter", "0"),
                                new XAttribute("binding", "Fields"),
                                new XAttribute("relationType", ""),
                                new XAttribute("join", "outer"),
                                new XAttribute("hierarchy", "0"),
                                new XElement("Table",
                                    new XAttribute("caption", ds.Caption),
                                    new XAttribute("filter", ds.FilterFormula ?? "1"),
                                    new XAttribute("result.filter", ""),
                                    new XAttribute("aggregated", ds.Aggregated),

                                    new XElement("Types",
                                        ds.TargetTypes.Select(t => new XElement("Type", new XAttribute("name", t)))
                                    ),

                                    new XElement("Fields",
                                        ds.Columns.Select(col => new XElement("Field",
                                            new XAttribute("caption", col.Caption),
                                            new XAttribute("data", col.DataFormula.Replace("[", "").Replace("]", "")),
                                            new XAttribute("type", col.DataFormula.Contains("[") ? "0" : "1"),
                                            new XAttribute("aggregate", col.Aggregate),
                                            new XAttribute("visible", col.Visible),
                                            new XAttribute("format", "")
                                        ))
                                    )
                                )
                            )
                        )
                    )
                )
            );
        }

        #endregion

        private void InitDefaultMscsProfile()
        {
            // Метод сохранен для обратной совместимости, но теперь управляется файлами.
            ExecuteCreateNewProfile();
        }

        private void ExecuteApplySettings()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                ConnectionStatus = "Ошибка: Нет активного документа nanoCAD.";
                return;
            }

            ConnectionStatus = "Обновление спецификации...";

            try
            {
                using (doc.LockDocument())
                {
                    SyncSelectedTypesToDatasetInternal();
                    AutoFillAttributesFromSelectedTypesInternal();
                    GenerateSpecificationInternal();
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Ошибка при обновлении: {ex.Message}";
            }
        }

        private void ScanAllDrawing()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            {
                _rawCache = _nanoCadService.GetAllModelSpaceObjects();
                LoadCadTypesFromDrawingInternal();
                ApplyFilteringLogics();
            }
            ExecuteApplySettings();
        }

        private void ScanSelectedOnly()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            {
                PromptSelectionResult selRes = doc.Editor.SelectImplied();
                if (selRes.Status == PromptStatus.OK && selRes.Value.Count > 0)
                {
                    _rawCache = _nanoCadService.GetObjectsFromCollection(selRes.Value.GetObjectIds());
                    LoadCadTypesFromDrawingInternal();
                    ApplyFilteringLogics();
                }
            }
            ExecuteApplySettings();
        }

        public void ApplyFilteringLogics()
        {
            GridItems.Clear();
            var targetSource = IsManualAddModeActive
                ? _rawCache.Where(x => _manuallyAddedHandles.Contains(x.Handle))
                : _rawCache;

            foreach (var item in targetSource)
            {
                GridItems.Add(item);
            }
            ConnectionStatus = $"Кэш элементов содержит записей: {GridItems.Count}";
        }

        private void ExecuteOpenSettings()
        {
            if (!_rawCache.Any())
            {
                Document doc = CadApp.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    using (doc.LockDocument())
                    {
                        _rawCache = _nanoCadService.GetAllModelSpaceObjects();
                        LoadCadTypesFromDrawingInternal();
                    }
                }
            }
            else
            {
                LoadCadTypesFromDrawingInternal();
            }

            AutoFillAttributesFromSelectedTypesInternal();

            var settingsWin = new Views.SpecificationSettingsWindow { DataContext = this };

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                settingsWin.Show(desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow);
            }
            else
            {
                settingsWin.Show();
            }
        }

        #region Работа со справочником типов чертежа

        private void LoadCadTypesFromDrawing()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            {
                LoadCadTypesFromDrawingInternal();
            }
        }

        private void LoadCadTypesFromDrawingInternal()
        {
            AllTypeItems.Clear();
            var sourceObjects = _rawCache.Any() ? _rawCache : _nanoCadService.GetAllModelSpaceObjects();
            var uniqueTypes = sourceObjects.Select(o => o.ObjectName).Distinct().OrderBy(t => t);

            foreach (var type in uniqueTypes)
            {
                var isChecked = SelectedDataset?.TargetTypes.Contains(type, StringComparer.OrdinalIgnoreCase) ?? false;
                var item = new CheckableTypeItem { TypeName = type, IsSelected = isChecked };

                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(CheckableTypeItem.IsSelected))
                    {
                        if (SelectedDataset != null)
                        {
                            SelectedDataset.TargetTypes.Clear();
                            foreach (var checkedItem in AllTypeItems.Where(x => x.IsSelected))
                            {
                                SelectedDataset.TargetTypes.Add(checkedItem.TypeName);
                            }
                            AutoFillAttributesFromSelectedTypesInternal();
                        }
                    }
                };
                AllTypeItems.Add(item);
            }
            ApplyTypeSearchFilter();
        }

        private void RefreshCheckableTypesStates()
        {
            if (SelectedDataset == null) return;
            foreach (var item in AllTypeItems)
            {
                item.IsSelected = SelectedDataset.TargetTypes.Contains(item.TypeName, StringComparer.OrdinalIgnoreCase);
            }
            ApplyTypeSearchFilter();
        }

        private void ApplyTypeSearchFilter()
        {
            FilteredTypeItems.Clear();
            var items = string.IsNullOrWhiteSpace(TypeSearchText)
                ? AllTypeItems
                : AllTypeItems.Where(x => x.TypeName.Contains(TypeSearchText, StringComparison.OrdinalIgnoreCase));

            foreach (var item in items)
            {
                FilteredTypeItems.Add(item);
            }
        }

        private void SetAllTypesSelection(bool select)
        {
            foreach (var item in FilteredTypeItems) item.IsSelected = select;
        }

        private void ExecuteInvertTypes()
        {
            foreach (var item in FilteredTypeItems) item.IsSelected = !item.IsSelected;
        }

        private void SyncSelectedTypesToDatasetInternal()
        {
            if (SelectedDataset == null) return;
            SelectedDataset.TargetTypes.Clear();
            foreach (var item in AllTypeItems.Where(x => x.IsSelected))
            {
                SelectedDataset.TargetTypes.Add(item.TypeName);
            }
        }

        private void AutoFillAttributesFromSelectedTypesInternal()
        {
            if (SelectedDataset == null) return;

            var dynamicAttributes = _rawCache
                .Where(obj => SelectedDataset.TargetTypes.Contains(obj.ObjectName, StringComparer.OrdinalIgnoreCase))
                .SelectMany(obj => obj.AllAttributes.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(attr => attr)
                .ToList();

            var baseAttributes = new List<string> { "PART_NAME", "BOM_GROUP", "AEC_ACCESSORY", "Layer", "Handle" };

            AvailableAttributes.Clear();
            foreach (var attr in baseAttributes.Union(dynamicAttributes, StringComparer.OrdinalIgnoreCase))
            {
                AvailableAttributes.Add(attr);
            }
            OnPropertyChanged(nameof(AvailableAttributes));
        }

        // НОВЫЕ МЕТОДЫ: Обработка событий изменения выделения строк в DataGrid
        // Используются для отправки хэндлов (Handles) выделенных строк в nanoCAD

        public void OnAnalyzerGridSelectionChanged(List<string> selectedHandles)
        {
            if (selectedHandles != null && RequestSelectionUpdate != null)
            {
                RequestSelectionUpdate.Invoke(selectedHandles);
            }
        }

        public void OnSpecGridSelectionChanged(List<string> selectedHandles)
        {
            if (selectedHandles != null && RequestSelectionUpdate != null)
            {
                RequestSelectionUpdate.Invoke(selectedHandles);
            }
        }

        #endregion

        #region Движок Вычисления Спецификации

        private void GenerateSpecificationInternal()
        {
            if (!_rawCache.Any() || !ActiveProfile.Datasets.Any()) return;

            var aggregatedReportList = new List<Dictionary<string, object>>();

            foreach (var dataset in ActiveProfile.Datasets)
            {
                var stepEvaluatedRows = new List<Dictionary<string, string>>();

                foreach (var dwgObj in _rawCache)
                {
                    if (dataset.TargetTypes.Any() && !dataset.TargetTypes.Contains(dwgObj.ObjectName, StringComparer.OrdinalIgnoreCase))
                        continue;

                    var evalDict = new Dictionary<string, string>(dwgObj.AllAttributes, StringComparer.OrdinalIgnoreCase);
                    evalDict["Handle"] = dwgObj.Handle;
                    evalDict["ObjectName"] = dwgObj.ObjectName;
                    evalDict["Layer"] = dwgObj.Layer;

                    if (!string.IsNullOrEmpty(dataset.FilterFormula) && dataset.FilterFormula != "1")
                    {
                        bool conditionPassed = true;
                        foreach (var cond in dataset.FilterConditions)
                        {
                            string val = evalDict.ContainsKey(cond.Attribute) ? evalDict[cond.Attribute] : "";
                            if (cond.Operator == "=" && val != cond.Value) conditionPassed = false;
                            if (cond.Operator == "<>" && val == cond.Value) conditionPassed = false;
                            if (cond.Operator == ">" && string.Compare(val, cond.Value) <= 0) conditionPassed = false;
                            if (cond.Operator == "<" && string.Compare(val, cond.Value) >= 0) conditionPassed = false;
                        }
                        if (!conditionPassed) continue;
                    }

                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var col in dataset.Columns)
                    {
                        row[col.Caption] = FormulaEvaluator.Evaluate(col.DataFormula, evalDict);
                    }
                    row["__Handle"] = dwgObj.Handle;
                    row["__RawObjectIdString"] = dwgObj.RawObjectId.ToString();

                    stepEvaluatedRows.Add(row);
                }

                var groupColumns = dataset.Columns.Where(c => c.Aggregate == 0 && c.Visible == 1).ToList();
                var grouped = stepEvaluatedRows.GroupBy(r => string.Join("|", groupColumns.Select(c => r.ContainsKey(c.Caption) ? r[c.Caption] : "")));

                foreach (var g in grouped)
                {
                    var repRow = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    var first = g.First();

                    foreach (var col in groupColumns) repRow[col.Caption] = first[col.Caption];

                    foreach (var col in dataset.Columns.Where(c => c.Aggregate != 0))
                    {
                        if (col.Aggregate == 1)
                        {
                            repRow[col.Caption] = g.Count().ToString();
                        }
                        else if (col.Aggregate == 8)
                        {
                            repRow[col.Caption] = g.Sum(r => double.TryParse(r.ContainsKey(col.Caption) ? r[col.Caption] : "0", out double d) ? d : 0).ToString();
                        }
                    }

                    repRow["__Handle"] = first["__Handle"];
                    repRow["__RawObjectIdString"] = first["__RawObjectIdString"];
                    aggregatedReportList.Add(repRow);
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                SpecificationRows.Clear();
                foreach (var row in aggregatedReportList)
                {
                    SpecificationRows.Add(row);
                }
                ConnectionStatus = $"Спецификация успешно обновлена. Строк: {SpecificationRows.Count}";
                OnColumnsStructureChanged?.Invoke();
            });
        }

        #endregion

        private async void ExecuteImportXml()
        {
            var dialog = new OpenFileDialog();
            dialog.Filters.Add(new FileDialogFilter { Name = "Model Studio XML", Extensions = { "xml" } });

            var activeWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.Windows.FirstOrDefault()
                : null;

            var result = await dialog.ShowAsync(activeWindow!);
            if (result != null && result.Length > 0)
            {
                try
                {
                    ActiveProfile = MscsXmlService.LoadFromMscsXml(result[0]);
                    if (ActiveProfile.Datasets.Any())
                    {
                        SelectedDataset = ActiveProfile.Datasets.First();
                    }

                    // Синхронизируем имя файла с базой
                    SelectedXmlFile = Path.GetFileName(result[0]);
                    // Копируем импортируемый файл в корневую рабочую папку
                    string destFile = Path.Combine(RootProfilesPath, SelectedXmlFile);
                    File.Copy(result[0], destFile, true);

                    IsProfileLoaded = true;
                    RefreshAvailableXmlFilesList();
                    LoadSetDataStructuresSafe();
                }
                catch (Exception ex)
                {
                    ConnectionStatus = $"Ошибка импорта: {ex.Message}";
                }
            }
        }

        private void LoadSetDataStructuresSafe()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            {
                LoadCadTypesFromDrawingInternal();
            }
            OnColumnsStructureChanged?.Invoke();
            ExecuteApplySettings();
        }

        private void ExecuteSaveXmlToDesktop()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string validFileName = string.Join("_", ActiveProfile.Name.Split(Path.GetInvalidFileNameChars()));
                string fullPath = Path.Combine(desktopPath, $"{validFileName}.xml");

                XDocument xDoc = BuildXmlDataStructure();
                xDoc.Save(fullPath);
                ConnectionStatus = $"Профиль экспортирован на Рабочий стол: {Path.GetFileName(fullPath)}";
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Ошибка экспорта XML: {ex.Message}";
            }
        }

        #region Управление Структурой Наборов (Без вызовов САПР)

        private void ExecuteAddDataset()
        {
            var ds = new DatasetConfig { Caption = $"Набор данных {ActiveProfile.Datasets.Count + 1}" };
            ds.Columns.Add(new ReportColumnConfig { Caption = "Наименование", DataFormula = "[PART_NAME]", Visible = 1 });
            ActiveProfile.Datasets.Add(ds);
        }

        private void ExecuteRemoveDataset(DatasetConfig? ds)
        {
            if (ds == null) return;
            ActiveProfile.Datasets.Remove(ds);
            if (SelectedDataset == ds) SelectedDataset = null;
        }

        private void ExecuteMoveDatasetUp(DatasetConfig? ds)
        {
            if (ds == null) return;
            int idx = ActiveProfile.Datasets.IndexOf(ds);
            if (idx > 0) ActiveProfile.Datasets.Move(idx, idx - 1);
        }

        private void ExecuteMoveDatasetDown(DatasetConfig? ds)
        {
            if (ds == null) return;
            int idx = ActiveProfile.Datasets.IndexOf(ds);
            if (idx >= 0 && idx < ActiveProfile.Datasets.Count - 1) ActiveProfile.Datasets.Move(idx, idx + 1);
        }

        private void ExecuteConfigureDataset(DatasetConfig? ds)
        {
            if (ds == null) return;
            SelectedDataset = ds;
            SettingsTargetTabIdx = 1;
        }

        private void ExecuteAddCondition()
        {
            SelectedDataset?.AddRootFilterCondition();
        }

        private void ExecuteRemoveCondition(FilterConditionItem? item)
        {
            SelectedDataset?.RemoveFilterCondition(item);
        }

        private void RebuildFilterFormulaFromConditions()
        {
            SelectedDataset?.EnsureRootFilterItems();
        }

        #endregion

        #region Управление Структурой Колонок (Без вызовов САПР)

        private void ExecuteAddColumn()
        {
            if (SelectedDataset == null) return;
            var col = new ReportColumnConfig { Caption = $"Колонка {SelectedDataset.Columns.Count + 1}", DataFormula = "", Visible = 1 };
            SelectedDataset.Columns.Add(col);
            OnColumnsStructureChanged?.Invoke();
        }

        private void ExecuteRemoveColumn(ReportColumnConfig? col)
        {
            if (SelectedDataset == null || col == null) return;
            SelectedDataset.Columns.Remove(col);
            OnColumnsStructureChanged?.Invoke();
        }

        private void ExecuteMoveColumnUp(ReportColumnConfig? col)
        {
            if (SelectedDataset == null || col == null) return;
            int idx = SelectedDataset.Columns.IndexOf(col);
            if (idx > 0)
            {
                SelectedDataset.Columns.Move(idx, idx - 1);
                OnColumnsStructureChanged?.Invoke();
            }
        }

        private void ExecuteMoveColumnDown(ReportColumnConfig? col)
        {
            if (SelectedDataset == null || col == null) return;
            int idx = SelectedDataset.Columns.IndexOf(col);
            if (idx >= 0 && idx < SelectedDataset.Columns.Count - 1)
            {
                SelectedDataset.Columns.Move(idx, idx + 1);
                OnColumnsStructureChanged?.Invoke();
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

