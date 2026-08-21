import { useState, useCallback, useEffect } from 'react';
import { ReactFlow, addEdge, applyNodeChanges, applyEdgeChanges, Background, Controls, Panel, useEdgesState, useNodesState } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import Node from './ui/Node'; // The component from the previous step

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

  useEffect(() => {
    console.log("Nodes updated");
    console.log(nodes)
  }, [nodes]);

  return (
    <div style={{ width: '100%', height: '100%', backgroundColor: '#1a1a2e' }}>
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
            <button
            onClick={onAddNode}
            style={{
                padding: '8px 16px',
                border: 'none',
                borderRadius: '4px',
                cursor: 'pointer',
                fontWeight: 'bold'
            }}
            >
                Add Node
            </button>
        </Panel>
        <Background color="#ccc" gap={16} />
        <Controls />
      </ReactFlow>
    </div>
  );
}