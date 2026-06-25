using System.Collections.Generic;

namespace SpecStudioParser.Models
{
    /// <summary>
    /// Immutable, nanoCAD-free snapshot of a drawing object fed to the specification engine.
    /// Decouples the engine from Teigha/HostMgd types.
    /// </summary>
    public sealed class SpecSourceObject
    {
        public string Handle { get; init; } = "";
        public string ObjectName { get; init; } = "";
        public string Layer { get; init; } = "";
        public string RawObjectIdString { get; init; } = "";
        public IReadOnlyDictionary<string, string> Attributes { get; init; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}
