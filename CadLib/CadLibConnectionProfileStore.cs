using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpecStudioParser.CadLib
{
    public sealed class CadLibConnectionProfileStore
    {
        private readonly string _filePath;

        public CadLibConnectionProfileStore()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SpecStudioProfiles");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "cadlib-connections.json");
        }

        public IReadOnlyList<CadLibConnectionProfile> LoadProfiles()
        {
            try
            {
                if (!File.Exists(_filePath)) return Array.Empty<CadLibConnectionProfile>();

                var json = File.ReadAllText(_filePath);
                var profiles = JsonSerializer.Deserialize<List<CadLibConnectionProfile>>(json) ?? new List<CadLibConnectionProfile>();
                return profiles
                    .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return Array.Empty<CadLibConnectionProfile>();
            }
        }

        public void SaveProfiles(IEnumerable<CadLibConnectionProfile> profiles)
        {
            var list = profiles
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }

        public static CadLibConnectionProfile FromSettings(string name, CadLibConnectionSettings settings)
        {
            return new CadLibConnectionProfile
            {
                Name = string.IsNullOrWhiteSpace(name) ? BuildDefaultName(settings) : name.Trim(),
                ProviderKind = settings.ProviderKind,
                Host = settings.Host,
                Port = settings.Port,
                Login = settings.Login,
                LowercaseLogin = settings.LowercaseLogin,
                LowercasePassword = settings.LowercasePassword,
                DatabaseName = settings.DatabaseName
            };
        }

        public static CadLibConnectionSettings ToSettings(CadLibConnectionProfile profile, string password)
        {
            return new CadLibConnectionSettings
            {
                ProviderKind = profile.ProviderKind,
                Host = profile.Host,
                Port = profile.Port,
                Login = profile.Login,
                Password = password,
                LowercaseLogin = profile.LowercaseLogin,
                LowercasePassword = profile.LowercasePassword,
                DatabaseName = profile.DatabaseName
            };
        }

        private static string BuildDefaultName(CadLibConnectionSettings settings)
        {
            var database = string.IsNullOrWhiteSpace(settings.DatabaseName) ? "database" : settings.DatabaseName;
            return $"{settings.Host} / {database}";
        }
    }
}
