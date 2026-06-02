using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SpecStudioParser
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Внутри CAD-систем мы не используем классический desktop lifetime,
            // поэтому оставляем метод пустым, чтобы предотвратить конфликты оконных менеджеров.
            base.OnFrameworkInitializationCompleted();
        }
    }
}