param(
    [string]$RootPath = (Resolve-Path (Join-Path $PSScriptRoot ".."))
)

$ErrorActionPreference = "Stop"

function Replace-RequiredText {
    param(
        [string]$Path,
        [string]$OldText,
        [string]$NewText
    )

    $content = Get-Content -Path $Path -Raw -Encoding UTF8
    if (-not $content.Contains($OldText)) {
        throw "Expected text was not found in $Path"
    }

    $content = $content.Replace($OldText, $NewText)
    Set-Content -Path $Path -Value $content -Encoding UTF8
}

function Replace-IfFound {
    param(
        [string]$Path,
        [string]$OldText,
        [string]$NewText
    )

    $content = Get-Content -Path $Path -Raw -Encoding UTF8
    if ($content.Contains($OldText)) {
        $content = $content.Replace($OldText, $NewText)
        Set-Content -Path $Path -Value $content -Encoding UTF8
    }
}

$viewModelPath = Join-Path $RootPath "ViewModels/MainWindowViewModel.cs"
$commandsPath = Join-Path $RootPath "NanoCadCommands.cs"
$mainWindowPath = Join-Path $RootPath "Views/MainWindow.axaml"

# ViewModel command declaration: safe if already applied by v1.
Replace-IfFound -Path $viewModelPath -OldText @'
        public ICommand ScanAllCommand { get; }
        public ICommand ScanSelectedCommand { get; }
'@ -NewText @'
        public ICommand ScanAllCommand { get; }
        public ICommand ScanSelectedCommand { get; }
        public ICommand RefreshDrawingDataCommand { get; }
'@

# ViewModel constructor assignment.
Replace-RequiredText -Path $viewModelPath -OldText @'
            ScanAllCommand = new RelayCommand(ScanAllDrawing);
            ScanSelectedCommand = new RelayCommand(ScanSelectedOnly);
            GenerateSpecificationCommand = new RelayCommand(ExecuteApplySettings);
'@ -NewText @'
            ScanAllCommand = new RelayCommand(RefreshDrawingDataFromDrawing);
            ScanSelectedCommand = new RelayCommand(ScanSelectedOnly);
            RefreshDrawingDataCommand = new RelayCommand(RefreshDrawingDataFromDrawing);
            GenerateSpecificationCommand = new RelayCommand(ExecuteApplySettings);
'@

# ViewModel shared refresh pipeline.
Replace-RequiredText -Path $viewModelPath -OldText @'
        private void ScanAllDrawing()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            {
                _rawCache = _nanoCadService.GetAllModelSpaceObjects();
                LoadCadTypesFromDrawingInternal();
                ApplyFilteringLogics();
            }
            ExecuteApplySettings();
        }
'@ -NewText @'
        public void RefreshDrawingDataFromDrawing()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                ConnectionStatus = "No active nanoCAD document.";
                return;
            }

            try
            {
                using (doc.LockDocument())
                {
                    _rawCache = _nanoCadService.GetAllModelSpaceObjects();
                    LoadCadTypesFromDrawingInternal();
                    AutoFillAttributesFromSelectedTypesInternal();
                    ApplyFilteringLogics();
                }

                if (ActiveProfile.Datasets.Any())
                {
                    ExecuteApplySettings();
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Drawing data refresh error: {ex.Message}";
            }
        }
'@

# Main window: add compact refresh button to active profile toolbar.
Replace-RequiredText -Path $mainWindowPath -OldText @'
				<Grid ColumnDefinitions="Auto, *, Auto, Auto">
'@ -NewText @'
				<Grid ColumnDefinitions="Auto, *, Auto, Auto, Auto">
'@

Replace-RequiredText -Path $mainWindowPath -OldText @'
					<StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="8">
						<Button Content="Сохранить изменения" Command="{Binding SaveCurrentProfileCommand}" Background="#00897B" Foreground="White" FontWeight="Bold"/>
						<Button Content="Настройки структуры данных" Command="{Binding OpenSettingsWindowCommand}" Background="#78909C" Foreground="White"/>
					</StackPanel>
