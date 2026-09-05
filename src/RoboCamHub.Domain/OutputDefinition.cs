namespace RoboCamHub.Domain;

public sealed record OutputDefinition
{
    public OutputDefinition(
        string id,
        string name,
        string ndiSourceName,
        string viewId,
        bool enabled = true)
    {
        Id = DefinitionValidation.StableId(id, nameof(id), "Output ID");
        Name = DefinitionValidation.Required(name, nameof(name), "Output name");
        NdiSourceName = DefinitionValidation.StableId(
            ndiSourceName,
            nameof(ndiSourceName),
            "NDI source name");
        ViewId = DefinitionValidation.StableId(viewId, nameof(viewId), "Referenced View ID");
        Enabled = enabled;
    }

    public string Id { get; }

    public string Name { get; }

    public string NdiSourceName { get; }

    public string ViewId { get; }

    public bool Enabled { get; }
}
