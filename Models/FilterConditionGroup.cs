using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpecStudioParser.Models
{
    public class FilterConditionGroup : INotifyPropertyChanged
    {
        private string _joinOperator = "and";

        public string JoinOperator
        {
            get => _joinOperator;
            set { _joinOperator = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> AvailableJoinOperators { get; } = new()
        {
            "and",
            "or"
        };

        public ObservableCollection<FilterConditionItem> Conditions { get; } = new();
        public ObservableCollection<FilterConditionGroup> Groups { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}