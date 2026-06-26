using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpecStudioParser.PositionNumbering.Models;
using SpecStudioParser.PositionNumbering.Services;

namespace SpecStudioParser.PositionNumbering.ViewModels
{
    public partial class PositionNumberingViewModel : ObservableObject
    {
        private readonly PositionNumberingService _service = new();

        [ObservableProperty]
        private string _statusText = "Нажмите «Сканировать» для поиска позиций на чертеже.";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
        [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
        private bool _isBusy;

        [ObservableProperty] private string _prefix = "";
        [ObservableProperty] private int _startNumber = 1;
        [ObservableProperty] private int _step = 1;
        [ObservableProperty] private bool _overwriteExisting = true;
        [ObservableProperty] private string _targetAttributeTag = "POS";
        [ObservableProperty] private string? _leaderLayerFilter;

        private SortMode _sortMode = SortMode.TopToBottom_LeftToRight;
        public ObservableCollection<string> SortOptions { get; } = new()
        {
            "Сверху вниз, слева направо (ГОСТ)",
            "Слева направо, сверху вниз",
            "По порядку выбора",
            "По слою"
        };

        private string _selectedSortOption = "Сверху вниз, слева направо (ГОСТ)";
        public string SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                _selectedSortOption = value;
                _sortMode = value switch
                {
                    "Слева направо, сверху вниз" => SortMode.LeftToRight_TopToBottom,
                    "По порядку выбора" => SortMode.SelectionOrder,
                    "По слою" => SortMode.ByLayer,
                    _ => SortMode.TopToBottom_LeftToRight
                };
                OnPropertyChanged();
            }
        }

        public ObservableCollection<PositionInfo> Positions { get; } = new();

        private List<PositionInfo> _scannedPositions = new();

        [RelayCommand(CanExecute = nameof(CanScan))]
        public void Scan()
        {
            IsBusy = true;
            StatusText = "Сканирование выносок и блоков…";

            try
            {
                var profile = BuildProfile();
                _scannedPositions = _service.ScanPositions(profile);

                Positions.Clear();
                foreach (var p in _scannedPositions)
                    Positions.Add(p);

                StatusText = $"Найдено позиций: {_scannedPositions.Count} " +
                             $"(выносок: {_scannedPositions.Count(x => x.IsLeader)}, " +
                             $"блоков: {_scannedPositions.Count(x => x.IsBlockAttribute)}).";
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanApply))]
        public void Apply()
        {
            if (_scannedPositions.Count == 0) return;

            IsBusy = true;
            StatusText = "Простановка номеров…";

            try
            {
                var profile = BuildProfile();
                var result = _service.ApplyNumbering(_scannedPositions, profile);

                // Обновляем отображение
                Positions.Clear();
                foreach (var p in result.Positions)
                    Positions.Add(p);

                StatusText = result.Message;
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private NumberingProfile BuildProfile() => new()
        {
            Prefix = Prefix,
            StartNumber = StartNumber,
            Step = Step,
            SortMode = _sortMode,
            OverwriteExisting = OverwriteExisting,
            TargetAttributeTag = string.IsNullOrWhiteSpace(TargetAttributeTag) ? null : TargetAttributeTag,
            LeaderLayerFilter = string.IsNullOrWhiteSpace(LeaderLayerFilter) ? null : LeaderLayerFilter,
        };

        private bool CanScan() => !IsBusy;
        private bool CanApply() => !IsBusy && _scannedPositions.Count > 0;
    }
}
