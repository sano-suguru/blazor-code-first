using BlazorCodeFirst.Site.DocGen;

if (args.Length != 5)
{
    Console.Error.WriteLine(
        "Usage: docgen <contentDir> <docsOutPath> <cssOutPath> <snippetsDir> <snippetsOutPath>");
    return 1;
}

DocGenRunner.Run(contentDir: args[0], docsOutPath: args[1], cssOutPath: args[2]);
SnippetGenRunner.Run(snippetsDir: args[3], outPath: args[4]);
Console.WriteLine($"Generated {args[1]}, {args[2]} and {args[4]}.");
return 0;
