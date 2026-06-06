using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SpecStudioParser.CadLib
{
    public sealed class CadLibConnectionViewModel : INotifyPropertyChanged
    {
        private readonly CadLibConnectionService _service = new();
        private readonly CadLibConnectionProfileStore _profileStore = new();
        private CadLibDatabaseProviderKind _providerKind = CadLibDatabaseProviderKind.PostgreSql;
        private CadLibConnectionProfile? _selectedProfile;
        private string _profileName = string.Empty;
        private string _host = "localhost";
        private int _port = 5432;
        private string _login = string.Empty;
        private string _password = string.Empty;
        private bool _lowercaseLogin;
        private bool _lowercasePassword;
        private CadLibDatabaseInfo? _selectedDatabase;
        private string _status = "Введите параметры подключения или выберите сохранённый профиль.";
        private string _details = string.Empty;
        private bool _isBusy;

        public CadLibConnectionViewModel()
        {
            LoadDatabasesCommand = new AsyncRelayCommand(LoadDatabasesAsync, CanExecuteDatabaseOperation);
            TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanConnect);
            ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
            ApplyProfileCommand = new RelayCommand(ApplySelectedProfile, () => SelectedProfile != null);
            SaveProfileCommand = new RelayCommand(SaveCurrentProfile, CanSaveProfile);
            DeleteProfileCommand = new RelayCommand(DeleteSelectedProfile, () => SelectedProfile != null);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(null));
            ReloadProfiles();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<CadLibConnectionResult?>? RequestClose;

        public IReadOnlyList<CadLibDatabaseProviderKind> Providers { get; } = new[] { CadLibDatabaseProviderKind.PostgreSql, CadLibDatabaseProviderKind.MsSqlServer };
        public ObservableCollection<CadLibConnectionProfile> Profiles { get; } = new();
        public ObservableCollection<CadLibDatabaseInfo> Databases { get; } = new();

        public CadLibConnectionProfile? SelectedProfile
        {
            get => _selectedProfile;
            set { _selectedProfile = value; OnPropertyChanged(); NotifyCommands(); }
        }

        public string ProfileName
        {
            get => _profileName;
            set { _profileName = value ?? string.Empty; OnPropertyChanged(); NotifyCommands(); }
        }

        public CadLibDatabaseProviderKind ProviderKind
        {
            get => _providerKind;
            set
            {
                if (_providerKind == value) return;
                _providerKind = value;
                Port = value == CadLibDatabaseProviderKind.PostgreSql ? 5432 : 1433;
                Databases.Clear();
                SelectedDatabase = null;
                OnPropertyChanged();
                NotifyCommands();
            }
        }

        public string Host { get => _host; set { _host = value ?? string.Empty; OnPropertyChanged(); NotifyCommands(); } }
        public int Port { get => _port; set { _port = value; OnPropertyChanged(); NotifyCommands(); } }
        public string Login { get => _login; set { _login = value ?? string.Empty; OnPropertyChanged(); NotifyCommands(); } }
        public string Password { get => _password; set { _password = value ?? string.Empty; OnPropertyChanged(); NotifyCommands(); } }
        public bool LowercaseLogin { get => _lowercaseLogin; set { _lowercaseLogin = value; OnPropertyChanged(); NotifyCommands(); } }
        public bool LowercasePassword { get => _lowercasePassword; set { _lowercasePassword = value; OnPropertyChanged(); NotifyCommands(); } }
        public CadLibDatabaseInfo? SelectedDatabase { get => _selectedDatabase; set { _selectedDatabase = value; OnPropertyChanged(); NotifyCommands(); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string Details { get => _details; set { _details = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDetails)); } }
        public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); NotifyCommands(); } }

        public ICommand LoadDatabasesCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand ApplyProfileCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand CancelCommand { get; }

        private void ReloadProfiles()
        {
            Profiles.Clear();
            foreach (var profile in _profileStore.LoadProfiles()) Profiles.Add(profile);
            SelectedProfile = Profiles.FirstOrDefault();
            if (SelectedProfile != null) ApplySelectedProfile();
        }

        private void ApplySelectedProfile()
        {
            if (SelectedProfile == null) return;
            ProviderKind = SelectedProfile.ProviderKind;
            Host = SelectedProfile.Host;
            Port = SelectedProfile.Port;
            Login = SelectedProfile.Login;
            LowercaseLogin = SelectedProfile.LowercaseLogin;
            LowercasePassword = SelectedProfile.LowercasePassword;
            ProfileName = SelectedProfile.Name;
            Databases.Clear();
            if (!string.IsNullOrWhiteSpace(SelectedProfile.DatabaseName))
            {
                var database = new CadLibDatabaseInfo { Name = SelectedProfile.DatabaseName, IsCadLib = true };
                Databases.Add(database);
                SelectedDatabase = database;
            }
            Status = "Профиль применён. Введите пароль и подключитесь.";
            Details = string.Empty;
        }

        private void SaveCurrentProfile()
        {
            var settings = BuildSettings();
            settings.DatabaseName = SelectedDatabase?.Name ?? settings.DatabaseName;
            var profiles = Profiles.Where(p => !string.Equals(p.Name, ProfileName, StringComparison.OrdinalIgnoreCase)).ToList();
            profiles.Add(CadLibConnectionProfileStore.FromSettings(ProfileName, settings));
            _profileStore.SaveProfiles(profiles);
            ReloadProfiles();
            Status = "Профиль подключения сохранён. Пароль не сохраняется.";
        }

        private void DeleteSelectedProfile()
        {
            if (SelectedProfile == null) return;
            var profiles = Profiles.Where(p => !ReferenceEquals(p, SelectedProfile)).ToList();
            _profileStore.SaveProfiles(profiles);
            ReloadProfiles();
            Status = "Профиль подключения удалён.";
        }

        private async Task LoadDatabasesAsync()
        {
            IsBusy = true;
            Status = "Чтение списка баз данных...";
            Details = string.Empty;
            try
            {
                var settings = BuildSettings();
                var databases = await Task.Run(async () => await _service.GetDatabasesAsync(settings));
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Databases.Clear();
                    foreach (var database in databases) Databases.Add(database);
                    SelectedDatabase = Databases.FirstOrDefault(d => d.IsCadLib) ?? Databases.FirstOrDefault();
                    var cadLibCount = Databases.Count(d => d.IsCadLib);
                    Status = Databases.Count == 0 ? "Сервер доступен, но список баз данных пуст." : $"Загружено баз: {Databases.Count}. CADLib: {cadLibCount}.";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => SetError("Не удалось загрузить список баз.", ex));
            }
            finally { await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false); }
        }

        private async Task TestConnectionAsync()
        {
            if (SelectedDatabase == null) { Status = "Выберите базу данных."; return; }
            IsBusy = true;
            Status = "Проверка CADLib схемы...";
            Details = string.Empty;
            try
            {
                var settings = BuildSettings();
                settings.DatabaseName = SelectedDatabase.Name;
                await Task.Run(async () => await _service.ConnectAndLoadParametersAsync(settings));
                await Dispatcher.UIThread.InvokeAsync(() => Status = "CADLib схема найдена. Параметры доступны.");
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => SetError("Проверка CADLib схемы не выполнена.", ex));
            }
            finally { await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false); }
        }

        private async Task ConnectAsync()
        {
            if (SelectedDatabase == null) { Status = "Выберите базу данных."; return; }
            IsBusy = true;
            Status = "Чтение параметров CADLib...";
            Details = string.Empty;
            try
            {
                var settings = BuildSettings();
                settings.DatabaseName = SelectedDatabase.Name;
                var parameters = await Task.Run(async () => await _service.ConnectAndLoadParametersAsync(settings));
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CadLibParameterCache.Current.Replace(settings, parameters);
                    Status = $"Подключено. Параметров: {parameters.Count}.";
                    if (string.IsNullOrWhiteSpace(ProfileName)) ProfileName = $"{settings.Host} / {settings.DatabaseName}";
                    SaveCurrentProfile();
                    RequestClose?.Invoke(new CadLibConnectionResult { Settings = settings, Parameters = parameters });
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => SetError("Не удалось прочитать CADLib БД.", ex));
            }
            finally { await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false); }
        }

        private void SetError(string message, Exception ex)
        {
            Status = message;
            Details = ex.Message;
        }

        private CadLibConnectionSettings BuildSettings()
        {
            return new CadLibConnectionSettings
            {
                ProviderKind = ProviderKind,
                Host = Host?.Trim() ?? string.Empty,
                Port = Port,
                Login = Login ?? string.Empty,
                Password = Password ?? string.Empty,
                LowercaseLogin = LowercaseLogin,
                LowercasePassword = LowercasePassword,
                DatabaseName = SelectedDatabase?.Name ?? string.Empty
            };
        }

        private bool CanExecuteDatabaseOperation() => !IsBusy && !string.IsNullOrWhiteSpace(Host) && Port > 0 && !string.IsNullOrWhiteSpace(Login);
        private bool CanConnect() => CanExecuteDatabaseOperation() && SelectedDatabase != null;
        private bool CanSaveProfile() => !IsBusy && !string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Login);

        private void NotifyCommands()
        {
            (LoadDatabasesCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            (TestConnectionCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            (ConnectCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            (ApplyProfileCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (SaveProfileCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (DeleteProfileCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
        }
    }

    public sealed class CadLibParameterBrowserViewModel : INotifyPropertyChanged
    {
        private readonly ObservableCollection<CadLibParameterInfo> _source = new();
        private string _searchText = string.Empty;
        private string _selectedCategory = "Все";
        private string _selectedType = "Все";
        private CadLibParameterInfo? _selectedParameter;
        private string _status = string.Empty;

        public CadLibParameterBrowserViewModel()
        {
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
            CopySystemNameCommand = new RelayCommand(() => RequestCopy?.Invoke(SelectedParameter?.SystemName ?? string.Empty), () => SelectedParameter != null);
            CopyDisplayNameCommand = new RelayCommand(() => RequestCopy?.Invoke(SelectedParameter?.DisplayName ?? string.Empty), () => SelectedParameter != null);
            CopyIdCommand = new RelayCommand(() => RequestCopy?.Invoke(SelectedParameter?.IdParamDef.ToString() ?? string.Empty), () => SelectedParameter != null);
            foreach (var parameter in CadLibParameterCache.Current.Parameters) _source.Add(parameter);
            ConnectionInfo = CadLibParameterCache.Current.ConnectionStatusText;
            RebuildCategories();
            RebuildTypes();
            ApplyFilter();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? RequestClose;
        public event Action<string>? RequestCopy;
        public ObservableCollection<string> Categories { get; } = new();
        public ObservableCollection<string> Types { get; } = new();
        public ObservableCollection<CadLibParameterInfo> Parameters { get; } = new();
        public ICommand CloseCommand { get; }
        public ICommand CopySystemNameCommand { get; }
        public ICommand CopyDisplayNameCommand { get; }
        public ICommand CopyIdCommand { get; }
        public string ConnectionInfo { get; }

        public string SearchText { get => _searchText; set { _searchText = value ?? string.Empty; OnPropertyChanged(); ApplyFilter(); } }
        public string SelectedCategory { get => _selectedCategory; set { _selectedCategory = value ?? "Все"; OnPropertyChanged(); ApplyFilter(); } }
        public string SelectedType { get => _selectedType; set { _selectedType = value ?? "Все"; OnPropertyChanged(); ApplyFilter(); } }
        public CadLibParameterInfo? SelectedParameter { get => _selectedParameter; set { _selectedParameter = value; OnPropertyChanged(); NotifyCopyCommands(); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private void RebuildCategories()
        {
            Categories.Clear(); Categories.Add("Все");
            foreach (var category in _source.Select(p => string.IsNullOrWhiteSpace(p.CategoryName) ? "Без категории" : p.CategoryName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c)) Categories.Add(category);
            SelectedCategory = "Все";
        }

        private void RebuildTypes()
        {
            Types.Clear(); Types.Add("Все");
            foreach (var type in _source.Select(p => p.TypeDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t)) Types.Add(type);
            SelectedType = "Все";
        }

        private void ApplyFilter()
        {
            Parameters.Clear();
            var query = _source.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(SelectedCategory) && !string.Equals(SelectedCategory, "Все", StringComparison.OrdinalIgnoreCase))
                query = string.Equals(SelectedCategory, "Без категории", StringComparison.OrdinalIgnoreCase) ? query.Where(p => string.IsNullOrWhiteSpace(p.CategoryName)) : query.Where(p => string.Equals(p.CategoryName, SelectedCategory, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(SelectedType) && !string.Equals(SelectedType, "Все", StringComparison.OrdinalIgnoreCase))
                query = query.Where(p => string.Equals(p.TypeDisplayName, SelectedType, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.Trim().ToLowerInvariant();
                query = query.Where(p => p.SearchText.Contains(search));
            }
            foreach (var parameter in query.OrderBy(p => p.CategoryName).ThenBy(p => p.DisplayName).ThenBy(p => p.SystemName)) Parameters.Add(parameter);
            Status = $"Показано параметров: {Parameters.Count} из {_source.Count}";
        }

        private void NotifyCopyCommands()
        {
            (CopySystemNameCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (CopyDisplayNameCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (CopyIdCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
        }
    }
}
