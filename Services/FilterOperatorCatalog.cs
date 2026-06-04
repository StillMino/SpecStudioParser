using System.Collections.ObjectModel;

namespace SpecStudioParser.Services
{
    public static class FilterOperatorCatalog
    {
        public static ObservableCollection<string> Operators { get; } = new()
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
    }
}