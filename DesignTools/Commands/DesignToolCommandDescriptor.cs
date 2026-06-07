namespace SpecStudioParser.DesignTools.Commands
{
    public sealed class DesignToolCommandDescriptor
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string IconPath { get; init; } = string.Empty;
        public string IconPngPath { get; init; } = string.Empty;
        public string NanoCadCommandName { get; init; } = string.Empty;
    }
}
