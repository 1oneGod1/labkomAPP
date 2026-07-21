using LabKom.Teacher.Services;

namespace LabKom.Teacher.ViewModels;

public sealed record ClassroomGroupViewModel(
    string Name,
    IReadOnlyList<string> PcNames)
{
    public string Label => $"{Name} ({PcNames.Count} PC)";

    public static ClassroomGroupViewModel From(
        ClassroomGroupDefinition definition) =>
        new(definition.Name, definition.PcNames.ToArray());
}