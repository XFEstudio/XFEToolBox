using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToolControls = XFEToolBox.WpfCore.Controls;

namespace XFEToolBox.Client.Views.Windows;

public partial class ControlGalleryWindow
{
    private ScenarioPreviewResult CreateScenarioPreview(string id) => id switch
    {
        "button" => BuildButtonScenarios(),
        "text-editor" => BuildTextEditorScenarios(),
        "password-editor" => BuildPasswordEditorScenarios(),
        "auto-suggest" => BuildAutoSuggestScenarios(),
        "check-box" => BuildCheckBoxScenarios(),
        "radio-button" => BuildRadioButtonScenarios(),
        "toggle-button" => BuildToggleButtonScenarios(),
        "combo-box" => BuildComboBoxScenarios(),
        "slider" => BuildSliderScenarios(),
        "color-picker" => BuildColorPickerScenarios(),
        "date-picker" => BuildDatePickerScenarios(),
        "calendar-picker" => BuildCalendarPickerScenarios(),
        "time-picker" => BuildTimePickerScenarios(),
        "progress-bar" => BuildProgressBarScenarios(),
        "progress-ring" => BuildProgressRingScenarios(),
        "info-bar" => BuildInfoBarScenarios(),
        "command-bar" => BuildCommandBarScenarios(),
        "expander" => BuildExpanderScenarios(),
        "image" => BuildImageScenarios(),
        "person-picture" => BuildPersonPictureScenarios(),
        "carousel" => BuildCarouselScenarios(),
        "data-grid" => BuildDataGridScenarios(),
        "tab-view" => BuildTabViewScenarios(),
        "navigation-view" => BuildNavigationViewScenarios(),
        "command-preview" => BuildCommandPreviewScenarios(),
        "xaml-code-viewer" => BuildXamlCodeViewerScenarios(),
        "scroll-text" => BuildScrollTextScenarios(),
        "mini-tool-button" => BuildMiniToolButtonScenarios(),
        _ => new ScenarioPreviewResult(
            new TextBlock { Text = "尚未提供此控件的场景演示。" },
            new HashSet<string>(StringComparer.Ordinal))
    };

