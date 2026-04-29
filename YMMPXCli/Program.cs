using YmmpxLib;

if (args.Length != 1)
{
    PrintUsage();
    return 1;
}

var inputPath = Path.GetFullPath(args[0]);
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"File not found: {inputPath}");
    return 2;
}

var extension = Path.GetExtension(inputPath);
if (extension.Equals(".ymmp", StringComparison.OrdinalIgnoreCase))
{
    var outputPath = Path.ChangeExtension(inputPath, ".ymmpx");
    var result = await YmmpxPackageService.CreatePackageAsync(inputPath, outputPath);
    Console.WriteLine($"Packed: {result.OutputPath}");
    Console.WriteLine($"Resources: {result.ResourceCount}");
    return 0;
}

if (extension.Equals(".ymmpx", StringComparison.OrdinalIgnoreCase))
{
    var extractBase = Path.Combine(
        Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory,
        Path.GetFileNameWithoutExtension(inputPath));
    var extractDirectory = YmmpxPackageService.GetAvailableDirectoryPath(extractBase);
    var result = YmmpxPackageService.ExtractAndRestoreProject(inputPath, extractDirectory);
    Console.WriteLine($"Extracted: {result.ExtractDirectory}");
    Console.WriteLine($"Project: {result.ProjectFilePath}");
    Console.WriteLine($"Replaced FilePath: {result.ReplacedPathCount}");
    return 0;
}

Console.Error.WriteLine($"Unsupported extension: {extension}");
PrintUsage();
return 3;

static void PrintUsage()
{
    Console.WriteLine("Usage: YMMPXCli <input.ymmp|input.ymmpx>");
    Console.WriteLine("  .ymmp  -> create .ymmpx package");
    Console.WriteLine("  .ymmpx -> extract package and restore absolute FilePath in project");
}
