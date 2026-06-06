using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HostMgd.ApplicationServices;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CadApp = HostMgd.ApplicationServices.Application;

namespace SpecStudioParser.DesignTools.ViewModels
{
    public enum DesignToolAccessLevel
    {
        Free,
        Paid
    }

    public enum DesignToolContext
    {
        Drafting2D,
        Modeling3D,
        Universal
    }

    public partial class DesignToolFeatureViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private string _status = "Готово";

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public DesignToolAccessLevel AccessLevel { get; }
        public DesignToolContext Context { get; }
        public ICommand RunCommand { get; }

        public string AccessLabel => AccessLevel == DesignToolAccessLevel.Free ? "Бесплатно" : "Платно";
        public string ContextLabel => Context switch
        {
            DesignToolContext.Drafting2D => "2D",
            DesignToolContext.Modeling3D => "3D",
            _ => "2D/3D"
        };

        public DesignToolFeatureViewModel(
            string id,
            string name,
            string description,
            DesignToolAccessLevel accessLevel,
            DesignToolContext context,
            Action<DesignToolFeatureViewModel> execute)
        {
            Id = id;
            Name = name;
            Description = description;
            AccessLevel = accessLevel;
            Context = context;
            RunCommand = new RelayCommand(() => execute(this));
        }
    }

    public partial class DesignToolBlockViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isEnabled = true;

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public ObservableCollection<DesignToolFeatureViewModel> Features { get; } = new();

        public DesignToolBlockViewModel(string id, string name, string description)
        {
            Id = id;
            Name = name;
            Description = description;
        }
    }

    public partial class DesignToolsViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _status = "Инструменты проектировщика готовы к работе.";

        [ObservableProperty]
        private string _documentStatus = "Документ nanoCAD не проверен.";

        public ObservableCollection<DesignToolBlockViewModel> Blocks { get; } = new();
        public ICommand RefreshContextCommand { get; }

        public DesignToolsViewModel()
        {
            RefreshContextCommand = new RelayCommand(RefreshContext);

            Blocks.Add(CreateDraftingBlock());
            Blocks.Add(CreateModelBlock());
            Blocks.Add(CreateSpecifierBridgeBlock());

            RefreshContext();
        }

        private DesignToolBlockViewModel CreateDraftingBlock()
        {
            var block = new DesignToolBlockViewModel(
                "2d-drafting",
                "2D-проектирование",
                "Быстрые команды для работы с чертежом и выделением объектов.");

            block.Features.Add(new DesignToolFeatureViewModel(
                "2d-selection-info",
                "Сведения о выделении",
                "Показывает базовую информацию о текущем выделении nanoCAD.",
                DesignToolAccessLevel.Free,
                DesignToolContext.Drafting2D,
                ExecuteSelectionInfo));

            block.Features.Add(new DesignToolFeatureViewModel(
                "2d-select-layer",
                "Выделить по слою",
                "Будущая функция массового выделения объектов на том же слое.",
                DesignToolAccessLevel.Free,
                DesignToolContext.Drafting2D,
                ExecuteStub));

            return block;
        }

        private DesignToolBlockViewModel CreateModelBlock()
        {
            var block = new DesignToolBlockViewModel(
                "3d-model-tools",
                "3D и Model Studio CS",
                "Инструменты проверки и анализа объектов модели.");

            block.Features.Add(new DesignToolFeatureViewModel(
                "3d-check-empty-parameters",
                "Проверка пустых параметров",
                "Будущая проверка обязательных параметров объектов Model Studio CS.",
                DesignToolAccessLevel.Paid,
                DesignToolContext.Modeling3D,
                ExecuteStub));

            block.Features.Add(new DesignToolFeatureViewModel(
                "3d-focus-selection",
                "Фокус на выделении",
                "Будущая команда фокусировки и визуального контроля выбранных объектов.",
                DesignToolAccessLevel.Free,
                DesignToolContext.Modeling3D,
                ExecuteStub));

            return block;
        }

        private DesignToolBlockViewModel CreateSpecifierBridgeBlock()
        {
            var block = new DesignToolBlockViewModel(
                "specifier-bridge",
                "Связь со спецификатором",
                "Функции, которые смогут использовать данные спецификатора, но не меняют его текущую логику.");

            block.Features.Add(new DesignToolFeatureViewModel(
                "specifier-check-active-profile",
                "Проверка данных спецификации",
                "Будущая функция анализа данных по активному профилю спецификатора.",
                DesignToolAccessLevel.Paid,
                DesignToolContext.Universal,
                ExecuteStub));

            return block;
        }

        private void ExecuteSelectionInfo(DesignToolFeatureViewModel feature)
        {
            try
            {
                var doc = CadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    SetFeatureStatus(feature, "Нет активного документа nanoCAD.");
                    return;
                }

                var selection = doc.Editor.SelectImplied();
                if (selection.Status != HostMgd.EditorInput.PromptStatus.OK || selection.Value == null)
                {
                    SetFeatureStatus(feature, "Нет текущего выделения.");
                    doc.Editor.WriteMessage("\n[DesignTools]: Нет текущего выделения.\n");
                    return;
                }

                var count = selection.Value.GetObjectIds().Length;
                SetFeatureStatus(feature, $"Выделено объектов: {count}.");
                doc.Editor.WriteMessage($"\n[DesignTools]: Выделено объектов: {count}.\n");
            }
            catch (Exception ex)
            {
                SetFeatureStatus(feature, $"Ошибка: {ex.Message}");
            }
        }

        private void ExecuteStub(DesignToolFeatureViewModel feature)
        {
            SetFeatureStatus(feature, "Каркас функции создан. Реализация будет добавлена следующим шагом.");
        }

        private void SetFeatureStatus(DesignToolFeatureViewModel feature, string message)
        {
            feature.Status = message;
            Status = $"{feature.Name}: {message}";
        }

        private void RefreshContext()
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            DocumentStatus = doc == null
                ? "Активный документ nanoCAD не найден."
                : $"Активный документ: {doc.Name}";

            Status = "Контекст nanoCAD обновлен.";
        }
    }
}
