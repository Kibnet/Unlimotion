using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaGraphControl;
using Unlimotion.ViewModel;
using Unlimotion.Views.Graph;

namespace Unlimotion.Views
{
    public partial class GraphControl : UserControl
    {
        public GraphControl()
        {
            DataContextChanged += GraphControl_DataContextChanged;
            InitializeComponent();
            
        }

        private void GraphControl_DataContextChanged(object sender, System.EventArgs e)
        {
            var dc = DataContext as GraphViewModel;
            if (dc != null)
            {
                dc.MyGraph = new SimpleWithSubgraph();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}