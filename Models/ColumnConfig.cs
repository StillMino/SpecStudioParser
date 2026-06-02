using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace SpecStudioParser.Models
{
    public partial class ColumnConfig : ObservableObject
    {
        public string Header { get; set; } = "";
        public string BindingPath { get; set; } = "";

        [ObservableProperty]
        private bool _isVisible = true;

        public int DisplayIndex { get; set; }
    }

    public class TableTemplate
    {
        public string Name { get; set; } = "";
        public List<string> VisibleColumns { get; set; } = new();
    }
}