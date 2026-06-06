using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SpecStudioParser.CadLib
{
    public sealed class CadLibParameterListItemViewModel : INotifyPropertyChanged
    {
        private readonly Func<bool> _useSystemNames;
        private bool _isSelected;

        public CadLibParameterListItemViewModel(CadLibParameterInfo parameter, Func<bool> useSystemNames)
        {
            Parameter = parameter;
            _useSystemNames = useSystemNames;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public CadLibParameterInfo Parameter { get; }
        public string SystemName => Parameter.SystemName;
        public string DisplayName => Parameter.DisplayName;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string DisplayText => _useSystemNames()
            ? string.IsNullOrWhiteSpace(Parameter.SystemName) ? Parameter.DisplayName : Parameter.SystemName
            : string.IsNullOrWhiteSpace(Parameter.DisplayName) ? Parameter.SystemName : Parameter.DisplayName;

        public string DetailText
        {
            get
            {
                var system = string.IsNullOrWhiteSpace(Parameter.SystemName) ? "-" : Parameter.SystemName;
                var display = string.IsNullOrWhiteSpace(Parameter.DisplayName) ? "-" : Parameter.DisplayName;
                return _useSystemNames() ? display : system;
            }
        }

        public string SearchText => $"{Parameter.SystemName} {Parameter.DisplayName}".ToLowerInvariant();
        public void RefreshText() { OnPropertyChanged(nameof(DisplayText)); OnPropertyChanged(nameof(DetailText)); }
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class CadLibParameterGroupViewModel : INotifyPropertyChanged
    {
        private bool _isExpanded = true;
        public CadLibParameterGroupViewModel(string name) { Name = name; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Name { get; }
        public string Caption => $"{Name} ({Parameters.Count})";
        public ObservableCollection<CadLibParameterListItemViewModel> Parameters { get; } = new();
        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
        public void RefreshCaption() => OnPropertyChanged(nameof(Caption));
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class CadLibParameterPickerViewModel : INotifyPropertyChanged
    {
        private readonly CadLibParameterPickerOptions _options;
        private readonly HashSet<string> _selectedSystemNames = new(StringComparer.OrdinalIgnoreCase);
        private string _searchText = string.Empty;
        private bool _sortAscending = true;
        private bool _showSystemNames = true;
        private CadLibParameterInfo? _selectedParameter;
        private string _status = string.Empty;

        public CadLibParameterPickerViewModel() : this(new CadLibParameterPickerOptions()) { }

        public CadLibParameterPickerViewModel(CadLibParameterPickerOptions options)
        {
            _options = options;
            foreach (var name in options.PreselectedSystemNames.Where(n => !string.IsNullOrWhiteSpace(n)))
                _selectedSystemNames.Add(name);

            ToggleSortCommand = new RelayCommand(ToggleSort);
            ExpandAllCommand = new RelayCommand(() => SetGroupsExpanded(true));
            CollapseAllCommand = new RelayCommand(() => SetGroupsExpanded(false));
            ToggleNameModeCommand = new RelayCommand(ToggleNameMode);
            ClearSelectionCommand = new RelayCommand(ClearSelection, () => SelectedCount > 0);
            SelectCommand = new RelayCommand(CloseWithSelection, CanSelect);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(null));
            ApplyFilter();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<CadLibParameterPickerResult?>? RequestClose;
        public ObservableCollection<CadLibParameterGroupViewModel> Groups { get; } = new();
        public ICommand ToggleSortCommand { get; }
        public ICommand ExpandAllCommand { get; }
        public ICommand CollapseAllCommand { get; }
        public ICommand ToggleNameModeCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand SelectCommand { get; }
        public ICommand CloseCommand { get; }
        public bool IsMultipleMode => _options.Mode == CadLibParameterPickerMode.Multiple;
        public string WindowTitle => _options.Title;
        public string Hint => _options.Hint;
        public bool HasHint => !string.IsNullOrWhiteSpace(Hint);
        public int SelectedCount => IsMultipleMode ? _selectedSystemNames.Count : SelectedParameter == null ? 0 : 1;

        public string SearchText { get => _searchText; set { _searchText = value ?? string.Empty; OnPropertyChanged(); ApplyFilter(); } }
        public bool ShowSystemNames { get => _showSystemNames; private set { _showSystemNames = value; OnPropertyChanged(); OnPropertyChanged(nameof(NameModeButtonText)); RefreshItemText(); } }
        public string NameModeButtonText => ShowSystemNames ? "Системные имена" : "Отображаемые имена";
        public string SortButtonText => _sortAscending ? "А → Я" : "Я → А";

        public CadLibParameterInfo? SelectedParameter
        {
            get => _selectedParameter;
            set
            {
                _selectedParameter = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedParameterText));
                OnPropertyChanged(nameof(SelectedCount));
                NotifySelectionCommands();
            }
        }

        public string SelectedParameterText => IsMultipleMode
            ? $"Выбрано: {SelectedCount}"
            : SelectedParameter == null ? "Параметр не выбран" : $"Выбран: {Safe(SelectedParameter.DisplayName)} / {Safe(SelectedParameter.SystemName)}";

        public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

        public void SelectParameter(CadLibParameterInfo? parameter)
        {
            if (parameter == null) return;
            if (IsMultipleMode)
            {
                var key = parameter.SystemName;
                if (string.IsNullOrWhiteSpace(key)) key = parameter.IdParamDef.ToString();
                if (!_selectedSystemNames.Add(key)) _selectedSystemNames.Remove(key);
                foreach (var item in Groups.SelectMany(g => g.Parameters).Where(i => ReferenceEquals(i.Parameter, parameter)))
                    item.IsSelected = _selectedSystemNames.Contains(key);
                OnPropertyChanged(nameof(SelectedParameterText));
                OnPropertyChanged(nameof(SelectedCount));
                NotifySelectionCommands();
            }
            else
            {
                SelectedParameter = parameter;
                if (_options.CloseAfterSingleSelection) CloseWithSelection();
            }
        }

        private void ToggleSort() { _sortAscending = !_sortAscending; OnPropertyChanged(nameof(SortButtonText)); ApplyFilter(); }
        private void ToggleNameMode() { ShowSystemNames = !ShowSystemNames; }
        private void SetGroupsExpanded(bool isExpanded) { foreach (var group in Groups) group.IsExpanded = isExpanded; }
        private void ClearSelection() { _selectedSystemNames.Clear(); SelectedParameter = null; foreach (var i in Groups.SelectMany(g => g.Parameters)) i.IsSelected = false; NotifySelectionCommands(); }
        private bool CanSelect() => SelectedCount > 0;
        private void CloseWithSelection()
        {
            var selected = IsMultipleMode
                ? CadLibParameterCache.Current.Parameters.Where(p => _selectedSystemNames.Contains(string.IsNullOrWhiteSpace(p.SystemName) ? p.IdParamDef.ToString() : p.SystemName)).ToList()
                : SelectedParameter == null ? new List<CadLibParameterInfo>() : new List<CadLibParameterInfo> { SelectedParameter };
            RequestClose?.Invoke(new CadLibParameterPickerResult { SelectedParameters = selected });
        }

        private void ApplyFilter()
        {
            Groups.Clear();
            var parameters = CadLibParameterCache.Current.Parameters.ToList();
            var search = SearchText.Trim().ToLowerInvariant();
            var grouped = parameters.GroupBy(p => string.IsNullOrWhiteSpace(p.CategoryName) ? "Без категории" : p.CategoryName);
            var orderedGroups = _sortAscending ? grouped.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase) : grouped.OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase);
            var visibleParameterCount = 0;

            foreach (var sourceGroup in orderedGroups)
            {
                var groupMatchesSearch = string.IsNullOrWhiteSpace(search) || sourceGroup.Key.ToLowerInvariant().Contains(search);
                var group = new CadLibParameterGroupViewModel(sourceGroup.Key);
                var orderedParameters = _sortAscending ? sourceGroup.OrderBy(GetSortKey, StringComparer.OrdinalIgnoreCase) : sourceGroup.OrderByDescending(GetSortKey, StringComparer.OrdinalIgnoreCase);
                foreach (var parameter in orderedParameters)
                {
                    var item = new CadLibParameterListItemViewModel(parameter, () => ShowSystemNames);
                    var key = string.IsNullOrWhiteSpace(parameter.SystemName) ? parameter.IdParamDef.ToString() : parameter.SystemName;
                    item.IsSelected = _selectedSystemNames.Contains(key);
                    if (groupMatchesSearch || string.IsNullOrWhiteSpace(search) || item.SearchText.Contains(search)) group.Parameters.Add(item);
                }
                if (group.Parameters.Count == 0) continue;
                group.IsExpanded = true; group.RefreshCaption(); Groups.Add(group); visibleParameterCount += group.Parameters.Count;
            }

            if (!IsMultipleMode && SelectedParameter != null && !Groups.SelectMany(g => g.Parameters).Any(i => ReferenceEquals(i.Parameter, SelectedParameter))) SelectedParameter = null;
            Status = CadLibParameterCache.Current.Parameters.Count == 0 ? "CADLib параметры не загружены. Сначала выполните SPEC_DB_CONNECT." : $"Показано параметров: {visibleParameterCount} из {CadLibParameterCache.Current.Parameters.Count}";
            NotifySelectionCommands();
        }

        private void RefreshItemText() { foreach (var item in Groups.SelectMany(g => g.Parameters)) item.RefreshText(); ApplyFilter(); }
        private string GetSortKey(CadLibParameterInfo p) => ShowSystemNames ? string.IsNullOrWhiteSpace(p.SystemName) ? p.DisplayName : p.SystemName : string.IsNullOrWhiteSpace(p.DisplayName) ? p.SystemName : p.DisplayName;
        private static string Safe(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
        private void NotifySelectionCommands() { (SelectCommand as RelayCommand)?.NotifyCanExecuteChanged(); (ClearSelectionCommand as RelayCommand)?.NotifyCanExecuteChanged(); }
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
