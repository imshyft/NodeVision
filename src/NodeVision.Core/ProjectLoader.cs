using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace NodeVision.Core;

public static class ProjectLoader
{
    public static Scene Load(string filePath)
    {
        string json = File.ReadAllText(filePath);

        ProjectSaveDto? saveFile =
            JsonSerializer.Deserialize<ProjectSaveDto>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (saveFile is null)
        {
            throw new InvalidOperationException(
                "Could not load the project file.");
        }

        Scene scene = new();

        Dictionary<int, Node> nodesById = new();

        // First create all nodes
        foreach (NodeDto nodeDto in saveFile.Nodes)
        {
            Node node = new()
            {
                Id = nodeDto.Id,
                NodeName = nodeDto.Name,
                Position = new Vector2(
                    nodeDto.Position.X,
                    nodeDto.Position.Y),
                Content = ConvertContent(nodeDto.Content)
            };

            scene.AddObject(node);
            nodesById.Add(node.Id, node);
        }

        // Then create connections
        foreach (ConnectionDto connectionDto in saveFile.Connections)
        {
            if (!nodesById.TryGetValue(
                    connectionDto.ParentNodeId,
                    out Node? parentNode))
            {
                throw new InvalidOperationException(
                    $"Parent node {connectionDto.ParentNodeId} does not exist.");
            }

            if (!nodesById.TryGetValue(
                    connectionDto.ChildNodeId,
                    out Node? childNode))
            {
                throw new InvalidOperationException(
                    $"Child node {connectionDto.ChildNodeId} does not exist.");
            }

            Connection connection = new()
            {
                Id = connectionDto.Id,
                ParentNodeId = connectionDto.ParentNodeId,
                ChildNodeId = connectionDto.ChildNodeId,
                ParentNode = parentNode,
                ChildNode = childNode
            };

            scene.AddObject(connection);
        }

        return scene;
    }

    private static NodeContent ConvertContent(ContentDto content)
    {
        return content.Type.ToLowerInvariant() switch
        {
            "text" => new TextContent
            {
                Value = content.Value ?? string.Empty
            },

            "image" => new ImageContent
            {
                FilePath = content.Path ?? string.Empty
            },

            _ => throw new InvalidOperationException(
                $"Unknown content type: {content.Type}")
        };
    }
}