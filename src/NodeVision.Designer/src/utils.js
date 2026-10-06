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

export const downloadFileFallbackBinary = (blobData, filename) => {
  const url = URL.createObjectURL(blobData);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url); // Clean up memory
};

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

export const uploadFileFallbackBinary = (onFileSelected) => {
  // 1. Create an invisible file input
  const input = document.createElement('input');
  input.type = 'file';
  input.accept = '.zip'; // Only allow zip files
  
  // 2. Listen for when the user selects a file
  input.onchange = (e) => {
    const file = e.target.files[0];
    if (file) {
      // Pass the raw File object back to App.jsx (do NOT use FileReader here)
      onFileSelected(file); 
    }
  };
  
  // 3. Trigger the file browser dialog
  input.click();
};

function convertReactFlowToSaveFile(flow, version = 1) {
  const nodeIdMap = new Map();

  const nodes = (flow.nodes || []).map((node, index) => {
    // Convert string IDs to integers; fallback to sequential index if non-numeric
    const numericId = Number.isInteger(Number(node.id))
      ? parseInt(node.id, 10)
      : index + 1;

    nodeIdMap.set(node.id, numericId);

    const label = node.data?.label ?? `Node ${numericId}`;

    // Safely format the content to strictly match the JSON Schema
    let cleanContent;
    if (node.data?.content?.type === 'image') {
      cleanContent = {
        type: 'image',
        path: node.data.content.path || '' // The zip loop will have already set this to 'assets/...'
      };
    } else {
      cleanContent = {
        type: 'text',
        value: node.data?.content?.value ?? label
      };
    }

    return {
      id: numericId,
      name: label,
      position: {
        x: Number(node.data?.position?.x || 0),
        y: Number(node.data?.position?.y || 0),
      },
      content: cleanContent,
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
    position: { x: 0, y: 0 }, // Dagre layout engine will overwrite this visually
    data: {
      label: node.name,
      position: node.position, // Your custom saved coordinates stay safe here!
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

const getExtFromMime = (mime) => {
  const map = { 'image/jpeg': 'jpg', 'image/png': 'png', 'image/gif': 'gif', 'image/webp': 'webp' };
  return map[mime] || 'png';
};

export {
    uploadFileFallback,
    downloadFileFallback,
    convertSaveFileToReactFlow,
    convertReactFlowToSaveFile,
    getExtFromMime
}