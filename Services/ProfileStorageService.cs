using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpecStudioParser.Services
{
    public static class ProfileStorageService
    {
        public const string DefaultProfilesFolderName = "SpecStudioProfiles";

        public static string GetDefaultProfilesPath()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documentsPath, DefaultProfilesFolderName);
        }

        public static void EnsureProfilesFolder(string profilesPath)
        {
            if (string.IsNullOrWhiteSpace(profilesPath))
            {
                throw new InvalidOperationException("Путь к папке профилей не задан.");
            }

            Directory.CreateDirectory(profilesPath);
        }

        public static IReadOnlyList<string> GetProfileFileNames(string profilesPath)
        {
            if (string.IsNullOrWhiteSpace(profilesPath) || !Directory.Exists(profilesPath))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(profilesPath, "*.xml")
                .Select(Path.GetFileName)
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .OrderBy(fileName => fileName)
                .Cast<string>()
                .ToList();
        }

        public static string NormalizeXmlFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "Новый_профиль.xml";
            }

            return fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : fileName + ".xml";
        }

        public static string GetProfilePath(string profilesPath, string fileName)
        {
            EnsureProfilesFolder(profilesPath);
            return Path.Combine(profilesPath, NormalizeXmlFileName(fileName));
        }
    }
}