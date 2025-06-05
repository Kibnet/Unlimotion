using Avalonia.ReactiveUI;
using Avalonia.Markup.Xaml;
using Unlimotion.ViewModel;
using ReactiveUI;
using System;

namespace Unlimotion.Views
{
    public partial class DeleteTaskConfirmationDialog : ReactiveWindow<DeleteTaskConfirmationDialogViewModel>
    {
        public DeleteTaskConfirmationDialog()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            ViewModel = new DeleteTaskConfirmationDialogViewModel("Default Title", 0, result => Close(result));
        }

        public DeleteTaskConfirmationDialog(string taskTitle, int nestedChildrenCount)
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            ViewModel = new DeleteTaskConfirmationDialogViewModel(taskTitle, nestedChildrenCount, result => Close(result));
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public DeleteTaskDialogResult DialogResult { get; private set; }

        private void Close(DeleteTaskDialogResult result)
        {
            DialogResult = result;
            Close();
        }
    }
}
