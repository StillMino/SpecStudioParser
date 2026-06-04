using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpecStudioParser.Models
{
    public class FilterConditionGroup : INotifyPropertyChanged
    {
        private string _joinOperator = "and";

        public FilterConditionGroup()
        {
            Conditions.CollectionChanged += ConditionsChanged;
            Groups.CollectionChanged += GroupsChanged;
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

        public ObservableCollection<string> AvailableJoinOperators { get; } = new()
        {
            "and",
            "or"
        };

        public ObservableCollection<FilterConditionItem> Conditions { get; } = new();
        public ObservableCollection<FilterConditionGroup> Groups { get; } = new();

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

            OnPropertyChanged(nameof(Groups));
        }

        private void ChildConditionChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Conditions));
        }

        private void ChildGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Groups));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}