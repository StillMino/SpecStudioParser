using SpecStudioParser.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpecStudioParser.Models
{
    public class ReportProfile : INotifyPropertyChanged
    {
        private string _name = "Новый профиль";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public ObservableCollection<DatasetConfig> Datasets { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DatasetConfig : INotifyPropertyChanged
    {
        private string _caption = "Новый набор данных";
        private string _filterFormula = "";
        private int _aggregated = 1;
        private FilterConditionGroup _rootFilterGroup = new();

        public DatasetConfig()
        {
            FilterConditions.CollectionChanged += FilterConditionsChanged;
            RootFilterGroup.PropertyChanged += RootFilterGroupChanged;
        }

        public string Caption
        {
            get => _caption;
            set { _caption = value; OnPropertyChanged(); }
        }

        public string FilterFormula
        {
            get => _filterFormula;
            set { _filterFormula = value; OnPropertyChanged(); }
        }

        public int Aggregated
        {
            get => _aggregated;
            set { _aggregated = value; OnPropertyChanged(); }
        }

        public FilterConditionGroup RootFilterGroup
        {
            get => _rootFilterGroup;
            set
            {
                if (_rootFilterGroup != null)
                {
                    _rootFilterGroup.PropertyChanged -= RootFilterGroupChanged;
                }

                _rootFilterGroup = value ?? new FilterConditionGroup();
                _rootFilterGroup.PropertyChanged += RootFilterGroupChanged;
                OnPropertyChanged();
                RebuildFilterFormula();
            }
        }

        public ObservableCollection<string> TargetTypes { get; set; } = new();
        public ObservableCollection<ReportColumnConfig> Columns { get; set; } = new();
        public ObservableCollection<FilterConditionItem> FilterConditions { get; set; } = new();

        // Будут задействованы позже при чтении сортировки/группировки из XML
        public ObservableCollection<GroupFieldConfig> GroupFields { get; set; } = new();
        public ObservableCollection<SortFieldConfig> SortFields { get; set; } = new();

        private void FilterConditionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (FilterConditionItem item in e.OldItems)
                {
                    item.PropertyChanged -= FilterConditionChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (FilterConditionItem item in e.NewItems)
                {
                    item.PropertyChanged += FilterConditionChanged;
                }
            }

            RebuildFilterFormula();
        }

        private void FilterConditionChanged(object? sender, PropertyChangedEventArgs e)
        {
            RebuildFilterFormula();
        }

        private void RootFilterGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FilterConditionGroup.JoinOperator))
            {
                RebuildFilterFormula();
            }
        }

        private void RebuildFilterFormula()
        {
            var formula = FilterFormulaBuilder.BuildFromFlatConditions(FilterConditions, RootFilterGroup.JoinOperator);
            if (_filterFormula != formula)
            {
                _filterFormula = formula;
                OnPropertyChanged(nameof(FilterFormula));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ReportColumnConfig : INotifyPropertyChanged
    {
        private string _caption = "";
        private string _dataFormula = "";
        private int _displayIndex;
        private int _visible = 1;
        private int _aggregate = 0;

        public string Caption { get => _caption; set { _caption = value; OnPropertyChanged(); } }
        public string DataFormula { get => _dataFormula; set { _dataFormula = value; OnPropertyChanged(); } }
        public int DisplayIndex { get => _displayIndex; set { _displayIndex = value; OnPropertyChanged(); } }
        public int Visible { get => _visible; set { _visible = value; OnPropertyChanged(); } }
        public int Aggregate { get => _aggregate; set { _aggregate = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class FilterConditionItem : INotifyPropertyChanged
    {
        private string _attribute = "PART_NAME";
        private string _operator = "=";
        private string _value = "";

        public FilterConditionItem()
        {
            foreach (var attribute in new[]
            {
                "PART_NAME",
                "PART_TYPE",
                "PART_TAG",
                "PART_MATERIAL",
                "PART_STANDARD",
                "PART_WEIGHT",
                "PART_MANUFACTURER",
                "BOM_INCLUDE",
                "BOM_GROUP",
                "BOM_NUMBER",
                "SYS_OBJECT_CATEGORY",
                "EXPLICATION_NUMBER",
                "AEC_ACCESSORY",
                "AEC_STEEL_GROUP",
                "STEEL_PROF_HEIGHT",
                "DIM_HEIGHT",
                "DIM_WIDTH",
                "DIM_LENGTH",
                "CENTROID_POINT_X",
                "CENTROID_POINT_Y",
                "CENTROID_POINT_Z",
                "Layer",
                "Handle",
                "ObjectName",
                "level",
                "sort"
            })
            {
                AvailableAttributes.Add(attribute);
            }
        }

        public string Attribute { get => _attribute; set { _attribute = value; OnPropertyChanged(); } }
        public string Operator { get => _operator; set { _operator = value; OnPropertyChanged(); } }
        public string Value { get => _value; set { _value = value; OnPropertyChanged(); } }

        public ObservableCollection<string> AvailableAttributes { get; } = new();
        public ObservableCollection<string> AvailableOperators { get; } = new()
        {
            "=",
            "!=",
            "gt",
            "lt",
            "gte",
            "lte",
            "like",
            "not like",
            "contains",
            "not contains",
            "isset",
            "not isset"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class GroupFieldConfig { public string Name { get; set; } = ""; }
    public class SortFieldConfig { public string Name { get; set; } = ""; public int Direction { get; set; } = 0; }
}