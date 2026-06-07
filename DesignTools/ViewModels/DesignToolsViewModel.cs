using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HostMgd.ApplicationServices;
using SpecStudioParser.DesignTools.Commands;
using SpecStudioParser.DesignTools.Services;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.ViewModels
{
    public enum DesignToolAccessLevel { Free, Paid }
    public enum DesignToolContext { Drafting2D, Modeling3D, Universal }

    public partial class DesignToolFeatureViewModel : ObservableObject
    {
        [ObservableProperty] private bool _isEnabled = true;
        [ObservableProperty] private string _status = "Готово";

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string IconPath { get; }
        public string IconPngPath { get; }
        public string IconGeometry { get; }
        public string NanoCadCommandName { get; }
        public DesignToolAccessLevel AccessLevel { get; }
        public DesignToolContext Context { get; }
        public ICommand RunCommand { get; }

        public string AccessLabel => AccessLevel == DesignToolAccessLevel.Free ? "Бесплатно" : "Платно";
        public string ContextLabel => Context switch { DesignToolContext.Drafting2D => "2D", DesignToolContext.Modeling3D => "3D", _ => "2D/3D" };

        public DesignToolFeatureViewModel(DesignToolCommandDescriptor descriptor, DesignToolAccessLevel accessLevel, DesignToolContext context, string iconGeometry, Action<DesignToolFeatureViewModel> execute)
        {
            Id = descriptor.Id;
            Name = descriptor.Name;
            Description = descriptor.Description;
            IconPath = descriptor.IconPath;
            IconPngPath = descriptor.IconPngPath;
            NanoCadCommandName = descriptor.NanoCadCommandName;
            AccessLevel = accessLevel;
            Context = context;
            IconGeometry = iconGeometry;
            RunCommand = new RelayCommand(() => execute(this));
        }

        public DesignToolFeatureViewModel(string id, string name, string description, DesignToolAccessLevel accessLevel, DesignToolContext context, Action<DesignToolFeatureViewModel> execute)
            : this(new DesignToolCommandDescriptor { Id = id, Name = name, Description = description }, accessLevel, context, string.Empty, execute)
        {
        }
    }

    public partial class DesignToolBlockViewModel : ObservableObject
    {
        [ObservableProperty] private bool _isEnabled = true;
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public ObservableCollection<DesignToolFeatureViewModel> Features { get; } = new();
        public DesignToolBlockViewModel(string id, string name, string description) { Id = id; Name = name; Description = description; }
    }

    public partial class DesignToolsViewModel : ObservableObject
    {
        private const string McadHorizontalIcon = "M5,18 L10,13 L17,13 M14,7 L21,7 M14,10 L21,10 M3,20 L5,18 L6,20 M6,16 L20,16";
        private const string McadVerticalIcon = "M5,18 L10,13 L17,13 M14,7 L21,7 M14,10 L21,10 M3,20 L5,18 L6,20 M10,4 L10,21";
        private const string MLeaderHorizontalIcon = "M4,18 L9,14 L13,14 L18,10 M3,20 L4,18 L6,19 M14,5 L21,5 L21,10 L14,10 Z M15.5,7.5 L19.5,7.5 M6,16 L21,16";
        private const string MLeaderVerticalIcon = "M4,18 L9,14 L13,14 L18,10 M3,20 L4,18 L6,19 M14,5 L21,5 L21,10 L14,10 Z M15.5,7.5 L19.5,7.5 M10,4 L10,21";
        private const string DistributeHorizontalIcon = "M4,8 L20,8 M6,5 L6,11 M12,5 L12,11 M18,5 L18,11";
        private const string DistributeVerticalIcon = "M8,4 L8,20 M5,6 L11,6 M5,12 L11,12 M5,18 L11,18";
        private const string DiagnosticsIcon = "M5,5 L19,5 L19,19 L5,19 Z M8,9 L16,9 M8,12 L16,12 M8,15 L13,15";

        private readonly MultiCadLeaderAlignmentService _leaderAlignmentService = new();
        private readonly SelectionDiagnosticsService _selectionDiagnosticsService = new();

        [ObservableProperty] private string _status = "Инструменты проектировщика готовы к работе.";
        [ObservableProperty] private string _documentStatus = "Документ nanoCAD не проверен.";

        public ObservableCollection<DesignToolBlockViewModel> Blocks { get; } = new();
        public ICommand RefreshContextCommand { get; }

        public DesignToolsViewModel()
        {
            RefreshContextCommand = new RelayCommand(RefreshContext);
            Blocks.Add(CreateDraftingBlock());
            Blocks.Add(CreateDiagnosticsBlock());
            Blocks.Add(CreateModelBlock());
            Blocks.Add(CreateSpecifierBridgeBlock());
            RefreshContext();
        }

        private DesignToolBlockViewModel CreateDraftingBlock()
        {
            var block = new DesignToolBlockViewModel("2d-drafting", "2D-проектирование", "Быстрые команды для работы с чертежом и выделением объектов.");
            AddFeature(block, "2d-multicad-leaders-align-horizontal", "MultiCAD-выноски: горизонтально", "Выравнивает универсальные, групповые и другие MultiCAD-выноски по Y первой выбранной выноски.", "DT_ALIGN_MCAD_LEADERS_H", McadHorizontalIcon, ExecuteAlignMultiCadLeadersHorizontal);
            AddFeature(block, "2d-multicad-leaders-align-vertical", "MultiCAD-выноски: вертикально", "Выравнивает универсальные, групповые и другие MultiCAD-выноски по X первой выбранной выноски.", "DT_ALIGN_MCAD_LEADERS_V", McadVerticalIcon, ExecuteAlignMultiCadLeadersVertical);
            AddFeature(block, "2d-teigha-mleaders-align-horizontal", "Мультивыноски: горизонтально", "Выравнивает стандартные Teigha/nanoCAD мультивыноски по Y первой выбранной мультивыноски.", "DT_ALIGN_MLEADERS_H", MLeaderHorizontalIcon, ExecuteAlignTeighaMLeadersHorizontal);
            AddFeature(block, "2d-teigha-mleaders-align-vertical", "Мультивыноски: вертикально", "Выравнивает стандартные Teigha/nanoCAD мультивыноски по X первой выбранной мультивыноски.", "DT_ALIGN_MLEADERS_V", MLeaderVerticalIcon, ExecuteAlignTeighaMLeadersVertical);
            AddFeature(block, "2d-multicad-leaders-distribute-horizontal", "MultiCAD-выноски: распределить горизонтально", "Равномерно распределяет MultiCAD-выноски между крайними выносками по X.", "DT_DISTR_MCAD_LEADERS_H", DistributeHorizontalIcon, ExecuteDistributeMultiCadLeadersHorizontal);
            AddFeature(block, "2d-multicad-leaders-distribute-vertical", "MultiCAD-выноски: распределить вертикально", "Равномерно распределяет MultiCAD-выноски между крайними выносками по Y.", "DT_DISTR_MCAD_LEADERS_V", DistributeVerticalIcon, ExecuteDistributeMultiCadLeadersVertical);
            AddFeature(block, "2d-teigha-mleaders-distribute-horizontal", "Мультивыноски: распределить горизонтально", "Равномерно распределяет Teigha-мультивыноски между крайними мультивыносками по X.", "DT_DISTR_MLEADERS_H", DistributeHorizontalIcon, ExecuteDistributeTeighaMLeadersHorizontal);
            AddFeature(block, "2d-teigha-mleaders-distribute-vertical", "Мультивыноски: распределить вертикально", "Равномерно распределяет Teigha-мультивыноски между крайними мультивыносками по Y.", "DT_DISTR_MLEADERS_V", DistributeVerticalIcon, ExecuteDistributeTeighaMLeadersVertical);
            block.Features.Add(new DesignToolFeatureViewModel("2d-selection-info", "Сведения о выделении", "Показывает базовую информацию о текущем выделении nanoCAD.", DesignToolAccessLevel.Free, DesignToolContext.Drafting2D, ExecuteSelectionInfo));
            block.Features.Add(new DesignToolFeatureViewModel("2d-select-layer", "Выделить по слою", "Будущая функция массового выделения объектов на том же слое.", DesignToolAccessLevel.Free, DesignToolContext.Drafting2D, ExecuteStub));
            return block;
        }

        private static void AddFeature(DesignToolBlockViewModel block, string id, string name, string description, string commandName, string icon, Action<DesignToolFeatureViewModel> execute)
        {
            block.Features.Add(new DesignToolFeatureViewModel(new DesignToolCommandDescriptor { Id = id, Name = name, Description = description, NanoCadCommandName = commandName }, DesignToolAccessLevel.Free, DesignToolContext.Drafting2D, icon, execute));
        }

        private DesignToolBlockViewModel CreateDiagnosticsBlock()
        {
            var block = new DesignToolBlockViewModel("diagnostics", "Диагностика", "Служебные инструменты для определения реальных типов и свойств выбранных объектов.");
            block.Features.Add(new DesignToolFeatureViewModel(new DesignToolCommandDescriptor { Id = "diagnostics-selected-objects", Name = "Диагностика выбранных объектов", Description = "Выводит в командную строку nanoCAD типы выбранных объектов, RXClass, слой, handle и найденные свойства точек.", NanoCadCommandName = "DT_DIAG_SELECTION" }, DesignToolAccessLevel.Free, DesignToolContext.Universal, DiagnosticsIcon, ExecuteSelectionDiagnostics));
            return block;
        }

        private DesignToolBlockViewModel CreateModelBlock()
        {
            var block = new DesignToolBlockViewModel("3d-model-tools", "3D и Model Studio CS", "Инструменты проверки и анализа объектов модели.");
            block.Features.Add(new DesignToolFeatureViewModel("3d-check-empty-parameters", "Проверка пустых параметров", "Будущая проверка обязательных параметров объектов Model Studio CS.", DesignToolAccessLevel.Paid, DesignToolContext.Modeling3D, ExecuteStub));
            block.Features.Add(new DesignToolFeatureViewModel("3d-focus-selection", "Фокус на выделении", "Будущая команда фокусировки и визуального контроля выбранных объектов.", DesignToolAccessLevel.Free, DesignToolContext.Modeling3D, ExecuteStub));
            return block;
        }

        private DesignToolBlockViewModel CreateSpecifierBridgeBlock()
        {
            var block = new DesignToolBlockViewModel("specifier-bridge", "Связь со спецификатором", "Функции, которые смогут использовать данные спецификатора, но не меняют его текущую логику.");
            block.Features.Add(new DesignToolFeatureViewModel("specifier-check-active-profile", "Проверка данных спецификации", "Будущая функция анализа данных по активному профилю спецификатора.", DesignToolAccessLevel.Paid, DesignToolContext.Universal, ExecuteStub));
            return block;
        }

        private void ExecuteAlignMultiCadLeadersHorizontal(DesignToolFeatureViewModel f) => ExecuteLeaderAlignment(f, LeaderAlignmentAxis.Horizontal, LeaderAlignmentSource.MultiCad);
        private void ExecuteAlignMultiCadLeadersVertical(DesignToolFeatureViewModel f) => ExecuteLeaderAlignment(f, LeaderAlignmentAxis.Vertical, LeaderAlignmentSource.MultiCad);
        private void ExecuteAlignTeighaMLeadersHorizontal(DesignToolFeatureViewModel f) => ExecuteLeaderAlignment(f, LeaderAlignmentAxis.Horizontal, LeaderAlignmentSource.TeighaMLeader);
        private void ExecuteAlignTeighaMLeadersVertical(DesignToolFeatureViewModel f) => ExecuteLeaderAlignment(f, LeaderAlignmentAxis.Vertical, LeaderAlignmentSource.TeighaMLeader);
        private void ExecuteDistributeMultiCadLeadersHorizontal(DesignToolFeatureViewModel f) => ExecuteLeaderDistribution(f, LeaderAlignmentAxis.Horizontal, LeaderAlignmentSource.MultiCad);
        private void ExecuteDistributeMultiCadLeadersVertical(DesignToolFeatureViewModel f) => ExecuteLeaderDistribution(f, LeaderAlignmentAxis.Vertical, LeaderAlignmentSource.MultiCad);
        private void ExecuteDistributeTeighaMLeadersHorizontal(DesignToolFeatureViewModel f) => ExecuteLeaderDistribution(f, LeaderAlignmentAxis.Horizontal, LeaderAlignmentSource.TeighaMLeader);
        private void ExecuteDistributeTeighaMLeadersVertical(DesignToolFeatureViewModel f) => ExecuteLeaderDistribution(f, LeaderAlignmentAxis.Vertical, LeaderAlignmentSource.TeighaMLeader);

        private void ExecuteLeaderAlignment(DesignToolFeatureViewModel feature, LeaderAlignmentAxis axis, LeaderAlignmentSource source)
        {
            try
            {
                var result = source switch { LeaderAlignmentSource.MultiCad => _leaderAlignmentService.AlignSelectedMultiCadLeaders(axis), LeaderAlignmentSource.TeighaMLeader => _leaderAlignmentService.AlignSelectedTeighaMLeaders(axis), _ => _leaderAlignmentService.AlignSelectedLeaders(axis) };
                SetFeatureStatus(feature, result.Message); WriteToNanoCad($"\n[DesignTools]: {result.Message}\n");
            }
            catch (Exception ex) { SetFeatureStatus(feature, $"Ошибка выравнивания выносок: {ex.Message}"); }
        }

        private void ExecuteLeaderDistribution(DesignToolFeatureViewModel feature, LeaderAlignmentAxis axis, LeaderAlignmentSource source)
        {
            try
            {
                var result = source == LeaderAlignmentSource.MultiCad ? _leaderAlignmentService.DistributeSelectedMultiCadLeaders(axis) : _leaderAlignmentService.DistributeSelectedTeighaMLeaders(axis);
                SetFeatureStatus(feature, result.Message); WriteToNanoCad($"\n[DesignTools]: {result.Message}\n");
            }
            catch (Exception ex) { SetFeatureStatus(feature, $"Ошибка распределения выносок: {ex.Message}"); }
        }

        private void ExecuteSelectionDiagnostics(DesignToolFeatureViewModel feature)
        {
            try { var result = _selectionDiagnosticsService.DiagnoseSelection(); SetFeatureStatus(feature, result.Summary); WriteToNanoCad("\n" + result.Details + "\n"); }
            catch (Exception ex) { SetFeatureStatus(feature, $"Ошибка диагностики: {ex.Message}"); }
        }

        private void ExecuteSelectionInfo(DesignToolFeatureViewModel feature)
        {
            try
            {
                var doc = CadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) { SetFeatureStatus(feature, "Нет активного документа nanoCAD."); return; }
                var selection = doc.Editor.SelectImplied();
                if (selection.Status != HostMgd.EditorInput.PromptStatus.OK || selection.Value == null) { SetFeatureStatus(feature, "Нет текущего выделения."); doc.Editor.WriteMessage("\n[DesignTools]: Нет текущего выделения.\n"); return; }
                var count = selection.Value.GetObjectIds().Length;
                SetFeatureStatus(feature, $"Выделено объектов: {count}."); doc.Editor.WriteMessage($"\n[DesignTools]: Выделено объектов: {count}.\n");
            }
            catch (Exception ex) { SetFeatureStatus(feature, $"Ошибка: {ex.Message}"); }
        }

        private void ExecuteStub(DesignToolFeatureViewModel feature) => SetFeatureStatus(feature, "Каркас функции создан. Реализация будет добавлена следующим шагом.");
        private void SetFeatureStatus(DesignToolFeatureViewModel feature, string message) { feature.Status = message; Status = $"{feature.Name}: {message}"; }
        private void RefreshContext() { var doc = CadApp.DocumentManager.MdiActiveDocument; DocumentStatus = doc == null ? "Активный документ nanoCAD не найден." : $"Активный документ: {doc.Name}"; Status = "Контекст nanoCAD обновлен."; }
        private static void WriteToNanoCad(string message) { try { CadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage(message); } catch { } }
    }

    internal enum LeaderAlignmentSource { Auto, MultiCad, TeighaMLeader }
}
