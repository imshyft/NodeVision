using System.Collections.Generic;

namespace NodeVision.Visualisation.Persistence
{
    public sealed class NodeLayout
    {
        public Dictionary<string, NodePosition> Positions { get; set; } = new();
    }

    public sealed class NodePosition
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}
