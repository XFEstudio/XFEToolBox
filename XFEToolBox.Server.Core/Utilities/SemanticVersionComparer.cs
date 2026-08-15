using System.Text.RegularExpressions;

namespace XFEToolBox.Server.Core.Utilities;

public sealed partial class SemanticVersionComparer : IComparer<string>
{
    public static SemanticVersionComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        if (!TryParse(left, out var leftVersion) || !TryParse(right, out var rightVersion))
            return StringComparer.OrdinalIgnoreCase.Compare(left, right);

        var result = leftVersion.Major.CompareTo(rightVersion.Major);
        if (result != 0) return result;
        result = leftVersion.Minor.CompareTo(rightVersion.Minor);
        if (result != 0) return result;
        result = leftVersion.Patch.CompareTo(rightVersion.Patch);
        if (result != 0) return result;

        if (leftVersion.PreRelease.Length == 0 && rightVersion.PreRelease.Length == 0) return 0;
        if (leftVersion.PreRelease.Length == 0) return 1;
        if (rightVersion.PreRelease.Length == 0) return -1;

        var count = Math.Min(leftVersion.PreRelease.Length, rightVersion.PreRelease.Length);
        for (var index = 0; index < count; index++)
        {
            var leftPart = leftVersion.PreRelease[index];
            var rightPart = rightVersion.PreRelease[index];
            var leftNumeric = int.TryParse(leftPart, out var leftNumber);
            var rightNumeric = int.TryParse(rightPart, out var rightNumber);

            if (leftNumeric && rightNumeric)
                result = leftNumber.CompareTo(rightNumber);
            else if (leftNumeric)
                result = -1;
            else if (rightNumeric)
                result = 1;
            else
                result = StringComparer.Ordinal.Compare(leftPart, rightPart);

            if (result != 0) return result;
        }

        return leftVersion.PreRelease.Length.CompareTo(rightVersion.PreRelease.Length);
    }

    public static bool IsValid(string value) => TryParse(value, out _);

    private static bool TryParse(string value, out ParsedSemanticVersion version)
    {
        version = default;
        var match = SemanticVersionRegex().Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
            return false;

        var preRelease = match.Groups["pre"].Success
            ? match.Groups["pre"].Value.Split('.')
            : [];
        version = new ParsedSemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    [GeneratedRegex("^(?<major>0|[1-9]\\d*)\\.(?<minor>0|[1-9]\\d*)\\.(?<patch>0|[1-9]\\d*)(?:-(?<pre>(?:0|[1-9]\\d*|[A-Za-z-][0-9A-Za-z-]*)(?:\\.(?:0|[1-9]\\d*|[A-Za-z-][0-9A-Za-z-]*))*))?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$")]
    private static partial Regex SemanticVersionRegex();

    private readonly record struct ParsedSemanticVersion(int Major, int Minor, int Patch, string[] PreRelease);
}
