using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Unlimotion.Views.SearchControl
{
    public partial class SearchControl : UserControl
    {
        public SearchControl()
        {
            InitializeComponent();
        }

        private void OnClearClick(object? sender, RoutedEventArgs e)
        {
            SearchText = string.Empty;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        #region Property Registrations

        public static readonly DirectProperty<SearchControl, string> SearchTextProperty = AvaloniaProperty.RegisterDirect<SearchControl, string>(
            nameof(SearchText),
            o => o.SearchText,
            (o, v) => o.SearchText = v
        );

        public static readonly StyledProperty<string> WatermarkProperty = AvaloniaProperty.Register<SearchControl, string>(nameof(Watermark));


        #endregion

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set => SetAndRaise(SearchTextProperty, ref _searchText, value);
        }

        public string Watermark
        {
            get => GetValue(WatermarkProperty);
            set => SetValue(WatermarkProperty, value);
        }

    }
}