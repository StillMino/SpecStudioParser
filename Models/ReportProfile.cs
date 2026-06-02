using System.Collections.ObjectModel;
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

        public ObservableCollection<string> TargetTypes { get; set; } = new();
        public ObservableCollection<ReportColumnConfig> Columns { get; set; } = new();
        public ObservableCollection<FilterConditionItem> FilterConditions { get; set; } = new();

        // Будут задействованы позже при чтении сортировки/группировки из XML
        public ObservableCollection<GroupFieldConfig> GroupFields { get; set; } = new();
        public ObservableCollection<SortFieldConfig> SortFields { get; set; } = new();

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

        public string Attribute { get => _attribute; set { _attribute = value; OnPropertyChanged(); } }
        public string Operator { get => _operator; set { _operator = value; OnPropertyChanged(); } }
        public string Value { get => _value; set { _value = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class GroupFieldConfig { public string Name { get; set; } = ""; }
    public class SortFieldConfig { public string Name { get; set; } = ""; public int Direction { get; set; } = 0; }
}