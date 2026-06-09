using System;
using System.IO;
using TranslationMod.Configuration;

namespace TranslationMod
{
    internal sealed class DebugLogger
    {
        public void LogDebug(string message) => Console.Error.WriteLine(message);
        public void LogInfo(string message) => Console.Error.WriteLine(message);
        public void LogWarning(string message) => Console.Error.WriteLine(message);
        public void LogError(string message) => Console.Error.WriteLine(message);
    }

    public static class TranslationMod
    {
        internal static DebugLogger Logger { get; } = new();
    }

    public static class LanguageManager
    {
        private static readonly LanguagePack CurrentLanguagePack = CreateDefaultLanguagePack();

        public static LanguagePack GetCurrentLanguagePack()
        {
            return CurrentLanguagePack;
        }

        private static LanguagePack CreateDefaultLanguagePack()
        {
            string root = FindRepositoryRoot();
            string packPath = Path.Combine(root, "languages", "Chinese");
            return new LanguagePack(packPath, "translations", "Chinese");
        }

        private static string FindRepositoryRoot()
        {
            string current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "languages")) &&
                    Directory.Exists(Path.Combine(current, "src")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            return Directory.GetCurrentDirectory();
        }
    }
}

namespace TranslationMod.Configuration
{
    public class LanguagePack
    {
        public LanguagePack(string directoryPath, string translationFilesPath, string name)
        {
            DirectoryPath = directoryPath;
            TranslationFilesPath = translationFilesPath;
            Name = name;
        }

        public string DirectoryPath { get; }
        public string TranslationFilesPath { get; }
        public string Name { get; }
    }
}
