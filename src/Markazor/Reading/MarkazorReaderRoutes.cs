using System.Globalization;

namespace Markazor.Reading;

public static class MarkazorReaderRoutes
{
    public static string Category(string value) => Taxonomy("/categories/", value);

    public static string Tag(string value) => Taxonomy("/tags/", value);

    public static string Page(string root, int number) => number <= 1 ? root : (root == "/" ? string.Empty : root) + "/page/" + number.ToString(CultureInfo.InvariantCulture);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Taxonomy route segments intentionally use lowercase canonical URLs.")]
    private static string Taxonomy(string root, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return root + Uri.EscapeDataString(value.Trim().ToLowerInvariant());
    }
}
