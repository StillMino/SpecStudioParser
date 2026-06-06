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
        private CadLibDatabaseProviderKind _providerKind = CadLibDatabaseProviderKind.PostgreSql;
        private string _host = "localhost";
        private int _port = 5432;
        private string _login = string.Empty;
        private string _password = string.Empty;
        private bool _lowercaseLogin;
        private bool _lowercasePassword;
        private CadLibDatabaseInfo? _selectedDatabase;
        private string _status = "Введите параметры подключения и загрузите список баз данных.";
        private bool _isBusy;

        public CadLibConnectionViewModel()
        {
            LoadDatabasesCommand = new AsyncRelayCommand(LoadDatabasesAsync, CanExecuteDatabaseOperation);
            ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(null));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<CadLibConnectionResult?>? RequestClose;

        public IReadOnlyList<CadLibDatabaseProviderKind> Providers { get; } =
            new[] { CadLibDatabaseProviderKind.PostgreSql, CadLibDatabaseProviderKind.MsSqlServer };

        public ObservableCollection<CadLibDatabaseInfo> Databases { get; } = new();

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

        public string Host
        {
            get => _host;
            set { _host = value; OnPropertyChanged(); NotifyCommands(); }
        }

        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); NotifyCommands(); }
        }

        public string Login
        {
            get => _login;
            set { _login = value; OnPropertyChanged(); NotifyCommands(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); NotifyCommands(); }
        }

        public bool LowercaseLogin
        {
            get => _lowercaseLogin;
            set { _lowercaseLogin = value; OnPropertyChanged(); }
        }

        public bool LowercasePassword
        {
            get => _lowercasePassword;
            set { _lowercasePassword = value; OnPropertyChanged(); }
        }

        public CadLibDatabaseInfo? SelectedDatabase
        {
            get => _selectedDatabase;
            set { _selectedDatabase = value; OnPropertyChanged(); NotifyCommands(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); NotifyCommands(); }
        }

        public ICommand LoadDatabasesCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand CancelCommand { get; }

        private async Task LoadDatabasesAsync()
        {
            IsBusy = true;
            Status = "Подключение к серверу и чтение списка баз данных...";

            try
            {
                var settings = BuildSettings();
                var databases = await Task.Run(() => _service.GetDatabasesAsync(settings)).Unwrap();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Databases.Clear();

                    foreach (var database in databases)
                    {
                        Databases.Add(database);
                    }

                    SelectedDatabase = Databases.FirstOrDefault();
                    Status = Databases.Count == 0
                        ? "Сервер доступен, но список баз данных пуст."
                        : $"Загружено баз данных: {Databases.Count}.";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Status = $"Ошибка подключения: {ex.Message}");
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
            }
        }

        private async Task ConnectAsync()
        {
            if (SelectedDatabase == null)
            {
                Status = "Выберите базу данных.";
                return;
            }

            IsBusy = true;
            Status = "Проверка структуры CADLib и чтение параметров...";

            try
            {
                var settings = BuildSettings();
                settings.DatabaseName = SelectedDatabase.Name;
                var parameters = await Task.Run(() => _service.ConnectAndLoadParametersAsync(settings)).Unwrap();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CadLibParameterCache.Current.Replace(settings, parameters);
                    Status = $"Подключено. Загружено параметров: {parameters.Count}.";
                    RequestClose?.Invoke(new CadLibConnectionResult { Settings = settings, Parameters = parameters });
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Status = $"Ошибка чтения CADLib БД: {ex.Message}");
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
            }
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

        private bool CanExecuteDatabaseOperation()
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(Host) && Port > 0 && !string.IsNullOrWhiteSpace(Login);
        }

        private bool CanConnect()
        {
            return CanExecuteDatabaseOperation() && SelectedDatabase != null;
        }

        private void NotifyCommands()
        {
            (LoadDatabasesCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            (ConnectCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            else
            {
                Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            }
        }
    }

    public sealed class CadLibParameterBrowserViewModel : INotifyPropertyChanged
    {
        private readonly ObservableCollection<CadLibParameterInfo> _source = new();
        private string _searchText = string.Empty;
        private string _selectedCategory = "Все";
        private string _status = string.Empty;

        public CadLibParameterBrowserViewModel()
        {
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            foreach (var parameter in CadLibParameterCache.Current.Parameters)
            {
                _source.Add(parameter);
            }

            RebuildCategories();
            ApplyFilter();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? RequestClose;

        public ObservableCollection<string> Categories { get; } = new();
        public ObservableCollection<CadLibParameterInfo> Parameters { get; } = new();
        public ICommand CloseCommand { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value ?? string.Empty;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value ?? "Все";
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private void RebuildCategories()
        {
            Categories.Clear();
            Categories.Add("Все");

            foreach (var category in _source
                         .Select(p => string.IsNullOrWhiteSpace(p.CategoryName) ? "Без категории" : p.CategoryName)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(c => c))
            {
                Categories.Add(category);
            }

            SelectedCategory = "Все";
        }

        private void ApplyFilter()
        {
            Parameters.Clear();
            var query = _source.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SelectedCategory) && !string.Equals(SelectedCategory, "Все", StringComparison.OrdinalIgnoreCase))
            {
                query = string.Equals(SelectedCategory, "Без категории", StringComparison.OrdinalIgnoreCase)
                    ? query.Where(p => string.IsNullOrWhiteSpace(p.CategoryName))
                    : query.Where(p => string.Equals(p.CategoryName, SelectedCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.Trim().ToLowerInvariant();
                query = query.Where(p => p.SearchText.Contains(search));
            }

            foreach (var parameter in query.OrderBy(p => p.CategoryName).ThenBy(p => p.DisplayName).ThenBy(p => p.SystemName))
            {
                Parameters.Add(parameter);
            }

            Status = $"Показано параметров: {Parameters.Count} из {_source.Count}";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            else
            {
                Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            }
        }
    }
}
