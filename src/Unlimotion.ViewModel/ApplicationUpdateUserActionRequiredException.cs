using System;

namespace Unlimotion.ViewModel;

public sealed class ApplicationUpdateUserActionRequiredException : InvalidOperationException
{
    public ApplicationUpdateUserActionRequiredException(string message)
        : base(message)
    {
    }
}
