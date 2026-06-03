using SpecStudioParser.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SpecStudioParser.Services
{
    public static class ProfileStorageService
    {
        public const string DefaultProfilesFolderName = "SpecStudioProfiles";
        public const string DefaultProfileFileName = "Новый_профиль.xml";

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
                return DefaultProfileFileName;
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

        public static ReportProfile LoadProfile(string profilesPath, string fileName)
        {
            var profilePath = GetProfilePath(profilesPath, fileName);
            return MscsXmlService.LoadFromMscsXml(profilePath);
        }

        public static string SaveProfile(string profilesPath, string fileName, XDocument document)
        {
            var normalizedFileName = NormalizeXmlFileName(fileName);
            var profilePath = GetProfilePath(profilesPath, normalizedFileName);
            document.Save(profilePath);
            return normalizedFileName;
        }

        public static string CopyProfileIntoStorage(string sourcePath, string profilesPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("XML-файл профиля не найден.", sourcePath);
            }

            var fileName = NormalizeXmlFileName(Path.GetFileName(sourcePath));
            var destinationPath = GetProfilePath(profilesPath, fileName);
            File.Copy(sourcePath, destinationPath, true);
            return fileName;
        }
    }
}