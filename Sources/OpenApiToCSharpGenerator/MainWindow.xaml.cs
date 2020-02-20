using OpenApiToCSharpGenerator.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OpenApiToCSharpGenerator
{
    public partial class MainWindow : Window
    {
        private readonly OpenApiGenerator _apiGenerator;

        public MainWindow()
        {
            InitializeComponent();
            InitSettingsPanel();
            _apiGenerator = new OpenApiGenerator(Settings.Default);
        }

        private void InitSettingsPanel()
        {
            var type = typeof(IAppSettings);
            var propertyInfos = type.GetProperties();
            for (var i = 0; i < propertyInfos.Length; i++)
            {
                var propertyInfo = propertyInfos[i];
                SettingsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var lb = new Label
                {
                    Content = propertyInfo.Name
                };

                lb.SetValue(Grid.ColumnProperty, 0);
                lb.SetValue(Grid.RowProperty, i);
                SettingsPanel.Children.Add(lb);

                UIElement element = null;
                switch (propertyInfo.PropertyType.Name)
                {
                    default:
                    {
                        var binding = new Binding(propertyInfo.Name);
                        binding.Source = Settings.Default;
                        var tb = new TextBox();
                        tb.SetBinding(TextBox.TextProperty, binding);
                        element = tb;
                        break;
                    }
                }

                element.SetValue(Grid.ColumnProperty, 1);
                element.SetValue(Grid.RowProperty, i);
                SettingsPanel.Children.Add(element);
            }
        }

        private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            Settings.Default.Save();

            var button = (Button)sender;
            button.IsEnabled = false;
            await _apiGenerator.Run();
            button.IsEnabled = true;
        }
    }
}
