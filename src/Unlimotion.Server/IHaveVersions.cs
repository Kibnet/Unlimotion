using System.Collections.Generic;

namespace Unlimotion.Server
{
    public interface IHaveVersions
    {
        IEnumerable<SemanticVersioning.Version> Versions { get; }
    }
}
