using SpecStudioParser.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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
            RootFilterItems.CollectionChanged += RootFilterItemsChanged;
        }

        public string Caption
        {
            get => _caption;
            set { _caption = value; OnPropertyChanged(); }
        }

        public string FilterFormula
        {
            get => _filterFormula;
            set
            {
                EnsureRootFilterItems();
                var normalized = HasEditableFilterItems()
                    ? FilterFormulaBuilder.BuildFromRoot(RootFilterGroup, FilterConditions, RootFilterItems)
                    : value;

                if (_filterFormula != normalized)
                {
                    _filterFormula = normalized;
                    OnPropertyChanged();
                }
            }
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
                _rootFilterGroup.EnsureItems();
                OnPropertyChanged();
                RebuildFilterFormula();
            }
        }

        public ObservableCollection<string> TargetTypes { get; set; } = new();
        public ObservableCollection<ReportColumnConfig> Columns { get; set; } = new();
        public ObservableCollection<FilterConditionItem> FilterConditions { get; set; } = new();
        public ObservableCollection<FilterRootItem> RootFilterItems { get; } = new();

        // Будут задействованы позже при чтении сортировки/группировки из XML
        public ObservableCollection<GroupFieldConfig> GroupFields { get; set; } = new();
        public ObservableCollection<SortFieldConfig> SortFields { get; set; } = new();

        public void EnsureRootFilterItems()
        {
            RootFilterGroup.EnsureItems();
            if (RootFilterItems.Count > 0) return;

            foreach (var condition in FilterConditions)
            {
                RootFilterItems.Add(FilterRootItem.FromCondition(condition));
            }

            foreach (var group in RootFilterGroup.Groups)
            {
                group.EnsureItems();
                RootFilterItems.Add(FilterRootItem.FromGroup(group));
            }
        }

        public void AddRootFilterCondition()
        {
            EnsureRootFilterItems();
            var condition = new FilterConditionItem
            {
                JoinWithNext = GetLastRootItemJoinWithNext()
            };
            FilterConditions.Add(condition);
            RootFilterItems.Add(FilterRootItem.FromCondition(condition));
            RebuildFilterFormula();
        }

        public void AddChildFilterGroup()
        {
            EnsureRootFilterItems();
            var group = RootFilterGroup.AddGroup();
            group.JoinWithNext = GetLastRootItemJoinWithNext();
            RootFilterItems.Add(FilterRootItem.FromGroup(group));
            RebuildFilterFormula();
        }

        public void DissolveNestedFilterGroup(FilterConditionGroup? group)
        {
            if (RootFilterGroup.DissolveGroup(group))
            {
                RebuildFilterFormula();
            }
        }

        public void PromoteNestedFilterGroup(FilterConditionGroup? group)
        {
            if (group == null) return;
            EnsureRootFilterItems();

            var parent = RootFilterGroup.FindParentOfGroup(group);
            if (parent == null || parent == RootFilterGroup)
            {
                return;
            }

            var rootParentItem = RootFilterItems.FirstOrDefault(item => item.Group == parent);
            if (rootParentItem == null)
            {
                return;
            }

            if (!parent.RemoveGroup(group))
            {
                return;
            }

            var insertIndex = RootFilterItems.IndexOf(rootParentItem) + 1;
            if (insertIndex < 0 || insertIndex > RootFilterItems.Count)
            {
                insertIndex = RootFilterItems.Count;
            }

            if (!RootFilterItems.Any(item => item.Group == group))
            {
                RootFilterItems.Insert(insertIndex, FilterRootItem.FromGroup(group));
            }

            if (!RootFilterGroup.Items.Any(item => item.Group == group))
            {
                RootFilterGroup.Items.Add(FilterGroupItem.FromGroup(group));
            }

            RebuildFilterFormula();
        }

        public void MoveNestedFilterGroupItem(FilterGroupItem? item, int direction)
        {
            if (RootFilterGroup.MoveItem(item, direction))
            {
                RebuildFilterFormula();
            }
        }

        public void GroupSelectedNestedFilterItems(FilterConditionGroup? group)
        {
            if (group == null) return;

            if (group.GroupSelectedItems())
            {
                RebuildFilterFormula();
            }
        }

        private string GetLastRootItemJoinWithNext()
        {
            var lastItem = RootFilterItems.LastOrDefault();
            if (lastItem?.Condition != null)
            {
                return lastItem.Condition.JoinWithNext;
            }

            if (lastItem?.Group != null)
            {
                return lastItem.Group.JoinWithNext;
            }

            return "and";
        }

        public void RemoveRootFilterItem(FilterRootItem? item)
        {
            if (item == null) return;

            if (item.Condition != null)
            {
                FilterConditions.Remove(item.Condition);
            }

            if (item.Group != null)
            {
                RootFilterGroup.RemoveGroup(item.Group);
            }

            RootFilterItems.Remove(item);
            RebuildFilterFormula();
        }

        public void RemoveFilterCondition(FilterConditionItem? condition)
        {
            if (condition == null) return;

            FilterConditions.Remove(condition);
            RootFilterGroup.RemoveCondition(condition);

            var rootItem = RootFilterItems.FirstOrDefault(item => item.Condition == condition);
            if (rootItem != null)
            {
                RootFilterItems.Remove(rootItem);
            }

            RebuildFilterFormula();
        }

        private void RootFilterItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildFilterFormula();
        }

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
            RebuildFilterFormula();
        }

        private bool HasEditableFilterItems()
        {
            return RootFilterItems.Count > 0 || FilterConditions.Count > 0 || RootFilterGroup.Groups.Count > 0 || RootFilterGroup.Conditions.Count > 0 || RootFilterGroup.Items.Count > 0;
        }

        private void RebuildFilterFormula()
        {
            var formula = FilterFormulaBuilder.BuildFromRoot(RootFilterGroup, FilterConditions, RootFilterItems);
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

    public class FilterRootItem
    {
        private FilterRootItem(FilterConditionItem? condition, FilterConditionGroup? group)
        {
            Condition = condition;
            Group = group;
        }

        public FilterConditionItem? Condition { get; }
        public FilterConditionGroup? Group { get; }
        public bool IsCondition => Condition != null;
        public bool IsGroup => Group != null;

        public static FilterRootItem FromCondition(FilterConditionItem condition) => new(condition, null);
        public static FilterRootItem FromGroup(FilterConditionGroup group) => new(null, group);
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
        private string _joinWithNext = "and";

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
        public string JoinWithNext { get => _joinWithNext; set { _joinWithNext = value; OnPropertyChanged(); } }

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

        public ObservableCollection<string> AvailableJoinOperators { get; } = new()
        {
            "and",
            "or"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class GroupFieldConfig { public string Name { get; set; } = ""; }
    public class SortFieldConfig { public string Name { get; set; } = ""; public int Direction { get; set; } = 0; }
}
