using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;

namespace NodeVision.Core;

public static class ProjectLoader
{
    private const string ProjectFileName = "project.json";

    public static Scene Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "The NodeVision project file could not be found.",
                filePath);
        }

        if (!string.Equals(
                Path.GetExtension(filePath),
                ".nodevision",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected file is not a .nodevision project.");
        }

        using ZipArchive archive = ZipFile.OpenRead(filePath);

        ZipArchiveEntry? projectEntry =
            archive.GetEntry(ProjectFileName);

        if (projectEntry is null)
        {
            throw new InvalidOperationException(
                $"The NodeVision file does not contain {ProjectFileName}.");
        }

        ProjectSaveDto saveFile;

        using (Stream jsonStream = projectEntry.Open())
        {
            saveFile =
                JsonSerializer.Deserialize<ProjectSaveDto>(
                    jsonStream,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    })
                ?? throw new InvalidOperationException(
                    "Could not load the project data.");
        }

        // Images need real filesystem paths because ImageContent currently
        // stores a filepath. They are therefore extracted to a temporary folder.
        string extractionDirectory = CreateExtractionDirectory();

        try
        {
            Scene scene = new();
            Dictionary<int, Node> nodesById = new();

            // First create all nodes.
            foreach (NodeDto nodeDto in saveFile.Nodes)
            {
                Node node = new()
                {
                    Id = nodeDto.Id,
                    NodeName = nodeDto.Name,
                    Position = new Vector2(
                        nodeDto.Position.X,
                        nodeDto.Position.Y),
                    Content = ConvertContent(
                        nodeDto.Content,
                        archive,
                        extractionDirectory)
                };

                scene.AddObject(node);
                nodesById.Add(node.Id, node);
            }

            // Then create all connections.
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
        catch
        {
            // Loading failed, so the extracted assets are unnecessary.
            if (Directory.Exists(extractionDirectory))
            {
                Directory.Delete(
                    extractionDirectory,
                    recursive: true);
            }

            throw;
        }
    }

    private static NodeContent ConvertContent(
        ContentDto content,
        ZipArchive archive,
        string extractionDirectory)
    {
        return content.Type.ToLowerInvariant() switch
        {
            "text" => new TextContent
            {
                Value = content.Value ?? string.Empty
            },

            "image" => new ImageContent
            {
                FilePath = ExtractImage(
                    content.Path,
                    archive,
                    extractionDirectory)
            },

            _ => throw new InvalidOperationException(
                $"Unknown content type: {content.Type}")
        };
    }

    private static string ExtractImage(
        string? assetPath,
        ZipArchive archive,
        string extractionDirectory)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new InvalidOperationException(
                "An image node does not specify an asset path.");
        }

        // ZIP paths should always use forward slashes.
        string normalizedPath =
            assetPath.Replace('\\', '/').TrimStart('/');

        if (!normalizedPath.StartsWith(
                "assets/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Image path must be inside the assets folder: {assetPath}");
        }

        ZipArchiveEntry? assetEntry =
            archive.GetEntry(normalizedPath);

        if (assetEntry is null)
        {
            throw new InvalidOperationException(
                $"The asset '{normalizedPath}' is missing from the project.");
        }

        string relativeSystemPath =
            normalizedPath.Replace(
                '/',
                Path.DirectorySeparatorChar);

        string destinationPath = Path.GetFullPath(
            Path.Combine(
                extractionDirectory,
                relativeSystemPath));

        string extractionRoot =
            Path.GetFullPath(extractionDirectory)
            + Path.DirectorySeparatorChar;

        // Prevent malicious paths such as assets/../../some-file.
        if (!destinationPath.StartsWith(
                extractionRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The asset path is invalid: {assetPath}");
        }

        string? destinationDirectory =
            Path.GetDirectoryName(destinationPath);

        if (destinationDirectory is not null)
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        assetEntry.ExtractToFile(
            destinationPath,
            overwrite: true);

        return destinationPath;
    }

    private static string CreateExtractionDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "NodeVision",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        return directory;
    }
}