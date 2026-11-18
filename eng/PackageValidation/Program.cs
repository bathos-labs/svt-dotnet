using System.Text.Json;
using Bathos.Graphics.SvgTemplates.PackageValidation;

if (args.Length != 4 && !(args.Length == 3 && args[0] == "compare-published"))
{
    Console.Error.WriteLine("Usage: PackageValidation <nupkg> <snupkg> <version> <commit>");
    Console.Error.WriteLine("       PackageValidation compare-published <candidate> <published>");
    return 2;
}

try
{
    if (args[0] == "compare-published")
    {
        PackageInspector.ComparePublished(PackageInspector.ReadArchive(args[1]), PackageInspector.ReadArchive(args[2]));
        Console.WriteLine("Published package contents match the validated candidate.");
        return 0;
    }
    var result = PackageInspector.Inspect(args[0], args[1], args[2], args[3]);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or BadImageFormatException or System.Xml.XmlException or JsonException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