'@ -NewText @'
					<Button Grid.Column="2" Command="{Binding RefreshDrawingDataCommand}" Width="32" Height="28" Padding="4" Margin="0,0,8,0" ToolTip.Tip="Refresh drawing data">
						<Path Data="M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z" Fill="#1565C0" Width="16" Height="16" Stretch="Uniform"/>
					</Button>
					<StackPanel Grid.Column="3" Orientation="Horizontal" Spacing="8">
						<Button Content="Сохранить изменения" Command="{Binding SaveCurrentProfileCommand}" Background="#00897B" Foreground="White" FontWeight="Bold"/>
						<Button Content="Настройки структуры данных" Command="{Binding OpenSettingsWindowCommand}" Background="#78909C" Foreground="White"/>
					</StackPanel>
'@

# Commands: shared ViewModel instance and immediate refresh on SPEC_SHOW.
Replace-RequiredText -Path $commandsPath -OldText @'
        private static MainWindow? _currentWindow;
        private static bool _isAvaloniaInitialized = false;
'@ -NewText @'
        private static MainWindow? _currentWindow;
        private static MainWindowViewModel? _currentViewModel;
        private static bool _isAvaloniaInitialized = false;
'@

Replace-RequiredText -Path $commandsPath -OldText @'
                if (_currentWindow != null)
                {
                    _currentWindow.Activate();
                    return;
                }
'@ -NewText @'
                if (_currentWindow != null)
                {
                    _currentWindow.Activate();
                    _currentViewModel?.RefreshDrawingDataFromDrawing();
                    return;
                }
'@

Replace-RequiredText -Path $commandsPath -OldText @'
                        _currentWindow = new MainWindow();
                        var viewModel = new MainWindowViewModel();
                        _currentWindow.DataContext = viewModel;

                        _currentWindow.Closed += (s, e) => { _currentWindow = null; };
'@ -NewText @'
                        _currentWindow = new MainWindow();
                        _currentViewModel ??= new MainWindowViewModel();
                        _currentWindow.DataContext = _currentViewModel;

                        _currentWindow.Closed += (s, e) => { _currentWindow = null; };
'@

Replace-RequiredText -Path $commandsPath -OldText @'
                        if (nanoCadHwnd != IntPtr.Zero)
                        {
                            var platformHandle = _currentWindow.TryGetPlatformHandle();
                            if (platformHandle != null && platformHandle.Handle != IntPtr.Zero)
                            {
                                IntPtr avaloniaHwnd = platformHandle.Handle;
                                NativeWin32.SetWindowLongPtr(avaloniaHwnd, NativeWin32.GWLP_HWNDPARENT, nanoCadHwnd);
                            }
                        }
'@ -NewText @'
                        if (nanoCadHwnd != IntPtr.Zero)
                        {
                            var platformHandle = _currentWindow.TryGetPlatformHandle();
                            if (platformHandle != null && platformHandle.Handle != IntPtr.Zero)
                            {
                                IntPtr avaloniaHwnd = platformHandle.Handle;
                                NativeWin32.SetWindowLongPtr(avaloniaHwnd, NativeWin32.GWLP_HWNDPARENT, nanoCadHwnd);
                            }
                        }

                        _currentViewModel.RefreshDrawingDataFromDrawing();
'@

Replace-RequiredText -Path $commandsPath -OldText @'
                var viewModel = _currentWindow?.DataContext as MainWindowViewModel ?? new MainWindowViewModel();
                viewModel.ScanAllCommand.Execute(null);
'@ -NewText @'
                var viewModel = _currentWindow?.DataContext as MainWindowViewModel ?? _currentViewModel ??= new MainWindowViewModel();
                viewModel.RefreshDrawingDataFromDrawing();
'@

Write-Host "Applied remaining drawing data refresh pipeline changes." -ForegroundColor Green
Write-Host "Run: dotnet build SpecStudioParser.sln -c Debug" -ForegroundColor Cyan
