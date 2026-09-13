import { useState, useCallback, useEffect } from 'react';
import { ReactFlow, addEdge, applyNodeChanges, applyEdgeChanges, Background, Controls, Panel, useEdgesState, useNodesState } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import Node from './ui/Node'; // The component from the previous step
import Dropdown from './ui/Dropdown';
import PropertyPanel from './ui/PropertyPanel';
import {uploadFileFallback, downloadFileFallback, convertReactFlowToSaveFile, convertSaveFileToReactFlow, getExtFromMime, uploadFileFallbackBinary, downloadFileFallbackBinary} from './utils'
import JSZip from 'jszip';


// Register custom nodes
const nodeTypes = { sphere: Node };

// Initial setup with two spherical nodes connected by an edge
const initialNodes = [
  // { id: '1', type: 'sphere', position: { x: 250, y: 100 }, data: { label: 'Node 1' } },
  // { id: '2', type: 'sphere', position: { x: 250, y: 300 }, data: { label: 'Node 2' } },
];

const initialEdges = [
  // { id: 'e1-2', source: '1', target: '2', animated: true },
];

export default function App() {
  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);
  const [selectedNodeId, setSelectedNodeId] = useState(null);

  // const onNodesChange = useCallback((changes) => setNodes((nds) => applyNodeChanges(changes, nds)), []);
  // const onEdgesChange = useCallback((changes) => setEdges((eds) => applyEdgeChanges(changes, eds)), []);
  const onConnect = useCallback((params) => setEdges((eds) => 
    addEdge({...params, animated: true}, eds)), 
    [setEdges]
  );

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
        position: { x: 200 + offset, y: 150 + offset },
        data: { label: `Node ${nextId}` },
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

  // Remove node and dependent edges
  const onDeleteNode = useCallback((id) => {
    setNodes((nds) => nds.filter((node) => node.id !== id));
    setEdges((eds) => eds.filter((edge) => edge.source !== id && edge.target !== id));
    setSelectedNodeId(null);
  }, [setNodes, setEdges]);

  const onExport = async () => {
    const zip = new JSZip();
    const assetsFolder = zip.folder("assets");

    const exportNodes = JSON.parse(JSON.stringify(nodes));

    console.log(nodes);

    exportNodes.forEach(async (node) => {
      if (node.data?.content?.type === "image") {
        const path = node.data.content.path; 
        const blobUrl = node.data.content.blobUrl;
        
        // CASE A: It was imported from a previous ZIP (it has a blobUrl)
        if (blobUrl) {
          // Fetch the binary data directly from the RAM URL
          const response = await fetch(blobUrl);
          const blobData = await response.blob();
          
          // Re-add it to the new ZIP
          const fileName = path.replace('assets/', '');
          assetsFolder.file(fileName, blobData);
          
          // Clean up the payload so the blobUrl doesn't get saved into the JSON text
          delete node.data.content.blobUrl;
        } 
        
        // CASE B: It's a brand new upload (it's a Base64 string)
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
    // const fileContent = JSON.stringify(payload, null, 2);

    const zipBlob = await zip.generateAsync({ type: 'blob' });

    if (window.electronAPI) {
      const arrayBuffer = await zipBlob.arrayBuffer();
      const result = await window.electronAPI.saveFile({
        data: new Uint8Array(arrayBuffer),
        filename: 'export.nodevision',
        extension: 'nodevision'
      });

      if (result.success) {
        console.log('Exported to: ', result.filePath);
      } else {
        console.log('Export failed: ', result.error || result.message);
      }
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

      // if (result.success) {
      //   try {
      //     const parsedData = JSON.parse(result.data);
      //     const flowData = convertSaveFileToReactFlow(parsedData);
      //     if (flowData.nodes) {
      //       // console.log(flowData)
      //       setNodes(flowData.nodes);
      //     }
      //     if (flowData.edges) {
      //       setEdges(flowData.edges);
      //     }
      //     console.log("Loaded Successfully")
      //   } catch (err) {
      //     alert("Failed to parse JSON File: Invalid Format");
      //   }
      // } 

      if (result.success) {
        fileData = result.data;
      } else {
        return;
      }
    } else {
      // console.log("Upload fallback called");
      fileData = await new Promise((resolve) => uploadFileFallbackBinary(resolve));
    }

    if (!fileData) return;

    try {
      const zip = await JSZip.loadAsync(fileData);

      const jsonString = await zip.file("nodes.json").async("string");
      const parsedData = JSON.parse(jsonString);
      const flowData = convertSaveFileToReactFlow(parsedData);

      if (flowData.nodes) {
        const newlyLoadedMedia = [];

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

        setNodes(flowData.nodes);
      }
      if (flowData.edges) {
        setEdges(flowData.edges);
      }
    } catch (err) {
      console.error(err);
      alert("Failed to parse File: Invalid Format or Corrupted");
    }
  }

  const menuActions = [
    {
      id: 1,
      label: "Export File",
      action: onExport
    },
    {
      id: 2,
      label: "Import File",
      action: onImport
    }
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
              <Dropdown 
                items={menuActions}
              >

              </Dropdown>
              <button

              onClick={onAddNode}
                className='dropdown-button'
                style={{
                    minWidth: 'auto'
                }}
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