// UNNAMED Content Validation CLI Entry Point
namespace UNNAMED.Content.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }
        
        string command = args[0].ToLowerInvariant();
        string[] remainingArgs = args.Skip(1).ToArray();
        
        string contentRoot = "./content";
        string format = "text";
        
        // Parse options
        for (int i = 0; i < remainingArgs.Length; i++)
        {
            if (remainingArgs[i] == "-c" || remainingArgs[i] == "--content-root")
            {
                if (i + 1 < remainingArgs.Length)
                {
                    contentRoot = remainingArgs[i + 1];
                    i++;
                }
            }
            else if (remainingArgs[i] == "-f" || remainingArgs[i] == "--format")
            {
                if (i + 1 < remainingArgs.Length)
                {
                    format = remainingArgs[i + 1];
                    i++;
                }
            }
        }
        
        switch (command)
        {
            case "lint":
                return ContentCliApp.LintContent(contentRoot, format);
            case "schema-dump":
                return ContentCliApp.SchemaDump(contentRoot, format);
            case "xref":
                return ContentCliApp.CrossReferenceReport(contentRoot, format);
            case "--help":
            case "-h":
            case "help":
                ShowHelp();
                return 0;
            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                ShowHelp();
                return 1;
        }
    }
    
    private static void ShowHelp()
    {
        Console.WriteLine("UNNAMED Content Validation Tool");
        Console.WriteLine("===============================");
        Console.WriteLine();
        Console.WriteLine("Usage: content <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  lint           Validate all content files");
        Console.WriteLine("  schema-dump    Dump schema information for content kinds");
        Console.WriteLine("  xref           Generate cross-reference report");
        Console.WriteLine("  help           Show this help message");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -c, --content-root <path>  Root path to content directory (default: ./content)");
        Console.WriteLine("  -f, --format <format>      Output format: text, json (default: text)");
    }
}
