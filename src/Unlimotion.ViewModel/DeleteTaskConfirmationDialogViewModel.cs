using System;
using ReactiveUI;
using System.Reactive;

namespace Unlimotion.ViewModel
{
    public enum DeleteTaskDialogResult
    {
        Cancel,
        DeleteSingle,
        DeleteWithChildren
    }

    public class DeleteTaskConfirmationDialogViewModel : ReactiveObject
    {
        public DeleteTaskConfirmationDialogViewModel(string taskTitle, int nestedChildrenCount, Action<DeleteTaskDialogResult> closeAction)
        {
            if (nestedChildrenCount > 0)
            {
                Message = $"'{taskTitle}' contains {nestedChildrenCount} nested tasks. How would you like to proceed?";
                ShowDeleteWithChildrenButton = true;
            }
            else
            {
                Message = $"Are you sure you want to delete '{taskTitle}'?";
                ShowDeleteWithChildrenButton = false;
            }

            DeleteSingleCommand = ReactiveCommand.Create(() =>
            {
                closeAction(DeleteTaskDialogResult.DeleteSingle);
                return Unit.Default;
            });

            DeleteWithChildrenCommand = ReactiveCommand.Create(() =>
            {
                closeAction(DeleteTaskDialogResult.DeleteWithChildren);
                return Unit.Default;
            }, this.WhenAnyValue(x => x.ShowDeleteWithChildrenButton));

            CancelCommand = ReactiveCommand.Create(() =>
            {
                closeAction(DeleteTaskDialogResult.Cancel);
                return Unit.Default;
            });
        }

        public string Message { get; }
        public bool ShowDeleteWithChildrenButton { get; }

        public ReactiveCommand<Unit, Unit> DeleteSingleCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteWithChildrenCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    }
}
