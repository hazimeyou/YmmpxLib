namespace YmmpxLibV2;

internal static class ProjectFileNameValidator
{
    public static string Validate(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.EndsWith(".ymmp", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(value) || value.Contains("..", StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
        {
            throw new ArgumentException("Project filename must be a safe .ymmp filename.", parameterName);
        }
        return value;
    }
}
