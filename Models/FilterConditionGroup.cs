using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SpecStudioParser.Models
{
    public class FilterConditionGroup : INotifyPropertyChanged
    {
        private string _joinOperator = "and";
        private string _joinWithNext = "and";
        private bool _syncingCollections;

        public FilterConditionGroup()
        {
            Conditions.CollectionChanged += ConditionsChanged;
            Groups.CollectionChanged += GroupsChanged;
            Items.CollectionChanged += ItemsChanged;
        }

        public string JoinOperator
        {
            get => _joinOperator;
            set
            {
                if (_joinOperator != value)
                {
                    _joinOperator = value;
                    OnPropertyChanged();
                }
            }
        }

        public string JoinWithNext
        {
            get => _joinWithNext;
            set
            {
                if (_joinWithNext != value)
                {
                    _joinWithNext = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<string> AvailableJoinOperators { get; } = new()
        {
            "and",
            "or"
        };

        public ObservableCollection<FilterConditionItem> Conditions { get; } = new();
        public ObservableCollection<FilterConditionGroup> Groups { get; } = new();

        // Canonical ordered content for the next nested-group editor.
        // Conditions and Groups are kept for backward compatibility with the current UI and imported profiles.
        public ObservableCollection<FilterGroupItem> Items { get; } = new();

        public void EnsureItems()
        {
            if (Items.Count == 0)
            {
                foreach (var condition in Conditions)
                {
                    Items.Add(FilterGroupItem.FromCondition(condition));
                }

                foreach (var group in Groups)
                {
                    group.EnsureItems();
                    Items.Add(FilterGroupItem.FromGroup(group));
                }
            }
            else
            {
                foreach (var item in Items)
                {
                    item.Group?.EnsureItems();
                }
            }
        }

        public FilterConditionItem AddCondition()
        {
            EnsureItems();
            var condition = new FilterConditionItem
            {
                JoinWithNext = GetLastItemJoinWithNext()
            };
            Items.Add(FilterGroupItem.FromCondition(condition));
            return condition;
        }

        public FilterConditionGroup AddGroup()
        {
            EnsureItems();
            var group = new FilterConditionGroup
            {
                JoinWithNext = GetLastItemJoinWithNext()
            };
            group.AddCondition();
            Items.Add(FilterGroupItem.FromGroup(group));
            return group;
        }

        private string GetLastItemJoinWithNext()
        {
            var lastItem = Items.LastOrDefault();
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

        public bool RemoveCondition(FilterConditionItem? condition)
        {
            if (condition == null) return false;
            EnsureItems();

            var item = Items.FirstOrDefault(i => i.Condition == condition);
            if (item != null)
            {
                Items.Remove(item);
                return true;
            }

            foreach (var group in Groups.ToList())
            {
                if (group.RemoveCondition(condition))
                {
                    return true;
                }
            }

            return false;
        }

        public bool RemoveGroup(FilterConditionGroup? group)
        {
            if (group == null) return false;
            EnsureItems();

            var item = Items.FirstOrDefault(i => i.Group == group);
            if (item != null)
            {
                Items.Remove(item);
                return true;
            }

            foreach (var childGroup in Groups.ToList())
            {
                if (childGroup.RemoveGroup(group))
                {
                    return true;
                }
            }

            return false;
        }

        private void ConditionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (FilterConditionItem item in e.OldItems)
                {
                    item.PropertyChanged -= ChildConditionChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (FilterConditionItem item in e.NewItems)
                {
                    item.PropertyChanged += ChildConditionChanged;
                }
            }

            SyncItemsFromCompatibilityCollections(e, isConditionCollection: true);
            OnPropertyChanged(nameof(Conditions));
        }

        private void GroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (FilterConditionGroup group in e.OldItems)
                {
                    group.PropertyChanged -= ChildGroupChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (FilterConditionGroup group in e.NewItems)
                {
                    group.PropertyChanged += ChildGroupChanged;
                }
            }

            SyncItemsFromCompatibilityCollections(e, isConditionCollection: false);
            OnPropertyChanged(nameof(Groups));
        }

        private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_syncingCollections)
            {
                try
                {
                    _syncingCollections = true;

                    if (e.OldItems != null)
                    {
                        foreach (FilterGroupItem item in e.OldItems)
                        {
                            if (item.Condition != null)
                            {
                                Conditions.Remove(item.Condition);
                            }

                            if (item.Group != null)
                            {
                                Groups.Remove(item.Group);
                            }
                        }
                    }

                    if (e.NewItems != null)
                    {
                        foreach (FilterGroupItem item in e.NewItems)
                        {
                            if (item.Condition != null && !Conditions.Contains(item.Condition))
                            {
                                Conditions.Add(item.Condition);
                            }

                            if (item.Group != null && !Groups.Contains(item.Group))
                            {
                                Groups.Add(item.Group);
                            }
                        }
                    }
                }
                finally
                {
                    _syncingCollections = false;
                }
            }

            OnPropertyChanged(nameof(Items));
        }

        private void SyncItemsFromCompatibilityCollections(NotifyCollectionChangedEventArgs e, bool isConditionCollection)
        {
            if (_syncingCollections) return;

            try
            {
                _syncingCollections = true;

                if (e.OldItems != null)
                {
                    foreach (var oldItem in e.OldItems)
                    {
                        var existing = isConditionCollection
                            ? Items.FirstOrDefault(item => item.Condition == oldItem)
                            : Items.FirstOrDefault(item => item.Group == oldItem);

                        if (existing != null)
                        {
                            Items.Remove(existing);
                        }
                    }
                }

                if (e.NewItems != null)
                {
                    foreach (var newItem in e.NewItems)
                    {
                        var exists = isConditionCollection
                            ? Items.Any(item => item.Condition == newItem)
                            : Items.Any(item => item.Group == newItem);

                        if (!exists)
                        {
                            if (isConditionCollection && newItem is FilterConditionItem condition)
                            {
                                Items.Add(FilterGroupItem.FromCondition(condition));
                            }
                            else if (!isConditionCollection && newItem is FilterConditionGroup group)
                            {
                                group.EnsureItems();
                                Items.Add(FilterGroupItem.FromGroup(group));
                            }
                        }
                    }
                }
            }
            finally
            {
                _syncingCollections = false;
            }
        }

        private void ChildConditionChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Conditions));
            OnPropertyChanged(nameof(Items));
        }

        private void ChildGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Groups));
            OnPropertyChanged(nameof(Items));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class FilterGroupItem
    {
        private FilterGroupItem(FilterConditionItem? condition, FilterConditionGroup? group)
        {
            Condition = condition;
            Group = group;
        }

        public FilterConditionItem? Condition { get; }
        public FilterConditionGroup? Group { get; }
        public bool IsCondition => Condition != null;
        public bool IsGroup => Group != null;

        public static FilterGroupItem FromCondition(FilterConditionItem condition) => new(condition, null);
        public static FilterGroupItem FromGroup(FilterConditionGroup group) => new(null, group);
    }
}
