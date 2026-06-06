using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SpecStudioParser.CadLib
{
    public sealed class CadLibParameterListItemViewModel
    {
        private readonly Func<bool> _useSystemNames;

        public CadLibParameterListItemViewModel(CadLibParameterInfo parameter, Func<bool> useSystemNames)
        {
            Parameter = parameter;
            _useSystemNames = useSystemNames;
        }

        public CadLibParameterInfo Parameter { get; }
        public string SystemName => Parameter.SystemName;
        public string DisplayName => Parameter.DisplayName;

        public string DisplayText
        {
            get
            {
                if (_useSystemNames()) return string.IsNullOrWhiteSpace(Parameter.SystemName) ? Parameter.DisplayName : Parameter.SystemName;
                return string.IsNullOrWhiteSpace(Parameter.DisplayName) ? Parameter.SystemName : Parameter.DisplayName;
            }
        }

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
    }

    public sealed class CadLibParameterGroupViewModel : INotifyPropertyChanged
    {
        private bool _isExpanded = true;

        public CadLibParameterGroupViewModel(string name)
        {
            Name = name;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; }
        public string Caption => $"{Name} ({Parameters.Count})";
        public ObservableCollection<CadLibParameterListItemViewModel> Parameters { get; } = new();

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public void RefreshCaption()
        {
            OnPropertyChanged(nameof(Caption));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class CadLibParameterPickerViewModel : INotifyPropertyChanged
    {
        private string _searchText = string.Empty;
        private bool _sortAscending = true;
        private bool _showSystemNames = true;
        private CadLibParameterInfo? _selectedParameter;
        private string _status = string.Empty;

        public CadLibParameterPickerViewModel()
        {
            ToggleSortCommand = new RelayCommand(ToggleSort);
            ExpandAllCommand = new RelayCommand(() => SetGroupsExpanded(true));
            CollapseAllCommand = new RelayCommand(() => SetGroupsExpanded(false));
            ToggleNameModeCommand = new RelayCommand(ToggleNameMode);
            SelectCommand = new RelayCommand(() => RequestClose?.Invoke(SelectedParameter), () => SelectedParameter != null);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(null));

            ApplyFilter();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<CadLibParameterInfo?>? RequestClose;

        public ObservableCollection<CadLibParameterGroupViewModel> Groups { get; } = new();

        public ICommand ToggleSortCommand { get; }
        public ICommand ExpandAllCommand { get; }
        public ICommand CollapseAllCommand { get; }
        public ICommand ToggleNameModeCommand { get; }
        public ICommand SelectCommand { get; }
        public ICommand CloseCommand { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value ?? string.Empty;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public bool ShowSystemNames
        {
            get => _showSystemNames;
            private set
            {
                _showSystemNames = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NameModeButtonText));
                ApplyFilter();
            }
        }

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
                (SelectCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }

        public string SelectedParameterText
        {
            get
            {
                if (SelectedParameter == null) return "Параметр не выбран";
                var display = string.IsNullOrWhiteSpace(SelectedParameter.DisplayName) ? "-" : SelectedParameter.DisplayName;
                var system = string.IsNullOrWhiteSpace(SelectedParameter.SystemName) ? "-" : SelectedParameter.SystemName;
                return $"Выбран: {display} / {system}";
            }
        }

        public string Status
        {
            get => _status;
            private set { _status = value; OnPropertyChanged(); }
        }

        public void SelectParameter(CadLibParameterInfo? parameter)
        {
            SelectedParameter = parameter;
        }

        private void ToggleSort()
        {
            _sortAscending = !_sortAscending;
            OnPropertyChanged(nameof(SortButtonText));
            ApplyFilter();
        }

        private void ToggleNameMode()
        {
            ShowSystemNames = !ShowSystemNames;
        }

        private void SetGroupsExpanded(bool isExpanded)
        {
            foreach (var group in Groups)
            {
                group.IsExpanded = isExpanded;
            }
        }

        private void ApplyFilter()
        {
            Groups.Clear();

            var parameters = CadLibParameterCache.Current.Parameters.ToList();
            var search = SearchText.Trim().ToLowerInvariant();
            var grouped = parameters.GroupBy(p => string.IsNullOrWhiteSpace(p.CategoryName) ? "Без категории" : p.CategoryName);

            var orderedGroups = _sortAscending
                ? grouped.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                : grouped.OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var visibleParameterCount = 0;

            foreach (var sourceGroup in orderedGroups)
            {
                var groupMatchesSearch = string.IsNullOrWhiteSpace(search) || sourceGroup.Key.ToLowerInvariant().Contains(search);
                var group = new CadLibParameterGroupViewModel(sourceGroup.Key);

                var orderedParameters = _sortAscending
                    ? sourceGroup.OrderBy(GetSortKey, StringComparer.OrdinalIgnoreCase)
                    : sourceGroup.OrderByDescending(GetSortKey, StringComparer.OrdinalIgnoreCase);

                foreach (var parameter in orderedParameters)
                {
                    var item = new CadLibParameterListItemViewModel(parameter, () => ShowSystemNames);
                    if (groupMatchesSearch || string.IsNullOrWhiteSpace(search) || item.SearchText.Contains(search))
                    {
                        group.Parameters.Add(item);
                    }
                }

                if (group.Parameters.Count == 0) continue;

                group.IsExpanded = true;
                group.RefreshCaption();
                Groups.Add(group);
                visibleParameterCount += group.Parameters.Count;
            }

            if (SelectedParameter != null && !Groups.SelectMany(g => g.Parameters).Any(i => ReferenceEquals(i.Parameter, SelectedParameter)))
            {
                SelectedParameter = null;
            }

            Status = CadLibParameterCache.Current.Parameters.Count == 0
                ? "CADLib параметры не загружены. Сначала выполните SPEC_DB_CONNECT."
                : $"Показано параметров: {visibleParameterCount} из {CadLibParameterCache.Current.Parameters.Count}";
        }

        private string GetSortKey(CadLibParameterInfo parameter)
        {
            if (ShowSystemNames)
                return string.IsNullOrWhiteSpace(parameter.SystemName) ? parameter.DisplayName : parameter.SystemName;

            return string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.SystemName : parameter.DisplayName;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
