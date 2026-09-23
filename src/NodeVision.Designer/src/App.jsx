import { useState, useCallback, useEffect } from 'react';
import { ReactFlow, addEdge, Background, Controls, Panel, useEdgesState, useNodesState } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import Node from './ui/Node'; 
import Dropdown from './ui/Dropdown';
import PropertyPanel from './ui/PropertyPanel';
import {
  uploadFileFallback, 
  downloadFileFallback, 
  convertReactFlowToSaveFile, 
  convertSaveFileToReactFlow, 
  getExtFromMime, 
  uploadFileFallbackBinary, 
  downloadFileFallbackBinary
} from './utils';
import JSZip from 'jszip';
import dagre from 'dagre';

// Register custom nodes
const nodeTypes = { sphere: Node };

const initialNodes = [];
const initialEdges = [];

// --- DAGRE TREE LAYOUT FUNCTION ---
// This function calculates the React Flow canvas positions based purely on the edges (connections).
const getLayoutedElements = (nodes, edges, direction = 'TB') => {
  const dagreGraph = new dagre.graphlib.Graph();
  dagreGraph.setDefaultEdgeLabel(() => ({}));
  
  // rankdir 'TB' = Top to Bottom tree. You can use 'LR' for Left to Right.
  dagreGraph.setGraph({ rankdir: direction, ranksep: 120, nodesep: 100 });

  // Approximate dimensions of your sphere nodes (update if your nodes are larger)
  const nodeWidth = 150; 
  const nodeHeight = 150;

  nodes.forEach((node) => {
    dagreGraph.setNode(node.id, { width: nodeWidth, height: nodeHeight });
  });

  edges.forEach((edge) => {
    dagreGraph.setEdge(edge.source, edge.target);
  });

  dagre.layout(dagreGraph);

  const layoutedNodes = nodes.map((node) => {
    const nodeWithPosition = dagreGraph.node(node.id);
    return {
      ...node,
      // React Flow requires a top-level "position". We calculate it here.
      // Your custom, logical position should be saved safely inside `node.data.position`
      position: {
        x: nodeWithPosition.x - nodeWidth / 2,
        y: nodeWithPosition.y - nodeHeight / 2,
      },
    };
  });

  return { nodes: layoutedNodes, edges };
};

