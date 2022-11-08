using AvaloniaGraphControl;

namespace Unlimotion.Views.Graph
{
  class SimpleWithSubgraph : AvaloniaGraphControl.Graph
  {
    public SimpleWithSubgraph()
    {
      var a = new InteractiveItem("A");
      var b = new CompositeItem("B");
      var b1 = new InteractiveItem("B1");
      var b2 = new InteractiveItem("B2");
      var b3 = new InteractiveItem("B3");
      var b4 = new InteractiveItem("B4");
      var c = new InteractiveItem("C");
      var d = new InteractiveItem("D");
      Edges.Add(new Edge(a, b));
      Edges.Add(new Edge(a, c));
      Edges.Add(new Edge(b, d));
      Edges.Add(new Edge(c, d));
      Edges.Add(new Edge(b1, b2));
      Edges.Add(new Edge(b1, b3));
      Edges.Add(new Edge(b2, b4));
      Edges.Add(new Edge(b3, b4));
      Parent[b1] = b;
      Parent[b2] = b;
      Parent[b3] = b;
      Parent[b4] = b;
    }
  }
}
