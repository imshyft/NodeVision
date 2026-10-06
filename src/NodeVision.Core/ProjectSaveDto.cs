using System.Collections.Generic;

namespace NodeVision.Core;

public class ProjectSaveDto
{
    public int Version { get; set; }

    public List<NodeDto> Nodes { get; set; } = new();

    public List<ConnectionDto> Connections { get; set; } = new();
}

public class NodeDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public PositionDto Position { get; set; } = new();

    /// <summary>Optional card size; when omitted a default is chosen from the content type.</summary>
    public SizeDto? Size { get; set; }

    public ContentDto Content { get; set; } = new();
}

public class SizeDto
{
    public float X { get; set; }

    public float Y { get; set; }
}

public class PositionDto
{
    public float X { get; set; }

    public float Y { get; set; }
}

public class ContentDto
{
    public string Type { get; set; } = string.Empty;

    public string? Value { get; set; }

    public string? Path { get; set; }
}

public class ConnectionDto
{
    public int Id { get; set; }

    public int ParentNodeId { get; set; }

    public int ChildNodeId { get; set; }
}