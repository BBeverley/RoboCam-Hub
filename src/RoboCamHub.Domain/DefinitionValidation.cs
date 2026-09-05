using System.Text;

namespace RoboCamHub.Domain;

internal static class DefinitionValidation
{
    public static string Required(string value, string parameterName, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{description} must not be empty.", parameterName);
        }

        return value;
    }

    public static string StableId(string value, string parameterName, string description)
    {
        _ = Required(value, parameterName, description);
        if (Encoding.UTF8.GetByteCount(value) > 255)
        {
            throw new ArgumentException($"{description} must not exceed 255 UTF-8 bytes.", parameterName);
        }

        return value;
    }

    public static string? OptionalStableId(string? value, string parameterName, string description)
        => value is null ? null : StableId(value, parameterName, description);
}
