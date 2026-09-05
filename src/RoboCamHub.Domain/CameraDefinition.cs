namespace RoboCamHub.Domain;

public sealed record CameraDefinition
{
    public CameraDefinition(
        string id,
        string name,
        string rtspUrl,
        bool enabled = true,
        uint connectTimeoutMs = 10_000)
    {
        Id = DefinitionValidation.StableId(id, nameof(id), "Camera ID");
        Name = DefinitionValidation.Required(name, nameof(name), "Camera name");
        RtspUrl = ValidateRtspUrl(rtspUrl, nameof(rtspUrl));
        if (connectTimeoutMs != 0 && connectTimeoutMs is < 100 or > 120_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectTimeoutMs),
                "Camera connection timeout must be zero (native default) or between 100 and 120000 milliseconds.");
        }

        Enabled = enabled;
        ConnectTimeoutMs = connectTimeoutMs;
    }

    public string Id { get; }

    public string Name { get; }

    public string RtspUrl { get; }

    public bool Enabled { get; }

    public uint ConnectTimeoutMs { get; }

    private static string ValidateRtspUrl(string value, string parameterName)
    {
        _ = DefinitionValidation.Required(value, parameterName, "RTSP URL");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("RTSP URL must be an absolute rtsp:// URL.", parameterName);
        }

        return value;
    }
}
