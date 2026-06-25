using System;
using System.Collections.Generic;

namespace SpecStudioParser.Models
{
    /// <summary>
    /// Structured output from the specification engine.
    /// Row dictionaries preserve column-caption keys plus hidden __Handle / __RawObjectIdString
    /// so the existing Avalonia DataGrid bindings remain unchanged.
    /// </summary>
    public sealed class SpecificationResult
    {
        public IReadOnlyList<IReadOnlyDictionary<string, object>> Rows { get; init; }
            = Array.Empty<IReadOnlyDictionary<string, object>>();

        public IReadOnlyList<string> ColumnCaptions { get; init; }
            = Array.Empty<string>();

        public int RowCount => Rows.Count;
    }
}
