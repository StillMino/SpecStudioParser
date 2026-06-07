using SpecStudioParser.Services;
using System;
using System.Collections.Generic;
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
        private string _filterDiagnosticsText = "";
        private int _aggregated = 1;
        private FilterConditionGroup _rootFilterGroup = new();
        private bool _suppressFilterRebuild;
        private bool _syncingRootCollections;

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

        public string FilterDiagnosticsText
        {
            get => _filterDiagnosticsText;
            private set
            {
                if (_filterDiagnosticsText != value)
                {
                    _filterDiagnosticsText = value;
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
                EnsureRootFilterItems();
                SyncRootCompatibilityCollections();
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
            BatchUpdateRootFilters(() =>
            {
                var condition = new FilterConditionItem
                {
                    JoinWithNext = GetLastRootItemJoinWithNext()
                };
                RootFilterItems.Add(FilterRootItem.FromCondition(condition));
            });
        }

        public void AddChildFilterGroup()
        {
            BatchUpdateRootFilters(() =>
            {
                var group = new FilterConditionGroup
                {
                    JoinWithNext = GetLastRootItemJoinWithNext()
                };
                group.AddCondition();
                RootFilterItems.Add(FilterRootItem.FromGroup(group));
            });
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
            if (rootParentItem == null || !parent.RemoveGroup(group))
            {
                return;
            }

            BatchUpdateRootFilters(() =>
            {
                var insertIndex = RootFilterItems.IndexOf(rootParentItem) + 1;
                if (insertIndex < 0 || insertIndex > RootFilterItems.Count)
                {
                    insertIndex = RootFilterItems.Count;
                }

                if (!RootFilterItems.Any(item => item.Group == group))
                {
                    RootFilterItems.Insert(insertIndex, FilterRootItem.FromGroup(group));
                }
            });
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

        public void GroupSelectedRootFilterItems()
        {
            EnsureRootFilterItems();

            var selectedItems = RootFilterItems
                .Where(item => item.IsSelected)
                .ToList();

            if (selectedItems.Count < 2)
            {
                return;
            }

            var selectedIndexes = selectedItems
                .Select(item => RootFilterItems.IndexOf(item))
                .OrderBy(index => index)
                .ToList();

            if (selectedIndexes.Any(index => index < 0))
            {
                return;
            }

            for (var i = 1; i < selectedIndexes.Count; i++)
            {
                if (selectedIndexes[i] != selectedIndexes[i - 1] + 1)
                {
                    return;
                }
            }

            var insertIndex = selectedIndexes[0];
            var groupedRootItems = selectedIndexes
                .Select(index => RootFilterItems[index])
                .ToList();

            var newGroup = new FilterConditionGroup
            {
                JoinWithNext = GetRootItemJoinWithNext(groupedRootItems.Last())
            };

            foreach (var item in groupedRootItems)
            {
                item.IsSelected = false;

                if (item.Condition != null)
                {
                    newGroup.Items.Add(FilterGroupItem.FromCondition(item.Condition));
                }

                if (item.Group != null)
                {
                    item.Group.EnsureItems();
                    newGroup.Items.Add(FilterGroupItem.FromGroup(item.Group));
                }
            }

            BatchUpdateRootFilters(() =>
            {
                for (var i = groupedRootItems.Count - 1; i >= 0; i--)
                {
                    RootFilterItems.Remove(groupedRootItems[i]);
                }

                RootFilterItems.Insert(insertIndex, FilterRootItem.FromGroup(newGroup));
            });
        }

        public void RunFilterIntegrityDiagnostics()
        {
            var before = CollectRootFilterIntegrityIssues();
            var migratedLegacyState = RootFilterItems.Count == 0 && HasEditableFilterItems();

            if (migratedLegacyState)
            {
                EnsureRootFilterItems();
            }

            if (before.Any() || migratedLegacyState)
            {
                SyncRootCompatibilityCollections();
            }

            var after = CollectRootFilterIntegrityIssues();
            if (!after.Any())
            {
                FilterDiagnosticsText = before.Any() || migratedLegacyState
                    ? $"OK: структура фильтров синхронизирована. Исправлено расхождений: {before.Count}."
                    : $"OK: структура фильтров согласована. Корневых элементов: {RootFilterItems.Count}.";
                return;
            }

            FilterDiagnosticsText = "Найдены расхождения структуры фильтров:\n- " + string.Join("\n- ", after);
        }

        public IReadOnlyList<string> CollectRootFilterIntegrityIssues()
        {
            var issues = new List<string>();
            RootFilterGroup.EnsureItems();

            var rootConditions = RootFilterItems
                .Where(item => item.Condition != null)
                .Select(item => item.Condition!)
                .ToList();
            var rootGroups = RootFilterItems
                .Where(item => item.Group != null)
                .Select(item => item.Group!)
                .ToList();

            if (!FilterConditions.SequenceEqual(rootConditions))
            {
                issues.Add($"FilterConditions не соответствует корневым условиям: FilterConditions={FilterConditions.Count}, RootFilterItems conditions={rootConditions.Count}.");
            }

            if (RootFilterGroup.Items.Count != RootFilterItems.Count)
            {
                issues.Add($"RootFilterGroup.Items не соответствует RootFilterItems по количеству: Items={RootFilterGroup.Items.Count}, RootFilterItems={RootFilterItems.Count}.");
            }
            else
            {
                for (var i = 0; i < RootFilterItems.Count; i++)
                {
                    if (!MatchesRootItem(RootFilterItems[i], RootFilterGroup.Items[i]))
                    {
                        issues.Add($"RootFilterGroup.Items[{i}] не соответствует RootFilterItems[{i}].");
                        break;
                    }
                }
            }

            var legacyConditionCount = RootFilterGroup.Conditions.Count;
            var legacyGroupCount = RootFilterGroup.Groups.Count;
            if (RootFilterItems.Count > 0 && (legacyConditionCount > 0 || legacyGroupCount > 0))
            {
                issues.Add($"В legacy-коллекциях RootFilterGroup есть дубли: Conditions={legacyConditionCount}, Groups={legacyGroupCount}.");
            }

            var duplicateConditions = rootConditions
                .GroupBy(condition => condition)
                .Where(group => group.Count() > 1)
                .Count();
            var duplicateGroups = rootGroups
                .GroupBy(group => group)
                .Where(group => group.Count() > 1)
                .Count();
            if (duplicateConditions > 0 || duplicateGroups > 0)
            {
                issues.Add($"В RootFilterItems есть повторяющиеся ссылки: conditions={duplicateConditions}, groups={duplicateGroups}.");
            }

            return issues;
        }

        private static bool MatchesRootItem(FilterRootItem rootItem, FilterGroupItem groupItem)
        {
            if (rootItem.Condition != null)
            {
                return ReferenceEquals(rootItem.Condition, groupItem.Condition);
            }

            if (rootItem.Group != null)
            {
                return ReferenceEquals(rootItem.Group, groupItem.Group);
            }

            return false;
        }

        private void BatchUpdateRootFilters(Action update)
        {
            try
            {
                _suppressFilterRebuild = true;
                EnsureRootFilterItems();
                update();
                SyncRootCompatibilityCollections();
            }
            finally
            {
                _suppressFilterRebuild = false;
            }

            RebuildFilterFormula();
        }

        private void SyncRootCompatibilityCollections()
        {
            if (_syncingRootCollections) return;

            try
            {
                _syncingRootCollections = true;
                _suppressFilterRebuild = true;

                FilterConditions.Clear();
                RootFilterGroup.Items.Clear();
                RootFilterGroup.Conditions.Clear();
                RootFilterGroup.Groups.Clear();

                foreach (var item in RootFilterItems)
                {
                    if (item.Condition != null)
                    {
                        FilterConditions.Add(item.Condition);
                        RootFilterGroup.Items.Add(FilterGroupItem.FromCondition(item.Condition));
                    }

                    if (item.Group != null)
                    {
                        item.Group.EnsureItems();
                        RootFilterGroup.Items.Add(FilterGroupItem.FromGroup(item.Group));
                    }
                }
            }
            finally
            {
                _syncingRootCollections = false;
                _suppressFilterRebuild = false;
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

        private static string GetRootItemJoinWithNext(FilterRootItem item)
        {
            if (item.Condition != null)
            {
                return item.Condition.JoinWithNext;
            }

            if (item.Group != null)
            {
                return item.Group.JoinWithNext;
            }

            return "and";
        }

        public void RemoveRootFilterItem(FilterRootItem? item)
        {
            if (item == null) return;

            BatchUpdateRootFilters(() => RootFilterItems.Remove(item));
        }

        public void RemoveFilterCondition(FilterConditionItem? condition)
        {
            if (condition == null) return;

            BatchUpdateRootFilters(() =>
            {
                var rootItem = RootFilterItems.FirstOrDefault(item => item.Condition == condition);
                if (rootItem != null)
                {
                    RootFilterItems.Remove(rootItem);
                    return;
                }

                RootFilterGroup.RemoveCondition(condition);
            });
        }

        private void RootFilterItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressFilterRebuild || _syncingRootCollections) return;
            SyncRootCompatibilityCollections();
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

            if (_suppressFilterRebuild || _syncingRootCollections) return;
            RebuildFilterFormula();
        }

        private void FilterConditionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressFilterRebuild) return;
            RebuildFilterFormula();
        }

        private void RootFilterGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressFilterRebuild || _syncingRootCollections) return;
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

    public class FilterRootItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        private FilterRootItem(FilterConditionItem? condition, FilterConditionGroup? group)
        {
            Condition = condition;
            Group = group;
        }

        public FilterConditionItem? Condition { get; }
        public FilterConditionGroup? Group { get; }
        public bool IsCondition => Condition != null;
        public bool IsGroup => Group != null;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public static FilterRootItem FromCondition(FilterConditionItem condition) => new(condition, null);
        public static FilterRootItem FromGroup(FilterConditionGroup group) => new(null, group);

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
