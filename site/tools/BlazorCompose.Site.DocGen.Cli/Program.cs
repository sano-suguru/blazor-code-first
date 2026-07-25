using BlazorCompose.Site.DocGen;

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "Usage: docgen <contentDir> <docsOutPath> <cssOutPath>");
    return 1;
}

DocGenRunner.Run(contentDir: args[0], docsOutPath: args[1], cssOutPath: args[2]);
Console.WriteLine($"Generated {args[1]} and {args[2]} from {args[0]}.");
return 0;
