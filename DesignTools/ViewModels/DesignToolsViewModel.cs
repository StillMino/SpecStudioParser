using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpecStudioParser.DesignTools.Commands;
using SpecStudioParser.DesignTools.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
            RunCommand = new RelayCommand(async () => await DeferredCommandRunner.RunAsync(() => execute(this)));
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

    public partial class DesignToolCardViewModel : ObservableObject
    {
        [ObservableProperty] private string _status = "Готово";
        [ObservableProperty] private string _selectedSource = string.Empty;
        [ObservableProperty] private string _selectedOperation = string.Empty;
        [ObservableProperty] private string _selectedAxis = string.Empty;
        [ObservableProperty] private string _selectedReference = string.Empty;

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string Category { get; }
        public string IconGeometry { get; }
        public ObservableCollection<string> Sources { get; } = new();
        public ObservableCollection<string> Operations { get; } = new();
        public ObservableCollection<string> Axes { get; } = new();
        public ObservableCollection<string> References { get; } = new();
        public ICommand RunCommand { get; }

        public DesignToolCardViewModel(
            string id,
            string title,
            string description,
            string category,
            string iconGeometry,
            IEnumerable<string> sources,
            IEnumerable<string> operations,
            IEnumerable<string> axes,
            IEnumerable<string> references,
            Action<DesignToolCardViewModel> execute)
        {
            Id = id;
            Title = title;
            Description = description;
            Category = category;
            IconGeometry = iconGeometry;
            foreach (var source in sources) Sources.Add(source);
            foreach (var operation in operations) Operations.Add(operation);
            foreach (var axis in axes) Axes.Add(axis);
            foreach (var reference in references) References.Add(reference);
            SelectedSource = Sources.FirstOrDefault() ?? string.Empty;
            SelectedOperation = Operations.FirstOrDefault() ?? string.Empty;
            SelectedAxis = Axes.FirstOrDefault() ?? string.Empty;
            SelectedReference = References.FirstOrDefault() ?? string.Empty;
            RunCommand = new RelayCommand(async () => await DeferredCommandRunner.RunAsync(() => execute(this)));
        }
    }

    internal static class DeferredCommandRunner
    {
        public static async Task RunAsync(Action action)
        {
            // Let Avalonia finish Button.PointerReleased / mouse capture cleanup before sending a nanoCAD command.
            await Task.Delay(150).ConfigureAwait(true);
            await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Background);
        }
    }

    public partial class DesignToolsViewModel : ObservableObject
    {
        private const string FilterAll = "all";
        private const string FilterDrafting = "drafting";
        private const string FilterDiagnostics = "diagnostics";
        private const string FilterModel = "model";
        private const string FilterSpecifier = "specifier";

        private const string LeadersIcon = "M4,18 L9,14 L13,14 L18,10 M3,20 L4,18 L6,19 M14,5 L21,5 L21,10 L14,10 Z M15.5,7.5 L19.5,7.5";
        private const string DimensionsIcon = "M4,7 H20 M6,5 L4,7 L6,9 M18,5 L20,7 L18,9 M8,15 H16 M12,12 V18";
        private const string DiagnosticsIcon = "M5,5 L19,5 L19,19 L5,19 Z M8,9 L16,9 M8,12 L16,12 M8,15 L13,15";
        private const string ModelIcon = "M5,8 L12,4 L19,8 L12,12 Z M5,8 V16 L12,20 L19,16 V8 M12,12 V20";
        private const string SpecifierIcon = "M6,4 H18 V20 H6 Z M8,8 H16 M8,12 H16 M8,16 H13";

        private readonly List<DesignToolCardViewModel> _allToolCards = new();
        private readonly DesignToolsCommandRunner _directCommandRunner = new();
        private DesignToolCardViewModel? _leadersCard;
        private DesignToolCardViewModel? _dimensionsCard;
        private DesignToolCardViewModel? _diagnosticsCard;

        [ObservableProperty] private string _status = "Инструменты проектировщика готовы к работе.";
        [ObservableProperty] private string _documentStatus = "Документ nanoCAD не проверен.";
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private string _activeFilter = FilterAll;
        [ObservableProperty] private string _activeFilterLabel = "Все";

        public ObservableCollection<DesignToolCardViewModel> ToolCards { get; } = new();
        public ObservableCollection<DesignToolBlockViewModel> Blocks { get; } = new();
        public ICommand RefreshContextCommand { get; }
        public ICommand SelectFilterCommand { get; }

        public DesignToolsViewModel()
        {
            RefreshContextCommand = new RelayCommand(RefreshContext);
            SelectFilterCommand = new RelayCommand<string>(SelectFilter);

            _leadersCard = CreateLeaderToolCard();
            _dimensionsCard = CreateDimensionToolCard();
            _diagnosticsCard = CreateDiagnosticsToolCard();

            _allToolCards.Add(_leadersCard);
            _allToolCards.Add(_dimensionsCard);
            _allToolCards.Add(_diagnosticsCard);
            _allToolCards.Add(CreateModelToolCard());
            _allToolCards.Add(CreateSpecifierToolCard());

            DesignToolsCommandStateService.ResultPublished += OnDesignToolsCommandResultPublished;

            ApplyFilter();
            RefreshContext();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        private DesignToolCardViewModel CreateLeaderToolCard()
        {
            return new DesignToolCardViewModel(
                "leaders",
                "Выноски",
                "Выравнивание и распределение MultiCAD-выносок и стандартных мультивыносок. MultiCAD-выноски нужно выбрать до запуска команды.",
                FilterDrafting,
                LeadersIcon,
                new[] { "MultiCAD", "Мультивыноски" },
                new[] { "Выровнять", "Распределить" },
                new[] { "Горизонтально", "Вертикально" },
                new[] { "Первая", "Точка" },
                ExecuteLeaderTool);
        }

        private DesignToolCardViewModel CreateDimensionToolCard()
        {
            return new DesignToolCardViewModel(
                "dimensions",
                "Размеры",
                "Управление положением текста размеров. Режим 'Точка' выравнивает TextPosition по указанной точке; режим 'Сбросить' возвращает стандартное положение текста.",
                FilterDrafting,
                DimensionsIcon,
                new[] { "Текст" },
                new[] { "Выровнять", "Распределить", "Сбросить" },
                new[] { "Горизонтально", "Вертикально" },
                new[] { "Первая", "Точка" },
                ExecuteDimensionTool);
        }

        private DesignToolCardViewModel CreateDiagnosticsToolCard()
        {
            return new DesignToolCardViewModel(
                "diagnostics",
                "Диагностика",
                "Служебный вывод сведений по выделенным объектам в командную строку nanoCAD.",
                FilterDiagnostics,
                DiagnosticsIcon,
                new[] { "Все объекты", "Размеры" },
                new[] { "Проверить" },
                new[] { "-" },
                new[] { "-" },
                ExecuteDiagnosticsTool);
        }

        private DesignToolCardViewModel CreateModelToolCard()
        {
            return new DesignToolCardViewModel(
                "model",
                "3D и Model Studio CS",
                "Каркас будущих команд проверки и анализа объектов модели.",
                FilterModel,
                ModelIcon,
                new[] { "Модель" },
                new[] { "Проверка пустых параметров" },
                new[] { "-" },
                new[] { "-" },
                ExecuteStubTool);
        }

        private DesignToolCardViewModel CreateSpecifierToolCard()
        {
            return new DesignToolCardViewModel(
                "specifier",
                "Связь со спецификатором",
                "Каркас будущих команд анализа данных спецификатора без изменения его текущей логики.",
                FilterSpecifier,
                SpecifierIcon,
                new[] { "Спецификатор" },
                new[] { "Проверка данных" },
                new[] { "-" },
                new[] { "-" },
                ExecuteStubTool);
        }

        private void SelectFilter(string? filter)
        {
            ActiveFilter = string.IsNullOrWhiteSpace(filter) ? FilterAll : filter;
            ActiveFilterLabel = ActiveFilter switch
            {
                FilterDrafting => "2D",
                FilterDiagnostics => "Диагностика",
                FilterModel => "3D",
                FilterSpecifier => "Spec",
                _ => "Все"
            };
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            ToolCards.Clear();
            var search = SearchText?.Trim() ?? string.Empty;
            foreach (var card in _allToolCards.Where(CardMatchesActiveFilter).Where(card => CardMatchesSearch(card, search)))
            {
                ToolCards.Add(card);
            }

            if (ToolCards.Count == 0)
            {
                Status = "Инструменты по текущему фильтру не найдены.";
            }
        }

        private bool CardMatchesActiveFilter(DesignToolCardViewModel card)
        {
            return ActiveFilter == FilterAll || card.Category == ActiveFilter;
        }

        private static bool CardMatchesSearch(DesignToolCardViewModel card, string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            return Contains(card.Title, search) ||
                   Contains(card.Description, search) ||
                   Contains(card.Id, search) ||
                   card.Sources.Any(value => Contains(value, search)) ||
                   card.Operations.Any(value => Contains(value, search)) ||
                   card.Axes.Any(value => Contains(value, search)) ||
                   card.References.Any(value => Contains(value, search));
        }

        private static bool Contains(string value, string search)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        private void ExecuteLeaderTool(DesignToolCardViewModel card)
        {
            var state = new DesignToolsCommandState
            {
                ToolKind = DesignToolsToolKind.Leaders,
                LeaderSource = ParseLeaderSource(card.SelectedSource),
                Operation = ParseOperation(card.SelectedOperation),
                Axis = ParseAxis(card.SelectedAxis),
                ReferenceMode = ParseReferenceMode(card.SelectedReference)
            };

            if (state.LeaderSource == DesignToolsLeaderSource.MultiCad)
            {
                RunMultiCadLeaderToolInPaletteContext(card, state);
                return;
            }

            DesignToolsCommandStateService.SetPendingState(state);
            SetCardStatus(card, "Команда передана в nanoCAD.");
            SendNanoCadCommand("DT_RUN_LEADERS_TOOL");
        }

        private void ExecuteDimensionTool(DesignToolCardViewModel card)
        {
            DesignToolsCommandStateService.SetPendingState(new DesignToolsCommandState
            {
                ToolKind = DesignToolsToolKind.Dimensions,
                Operation = ParseOperation(card.SelectedOperation),
                Axis = ParseAxis(card.SelectedAxis),
                ReferenceMode = ParseReferenceMode(card.SelectedReference)
            });

            SetCardStatus(card, "Команда передана в nanoCAD.");
            SendNanoCadCommand("DT_RUN_DIMENSIONS_TOOL");
        }

        private void ExecuteDiagnosticsTool(DesignToolCardViewModel card)
        {
            DesignToolsCommandStateService.SetPendingState(new DesignToolsCommandState
            {
                ToolKind = DesignToolsToolKind.Diagnostics,
                Operation = DesignToolsOperation.Check,
                DiagnosticsSource = card.SelectedSource == "Размеры" ? DesignToolsDiagnosticsSource.Dimensions : DesignToolsDiagnosticsSource.AllObjects
            });

            SetCardStatus(card, "Команда передана в nanoCAD.");
            SendNanoCadCommand("DT_RUN_DIAGNOSTICS_TOOL");
        }

        private void RunMultiCadLeaderToolInPaletteContext(DesignToolCardViewModel card, DesignToolsCommandState state)
        {
            try
            {
                // MultiCAD SelectionSet.CurrentSelection is the only confirmed reliable source for MultiCAD leaders.
                if (!HasCurrentMultiCadSelection())
                {
                    SetCardStatus(card, "Для MultiCAD-выносок выберите объекты до запуска команды.");
                    WriteToNanoCad("\n[DesignTools]: Для MultiCAD-выносок выберите объекты до запуска команды.\n");
                    return;
                }

                SetCardStatus(card, "Выполняется MultiCAD-команда.");
                var message = _directCommandRunner.RunLeaders(state);
                SetCardStatus(card, message);
            }
            catch (Exception ex)
            {
                SetCardStatus(card, $"Ошибка MultiCAD-команды: {ex.Message}");
            }
        }

        private void ExecuteStubTool(DesignToolCardViewModel card)
        {
            SetCardStatus(card, "Каркас инструмента создан. Реализация будет добавлена следующим шагом.");
        }

        private void OnDesignToolsCommandResultPublished(object? sender, DesignToolsCommandResultEventArgs e)
        {
            var card = e.ToolKind switch
            {
                DesignToolsToolKind.Leaders => _leadersCard,
                DesignToolsToolKind.Dimensions => _dimensionsCard,
                DesignToolsToolKind.Diagnostics => _diagnosticsCard,
                _ => null
            };

            if (card == null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() => SetCardStatus(card, e.Message));
        }

        private static LeaderAlignmentAxis ParseAxis(string value)
        {
            return value == "Вертикально" ? LeaderAlignmentAxis.Vertical : LeaderAlignmentAxis.Horizontal;
        }

        private static DesignToolsLeaderSource ParseLeaderSource(string value)
        {
            return value == "MultiCAD" ? DesignToolsLeaderSource.MultiCad : DesignToolsLeaderSource.TeighaMLeader;
        }

        private static DesignToolsOperation ParseOperation(string value)
        {
            return value switch
            {
                "Распределить" => DesignToolsOperation.Distribute,
                "Сбросить" => DesignToolsOperation.Reset,
                "Проверить" => DesignToolsOperation.Check,
                _ => DesignToolsOperation.Align
            };
        }

        private static DesignToolsReferenceMode ParseReferenceMode(string value)
        {
            return value == "Точка" ? DesignToolsReferenceMode.Point : DesignToolsReferenceMode.First;
        }

        private void SetCardStatus(DesignToolCardViewModel card, string message)
        {
            card.Status = message;
            Status = $"{card.Title}: {message}";
        }

        private void RefreshContext()
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            DocumentStatus = doc == null ? "Активный документ nanoCAD не найден." : $"Активный документ: {doc.Name}";
            Status = "Контекст nanoCAD обновлен.";
        }

        private static bool HasCurrentMultiCadSelection()
        {
            try
            {
                var objectManagerType = ResolveLoadedType("Multicad.DatabaseServices.McObjectManager");
                if (objectManagerType == null)
                {
                    return false;
                }

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
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
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

        private static void SendNanoCadCommand(string commandName)
        {
            try
            {
                var doc = CadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    return;
                }

                doc.SendStringToExecute(commandName + " ", true, false, false);
            }
            catch (Exception ex)
            {
                try { CadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage($"\n[DesignTools]: Не удалось передать команду {commandName}: {ex.Message}\n"); } catch { }
            }
        }

        private static void WriteToNanoCad(string message)
        {
            try { CadApp.DocumentManager.MdiActiveDocument?.Editor?.WriteMessage(message); } catch { }
        }
    }
}
