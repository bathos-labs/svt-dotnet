using System.Text.Json;
using Bathos.Graphics.SvgTemplates.PackageValidation;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: PackageValidation <nupkg> <snupkg> <version> <commit>");
    return 2;
}

try
{
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
