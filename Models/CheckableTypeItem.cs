using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpecStudioParser.Models
{
    public class CheckableTypeItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string TypeName { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}