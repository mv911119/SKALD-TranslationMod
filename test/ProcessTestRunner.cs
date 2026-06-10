using System.Text;
using TranslationMod;

internal static class Program
{
    private static int Main()
    {
        try
        {
            string input = ReadInputFile();
            var service = new TranslationService();
            string result = service.Process(input);

            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(result);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string ReadInputFile()
    {
        string inputPath = Path.Combine(AppContext.BaseDirectory, "ProcessInput.txt");
        if (!File.Exists(inputPath))
        {
            throw new InvalidOperationException($"Input file not found: {inputPath}");
        }

        return File.ReadAllText(inputPath);
    }
}
