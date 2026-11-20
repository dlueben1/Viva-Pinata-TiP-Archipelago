using System.Reflection;
using System.Text;

namespace VivaAP.Client.Utils;

/// <summary>
/// Reads a CSV file embedded in the C# assembly
/// </summary>
internal static class EmbeddedCsvReader
{
    private const string Prefix = "VivaAP.Client.Data.";

    public static string Read(string filename)
    {
        Assembly assembly = typeof(EmbeddedCsvReader).Assembly;

        using Stream stream =
            assembly.GetManifestResourceStream(Prefix + filename)
            ?? throw new FileNotFoundException($"Embedded CSV not found: {filename}");

        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );

        return reader.ReadToEnd();
    }
}
