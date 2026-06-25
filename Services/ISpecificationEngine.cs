using System.Collections.Generic;
using SpecStudioParser.Models;

namespace SpecStudioParser.Services
{
    /// <summary>
    /// Pure specification generation engine — no UI, no Dispatcher, no nanoCAD dependencies.
    /// </summary>
    public interface ISpecificationEngine
    {
        /// <summary>
        /// Generates aggregated specification rows from a report profile and a set of source objects.
        /// </summary>
        SpecificationResult Generate(ReportProfile profile, IReadOnlyList<SpecSourceObject> sourceObjects);
    }
}
