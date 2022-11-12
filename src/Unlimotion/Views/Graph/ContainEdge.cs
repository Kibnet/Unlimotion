using AvaloniaGraphControl;

namespace Unlimotion.Views.Graph
{
    public class ContainEdge : Edge
    {
        public ContainEdge(object tail, object head, object label = null, Symbol tailSymbol = Symbol.None, Symbol headSymbol = Symbol.Arrow) : base(tail, head, label, tailSymbol, headSymbol)
        {
        }
    }
}
