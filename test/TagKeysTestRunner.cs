using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TranslationMod.Patches;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            string baseDirectory = AppContext.BaseDirectory;
            string textPath = Path.Combine(baseDirectory, "TagKeysInput1.txt");
            string keysPath = Path.Combine(baseDirectory, "TagKeysInput2.txt");

            string inputText = File.ReadAllText(textPath);
            Dictionary<string, string> keys = LoadKeys(keysPath);

            UITextBlockSetContentPatch.TooltipKeyBuffer.Clear();


            Console.WriteLine($"[TagKeys] INPUT: translated = \n'{inputText}'");
            Console.WriteLine($"[TagKeys] INPUT: ");
            foreach (var kvp in keys)
            {
                Console.WriteLine($"keys['{kvp.Key}'] = '{kvp.Value}'");
            }

            string actual = UITextBlockSetContentPatch.TagKeys(inputText, keys);

            Console.WriteLine($"[TagKeys] OUTPUT: translated = \n'{actual}'\n");

            AssertBuffer(keys);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> LoadKeys(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            const string prefix = "keys['";
            const string separator = "'] = '";

            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"无法解析键值行：{line}");
            }

            int separatorIndex = line.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex < 0 || !line.EndsWith("'", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"无法解析键值行：{line}");
            }

            string key = line.Substring(prefix.Length, separatorIndex - prefix.Length);
            int valueStart = separatorIndex + separator.Length;
            string value = line.Substring(valueStart, line.Length - valueStart - 1);

            dict[key] = value;
        }

        return dict;
    }

    private static void AssertBuffer(Dictionary<string, string> expected)
    {
        var buffer = UITextBlockSetContentPatch.TooltipKeyBuffer;

        AssertEqual(expected.Count.ToString(), buffer.Count.ToString(), "TooltipKeyBuffer 数量不符合预期。");

        foreach (var pair in expected)
        {
            if (!buffer.TryGetValue(pair.Key, out string? actualValue))
            {
                throw new InvalidOperationException($"TooltipKeyBuffer 缺少键：{pair.Key}");
            }

            AssertEqual(pair.Value, actualValue, $"TooltipKeyBuffer 键 `{pair.Key}` 映射错误。");
        }
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            string details =
                $"{message}\n" +
                "--- Expected ---\n" +
                expected + "\n" +
                "--- Actual ---\n" +
                actual;
            throw new InvalidOperationException(details);
        }
    }
}
