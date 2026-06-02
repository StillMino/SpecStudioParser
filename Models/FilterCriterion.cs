using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SpecStudioParser.Models
{
    public partial class FilterCriterion : ObservableObject
    {
        [ObservableProperty] private string _selectedProperty = "";
        [ObservableProperty] private string _selectedCondition = "="; // "=", "!=", "Содержит", "Не содержит"
        [ObservableProperty] private string _targetValue = "";

        public ObservableCollection<string> AvailableProperties { get; set; } = new();
        public ObservableCollection<string> AvailableConditions { get; set; } = new() { "=", "!=", "Содержит", "Не содержит" };
    }
}