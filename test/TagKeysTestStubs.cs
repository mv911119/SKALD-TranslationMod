using System;
using TranslationMod.Configuration;

namespace TranslationMod
{
    public sealed class DebugLogger
    {
        public void LogDebug(string message) => Console.Error.WriteLine(message);
        public void LogInfo(string message) => Console.Error.WriteLine(message);
        public void LogWarning(string message) => Console.Error.WriteLine(message);
        public void LogError(string message) => Console.Error.WriteLine(message);
    }

    public static class TranslationMod
    {
        public static DebugLogger Logger { get; } = new();
    }

    public sealed class TranslationService
    {
        public string Process(string input) => input;
    }

    public static class LanguageManager
    {
        private static readonly LanguagePack CurrentLanguagePack = new(
            directoryPath: string.Empty,
            translationFilesPath: string.Empty,
            name: "Chinese");

        public static LanguagePack GetCurrentLanguagePack() => CurrentLanguagePack;

        public static bool NoLetterLanguage() => true;
    }
}

namespace TranslationMod.Configuration
{
    public sealed class LanguagePack
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