    private static ScenarioPreviewResult Scenarios(params GalleryScenario[] scenarios)
    {
        var panel = new StackPanel();
        var coveredProperties = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < scenarios.Length; index++)
        {
            var scenario = scenarios[index];
            foreach (var parameter in scenario.Parameters)
                coveredProperties.Add(parameter.PropertyName);
            panel.Children.Add(CreateScenarioCard(scenario, index + 1));
        }
        return new ScenarioPreviewResult(panel, coveredProperties);
    }

    private static GalleryScenario Scenario(
        string title,
        string description,
        FrameworkElement preview,
        params GalleryParameter[] parameters) => new(title, description, preview, parameters);

    private static Border CreateScenarioCard(GalleryScenario scenario, int sequence)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleGrid = new Grid { Margin = new Thickness(16, 13, 16, 12) };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Border
        {
            Width = 25,
            Height = 25,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(238, 237, 251)),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = sequence.ToString("00"),
                Foreground = new SolidColorBrush(Color.FromRgb(105, 105, 188)),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        titleGrid.Children.Add(badge);
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        heading.Children.Add(new TextBlock
        {
            Text = scenario.Title,
            Foreground = new SolidColorBrush(Color.FromRgb(70, 70, 91)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        });
        heading.Children.Add(new TextBlock
        {
            Text = scenario.Description,
            Foreground = new SolidColorBrush(Color.FromRgb(143, 143, 162)),
            FontSize = 9,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(heading, 1);
        titleGrid.Children.Add(heading);
        root.Children.Add(titleGrid);

        var divider = new Border { Background = new SolidColorBrush(Color.FromRgb(236, 235, 244)) };
        Grid.SetRow(divider, 1);
        root.Children.Add(divider);

        var body = new Grid { Margin = new Thickness(14) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(282) });

        var previewSurface = new Border
        {
            MinHeight = 142,
            Padding = new Thickness(18),
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(235, 234, 244)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = scenario.Preview
        };
        body.Children.Add(previewSurface);

        var bodyDivider = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(235, 234, 244)),
            Margin = new Thickness(12, 0, 12, 0)
        };
        Grid.SetColumn(bodyDivider, 1);
        body.Children.Add(bodyDivider);

        var parameters = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        parameters.Children.Add(new TextBlock
        {
            Text = "场景参数",
            Foreground = new SolidColorBrush(Color.FromRgb(91, 91, 160)),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var parameter in scenario.Parameters)
            parameters.Children.Add(parameter.Editor);
        Grid.SetColumn(parameters, 2);
        body.Children.Add(parameters);
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 14),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(229, 228, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = root
        };
    }

    private static GalleryParameter ToggleParameter(string propertyName, bool initialValue, Action<bool> changed)
    {
        var toggle = new CheckBox { Content = initialValue ? "已启用" : "已关闭", IsChecked = initialValue };
        toggle.Click += (_, _) =>
        {
            var value = toggle.IsChecked == true;
            toggle.Content = value ? "已启用" : "已关闭";
            changed(value);
        };
        return Parameter(propertyName, toggle);
    }

    private static GalleryParameter TriStateParameter(string propertyName, bool? initialValue, Action<bool?> changed)
    {
        var combo = new ComboBox
        {
            Height = 34,
            ItemsSource = new[] { "未选中", "已选中", "不确定" },
            SelectedIndex = initialValue is null ? 2 : initialValue.Value ? 1 : 0
        };
        combo.SelectionChanged += (_, _) => changed(combo.SelectedIndex switch { 1 => true, 2 => null, _ => false });
        return Parameter(propertyName, combo);
    }

    private static GalleryParameter TextParameter(string propertyName, string initialValue, Action<string> changed)
    {
        var editor = new ToolControls.TextEditor { Height = 34, Text = initialValue };
        editor.TextChanged += (_, _) => changed(editor.Text);
        return Parameter(propertyName, editor);
    }

    private static GalleryParameter ChoiceParameter(
        string propertyName,
        IReadOnlyList<string> choices,
        int selectedIndex,
        Action<int, string> changed)
    {
        var combo = new ComboBox { Height = 34, ItemsSource = choices, SelectedIndex = selectedIndex };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedItem is string value)
                changed(combo.SelectedIndex, value);
        };
        return Parameter(propertyName, combo);
    }

    private static GalleryParameter SliderParameter(
        string propertyName,
        double minimum,
        double maximum,
        double initialValue,
        Action<double> changed,
        string format = "0")
    {
        var panel = new StackPanel();
        var valueText = new TextBlock
        {
            Text = initialValue.ToString(format),
            Foreground = new SolidColorBrush(Color.FromRgb(105, 105, 188)),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = initialValue,
            TickFrequency = format.Contains('.') ? 0.1 : 1,
            IsSnapToTickEnabled = !format.Contains('.')
        };
        slider.ValueChanged += (_, _) =>
        {
            valueText.Text = slider.Value.ToString(format);
            changed(slider.Value);
        };
        panel.Children.Add(valueText);
        panel.Children.Add(slider);
        return Parameter(propertyName, panel);
    }

    private static GalleryParameter ActionParameter(string propertyName, string buttonText, Action clicked)
    {
        var button = new Button { Content = buttonText, Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => clicked();
        return Parameter(propertyName, button);
    }

    private static GalleryParameter LiveValueParameter(string propertyName, string initialValue, out TextBlock valueText)
    {
        valueText = new TextBlock
        {
            Text = initialValue,
            Foreground = new SolidColorBrush(Color.FromRgb(91, 91, 160)),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 9.5,
            TextWrapping = TextWrapping.Wrap
        };
        return Parameter(propertyName, new Border
        {
            Padding = new Thickness(9, 7, 9, 7),
            Background = new SolidColorBrush(Color.FromRgb(245, 244, 252)),
            CornerRadius = new CornerRadius(8),
            Child = valueText
        });
    }

    private static GalleryParameter Parameter(string propertyName, FrameworkElement editor)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = propertyName,
            Foreground = new SolidColorBrush(Color.FromRgb(91, 91, 111)),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 8.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        panel.Children.Add(editor);
        return new GalleryParameter(propertyName, new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Child = panel
        });
    }

    private static StackPanel ScenarioPreviewStack(params UIElement[] children)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    private static TextBlock ScenarioStatus(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(126, 126, 146)),
        FontSize = 9.5,
        Margin = new Thickness(0, 12, 0, 0),
        TextWrapping = TextWrapping.Wrap
    };

    private static StackPanel ScenarioRow(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    private sealed record ScenarioPreviewResult(FrameworkElement View, IReadOnlySet<string> CoveredProperties);

    private sealed record GalleryScenario(
        string Title,
        string Description,
        FrameworkElement Preview,
        IReadOnlyList<GalleryParameter> Parameters);

    private sealed record GalleryParameter(string PropertyName, FrameworkElement Editor);

    private sealed class GalleryCommand(Action execute) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
