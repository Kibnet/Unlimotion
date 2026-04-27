using TUnit.Core.Interfaces;

namespace Unlimotion.Test;

public sealed class SharedUiStateParallelLimit : IParallelLimit
{
    public int Limit => 1;
}