export default function App() {
  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);
  const [selectedNodeId, setSelectedNodeId] = useState(null);

  const onConnect = useCallback((params) => setEdges((eds) => 
    addEdge({...params, animated: true}, eds)), 
    [setEdges]
  );

  // Trigger to manually snap the current nodes into a tree structure
  const onLayout = useCallback(() => {
    const { nodes: layoutedNodes, edges: layoutedEdges } = getLayoutedElements(nodes, edges);
    setNodes([...layoutedNodes]);
    setEdges([...layoutedEdges]);
  }, [nodes, edges, setNodes, setEdges]);

  const onAddNode = useCallback(() => {
    setNodes((prevNodes) => {
      const numericIds = prevNodes
        .map((n) => parseInt(n.id, 10))
        .filter((id) => !Number.isNaN(id));

      const nextId = numericIds.length > 0 ? Math.max(...numericIds) + 1 : 1;
      const offset = (prevNodes.length * 30) % 180;

      const newNode = {
        id: String(nextId),
        type: 'sphere',
        // This is a temporary visual placement; clicking "Auto-Layout" will snap it into the tree
        position: { x: 200 + offset, y: 150 + offset },
        data: { 
          label: `Node ${nextId}`,
          // STORE YOUR ACTUAL DOMAIN POSITION HERE so it ignores ReactFlow's layout
          position: { x: 0, y: 0, z: 0 } 
        },
      };

      return [...prevNodes, newNode];
    });
  }, [setNodes]);

  const onNodeClick = useCallback((_, node) => {
    setSelectedNodeId(node.id);
  }, []);

  const onPaneClick = useCallback(() => {
    setSelectedNodeId(null);
  }, []);

  const onUpdateNode = useCallback((id, updates) => {
    setNodes((nds) =>
      nds.map((node) => {
        if (node.id !== id) return node;
        return {
          ...node,
          ...updates,
          position: updates.position ? { ...node.position, ...updates.position } : node.position,
          data: updates.data ? { ...node.data, ...updates.data } : node.data,
        };
      })
    );
  }, [setNodes]);

  const onDeleteNode = useCallback((id) => {
    setNodes((nds) => nds.filter((node) => node.id !== id));
    setEdges((eds) => eds.filter((edge) => edge.source !== id && edge.target !== id));
    setSelectedNodeId(null);
  }, [setNodes, setEdges]);

  const onExport = async () => {
    const zip = new JSZip();
    const assetsFolder = zip.folder("assets");
    
    // Deep copy nodes before exporting
    const exportNodes = JSON.parse(JSON.stringify(nodes)).map(node => {
      delete node.position; 
      delete node.selected;
      delete node.dragging;
      delete node.width;
      delete node.height;

      return node;
    });

    exportNodes.forEach(async (node) => {
      if (node.data?.content?.type === "image") {
        const path = node.data.content.path; 
        const blobUrl = node.data.content.blobUrl;
        
        if (blobUrl) {
          const response = await fetch(blobUrl);
          const blobData = await response.blob();
          const fileName = path.replace('assets/', '');
          assetsFolder.file(fileName, blobData);
          delete node.data.content.blobUrl;
        } 
        else if (path && path.startsWith('data:')) {
          const [header, base64Data] = path.split(',');
          const mime = header.split(':')[1].split(';')[0];
          const ext = getExtFromMime(mime);
          const fileName = `node_${node.id}.${ext}`;

          assetsFolder.file(fileName, base64Data, { base64: true });
          node.data.content.path = `assets/${fileName}`;
        }
      }
    });

    const payload = convertReactFlowToSaveFile({
      nodes: exportNodes,
      edges: edges
    });

    zip.file("nodes.json", JSON.stringify(payload, null, 2));

    const zipBlob = await zip.generateAsync({ type: 'blob' });

    if (window.electronAPI) {
      const arrayBuffer = await zipBlob.arrayBuffer();
      const result = await window.electronAPI.saveFile({
        data: new Uint8Array(arrayBuffer),
        filename: 'export.nodevision',
        extension: 'nodevision'
      });

      if (result.success) console.log('Exported to: ', result.filePath);
      else console.log('Export failed: ', result.error || result.message);
    } else {
      downloadFileFallback(zipBlob, 'export.nodevision', 'application/zip');
    }
  }

  const onImport = async () => {
    let fileData;

    nodes.forEach(node => {
      if (node.data?.content?.blobUrl) {
        URL.revokeObjectURL(node.data.content.blobUrl);
      }
    });

    if (window.electronAPI) {
      const result = await window.electronAPI.loadFile({extensions: ['nodevision']});
      if (result.success) fileData = result.data;
      else return;
    } else {
      fileData = await new Promise((resolve) => uploadFileFallbackBinary(resolve));
    }

    if (!fileData) return;

    try {
      const zip = await JSZip.loadAsync(fileData);
      const jsonString = await zip.file("nodes.json").async("string");
      const parsedData = JSON.parse(jsonString);
      const flowData = convertSaveFileToReactFlow(parsedData);

      if (flowData.nodes) {
        for (const node of flowData.nodes) {
          if (node.data?.content?.type == "image") {
            const path = node.data.content.path;
            if (path.startsWith('assets/')) {
              const file = zip.file(path);
              if (file) {
                const blobData = await file.async("blob");
                const objectUrl = URL.createObjectURL(blobData);
                node.data.content.blobUrl = objectUrl;
              }
            }
          }
        }

        // Apply Tree Layout dynamically based on edges during import!
        const { nodes: layoutedNodes, edges: layoutedEdges } = getLayoutedElements(
            flowData.nodes, 
            flowData.edges || []
        );

        setNodes(layoutedNodes);
        if (flowData.edges) setEdges(layoutedEdges);
      }
    } catch (err) {
      console.error(err);
      alert("Failed to parse File: Invalid Format or Corrupted");
    }
  }

  const menuActions = [
    { id: 1, label: "Export File", action: onExport },
    { id: 2, label: "Import File", action: onImport },
    { id: 3, label: "Auto-Layout Tree", action: onLayout } // Added to menu
  ];

  return (
    <div style={{ width: '100vw', height: '100vh', position: "relative", overflow: 'hidden', backgroundColor: '#1a1a2e' }}>
      <div style={{ width: '100%', height: '100%', backgroundColor: '#1a1a2e'}}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onNodeClick={onNodeClick}
          onPaneClick={onPaneClick}
          onConnect={onConnect}
          fitView
        >
          <Panel position='top-left'>
            <div style={{display: 'flex', gap: '8px'}}>
              <Dropdown items={menuActions} />
              <button
                onClick={onAddNode}
                className='dropdown-button'
                style={{ minWidth: 'auto' }}
              >
                  Add Node
              </button>
            </div>
          </Panel>
          <Background color="#ccc" gap={16} />
          <Controls />
        </ReactFlow>
      </div>

      {selectedNodeId && (
          <PropertyPanel
            selectedNode={nodes.find((node) => node.id == selectedNodeId)}
            onUpdateNode={onUpdateNode}
            onDeleteNode={onDeleteNode}
            onClose={() => setSelectedNodeId(null)}
          />
      )}
    </div>
  );
}