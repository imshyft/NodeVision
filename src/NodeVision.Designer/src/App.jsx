import { useState, useCallback, useEffect } from 'react';
import { ReactFlow, addEdge, applyNodeChanges, applyEdgeChanges, Background, Controls, Panel, useEdgesState, useNodesState } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import Node from './ui/Node'; // The component from the previous step
import Dropdown from './ui/Dropdown';
import {uploadFileFallback, downloadFileFallback} from './utils'


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
  const [idTracker, setIdTracker] = useState(1); 

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

  // useEffect(() => {
  //   console.log("Nodes updated");
  //   console.log(nodes)
  // }, [nodes]);

  const onExport = async () => {
    const payload = {
      nodes: nodes,
      edges: edges
    } 
    const fileContent = JSON.stringify(payload, null, 2);
    if (window.electronAPI) {
      const result = await window.electronAPI.saveFile({
        data: fileContent,
        filename: 'export.json',
        extension: 'json'
      });

      if (result.success) {
        console.log('Exported to: ', result.filePath);
      } else {
        console.log('Export failed: ', result.error || result.message);
      }
    } else {
      downloadFileFallback(fileContent, 'project-export.json', 'application/json');
    }
  }

  const onImport = async () => {
    if (window.electronAPI) {
      const result = await window.electronAPI.loadFile({extensions: ['json']});
      
      if (result.success) {
        try {
          const parsedData = JSON.parse(result.data);
          if (parsedData.nodes) {
            // console.log(parsedData)
            setNodes(parsedData.nodes);
          }
          if (parsedData.edges) {
            setEdges(parsedData.edges);
          }
          console.log("Loaded Successfully")
        } catch (err) {
          alert("Failed to parse JSON File: Invalid Format");
        }
      } 
    } else {
      // console.log("Upload fallback called");
      uploadFileFallback((fileContent) => {
        const parsedData = JSON.parse(fileContent);
        setNodes(parsedData.nodes || []);
      })
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
    <div style={{ width: '100%', height: '100%', backgroundColor: '#1a1a2e'}}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
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
  );
}