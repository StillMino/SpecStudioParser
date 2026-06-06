using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SpecStudioParser.CadLib
{
    public enum CadLibDatabaseProviderKind
    {
        PostgreSql = 0,
        MsSqlServer = 1
    }

    public enum CadLibParameterPickerMode
    {
        Single = 0,
        Multiple = 1
    }

    public sealed class CadLibParameterPickerOptions
    {
        public CadLibParameterPickerMode Mode { get; init; } = CadLibParameterPickerMode.Single;
        public string Title { get; init; } = "Выбор параметра CADLib";
        public string Hint { get; init; } = string.Empty;
        public bool CloseAfterSingleSelection { get; init; }
        public IReadOnlyCollection<string> PreselectedSystemNames { get; init; } = Array.Empty<string>();
    }

    public sealed class CadLibParameterPickerResult
    {
        public IReadOnlyList<CadLibParameterInfo> SelectedParameters { get; init; } = Array.Empty<CadLibParameterInfo>();
        public CadLibParameterInfo? SingleParameter => SelectedParameters.Count == 1 ? SelectedParameters[0] : null;
    }

    public sealed class CadLibConnectionSettings
    {
        public CadLibDatabaseProviderKind ProviderKind { get; set; } = CadLibDatabaseProviderKind.PostgreSql;
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5432;
        public string Login { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool LowercaseLogin { get; set; }
        public bool LowercasePassword { get; set; }
        public string DatabaseName { get; set; } = string.Empty;

        public string EffectiveLogin => LowercaseLogin ? Login.ToLowerInvariant() : Login;
        public string EffectivePassword => LowercasePassword ? Password.ToLowerInvariant() : Password;

        public CadLibConnectionSettings Clone()
        {
            return new CadLibConnectionSettings
            {
                ProviderKind = ProviderKind,
                Host = Host,
                Port = Port,
                Login = Login,
                Password = Password,
                LowercaseLogin = LowercaseLogin,
                LowercasePassword = LowercasePassword,
                DatabaseName = DatabaseName
            };
        }
    }

    public sealed class CadLibConnectionProfile
    {
        public string Name { get; set; } = string.Empty;
        public CadLibDatabaseProviderKind ProviderKind { get; set; } = CadLibDatabaseProviderKind.PostgreSql;
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5432;
        public string Login { get; set; } = string.Empty;
        public bool LowercaseLogin { get; set; }
        public bool LowercasePassword { get; set; }
        public string DatabaseName { get; set; } = string.Empty;

        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Host : Name;
    }

    public sealed class CadLibDatabaseInfo
    {
        public string Name { get; init; } = string.Empty;
        public bool IsCadLib { get; init; }
        public string DisplayName => IsCadLib ? $"✓ {Name}" : Name;
        public override string ToString() => DisplayName;
    }

    public sealed class CadLibParameterInfo
    {
        public int IdParamDef { get; init; }
        public int TypeId { get; init; }
        public string SystemName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;

        public string TypeDisplayName => TypeId switch
        {
            1 => "Строка",
            2 => "Целое",
            3 => "Число",
            _ => TypeId == 0 ? "Не задан" : TypeId.ToString()
        };

        public string SearchText => $"{SystemName} {DisplayName} {CategoryName} {Comment} {TypeDisplayName}".ToLowerInvariant();
    }

    public sealed class CadLibConnectionResult
    {
        public CadLibConnectionSettings Settings { get; init; } = new();
        public IReadOnlyList<CadLibParameterInfo> Parameters { get; init; } = Array.Empty<CadLibParameterInfo>();
    }

    public sealed class CadLibParameterCache
    {
        public static CadLibParameterCache Current { get; } = new();

        private CadLibParameterCache()
        {
        }

        public CadLibConnectionSettings? ConnectionSettings { get; private set; }
        public ObservableCollection<CadLibParameterInfo> Parameters { get; } = new();
        public bool IsConnected => ConnectionSettings != null && Parameters.Count > 0;

        public string ConnectionStatusText
        {
            get
            {
                if (ConnectionSettings == null) return "CADLib: не подключена";
                return $"CADLib: {ConnectionSettings.ProviderKind} / {ConnectionSettings.Host} / {ConnectionSettings.DatabaseName}, параметров {Parameters.Count}";
            }
        }

        public void Replace(CadLibConnectionSettings settings, IEnumerable<CadLibParameterInfo> parameters)
        {
            ConnectionSettings = settings.Clone();
            Parameters.Clear();

            foreach (var parameter in parameters.OrderBy(p => p.CategoryName).ThenBy(p => p.DisplayName).ThenBy(p => p.SystemName))
            {
                Parameters.Add(parameter);
            }
        }

        public CadLibParameterInfo? FindBySystemName(string systemName)
        {
            return Parameters.FirstOrDefault(p => string.Equals(p.SystemName, systemName, StringComparison.OrdinalIgnoreCase));
        }

        public CadLibParameterInfo? FindByDisplayName(string displayName)
        {
            return Parameters.FirstOrDefault(p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
