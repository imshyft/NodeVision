function downloadFileFallback(content, filename, mimeType = 'text/plain') {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  
  // Clean up
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function uploadFileFallback(onFileLoaded) {
  const input = document.createElement('input');
  input.type = 'file';
  input.accept = '.json';

  input.onchange = (event) => {
    const file = event.target.files[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = (e) => {
      onFileLoaded(e.target.result);
    };
    reader.readAsText(file);
  };

  input.click();
}

function convertReactFlowToSaveFile(flow, version = 1) {
  const nodeIdMap = new Map();

  const nodes = (flow.nodes || []).map((node, index) => {
    // Convert string IDs to integers; fallback to sequential index if non-numeric
    const numericId = Number.isInteger(Number(node.id))
      ? parseInt(node.id, 10)
      : index + 1;

    nodeIdMap.set(node.id, numericId);

    const label = node.data?.label ?? `Node ${numericId}`;

    return {
      id: numericId,
      name: label,
      position: {
        x: Number(node.position.x),
        y: Number(node.position.y),
      },
      content: {
        type: "text",
        value: label,
      },
    };
  });

  const connections = (flow.edges || []).map((edge, index) => {
    const parentNodeId = nodeIdMap.has(edge.source)
      ? nodeIdMap.get(edge.source)
      : parseInt(edge.source, 10);

    const childNodeId = nodeIdMap.has(edge.target)
      ? nodeIdMap.get(edge.target)
      : parseInt(edge.target, 10);

    return {
      id: index + 1,
      parentNodeId,
      childNodeId,
    };
  });

  return {
    version,
    nodes,
    connections,
  };
}

function convertSaveFileToReactFlow(saveFile, nodeType = "sphere") {
  const nodes = (saveFile.nodes || []).map((node) => ({
    id: String(node.id),
    type: nodeType,
    position: {
      x: node.position.x,
      y: node.position.y,
    },
    data: {
      label: node.name,
      content: node.content,
    },
  }));

  const edges = (saveFile.connections || []).map((conn) => ({
    id: `xy-edge__${conn.parentNodeId}-${conn.childNodeId}`,
    source: String(conn.parentNodeId),
    target: String(conn.childNodeId),
    animated: true,
  }));

  return { nodes, edges };
}

export {
    uploadFileFallback,
    downloadFileFallback,
    convertSaveFileToReactFlow,
    convertReactFlowToSaveFile
}