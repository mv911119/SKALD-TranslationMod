using System.Text;
using TranslationMod;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string input = ReadInput(args);
            var service = new TranslationService();
            string result = service.Process(input);

            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(result);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  ParseRunner.exe \"raw text\"");
            Console.Error.WriteLine("  ParseRunner.exe path\\to\\input.txt");
            Console.Error.WriteLine("  Get-Content path\\to\\input.txt | ParseRunner.exe");
            return 1;
        }
    }

    private static string ReadInput(string[] args)
    {
        if (args.Length > 0)
        {
            string value = string.Join(" ", args).Trim();
            if (File.Exists(value))
            {
                return File.ReadAllText(value);
            }

            return value;
        }

        if (Console.IsInputRedirected)
        {
            string stdin = Console.In.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(stdin))
            {
                return stdin;
            }
        }

        throw new InvalidOperationException("No input provided.");
    }
}

//dotnet build .\src\ParseRunner\ParseRunner.csproj -c Release
//dotnet run --project .\src\ParseRunner\ParseRunner.csproj -- .\src\ParseRunner\input_file.txt
//src\ParseRunner\bin\Release\net6.0\ParseRunner.exe .\src\ParseRunner\input_file.txt
