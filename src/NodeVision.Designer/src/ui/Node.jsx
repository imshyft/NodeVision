import { Handle, Position, useReactFlow, useNodeConnections } from '@xyflow/react';
import { useState, useCallback } from 'react';

export default function Node({ id, data, selected }) {
  const [nodeName, setNodeName] = useState(data.label || '');
  const [isEditing, setIsEditing] = useState(false);
  const { updateNodeData } = useReactFlow();

  // Get all outgoing edges from this specific node
  const sourceConnections = useNodeConnections({ 
    // id: id,
    handleType: 'source' 
  });
  
  // If there are no outgoing connections, it's a leaf node!
  const isLeaf = sourceConnections.length === 0;

  // console.log(`Node ${nodeName} have ${sourceConnections.length} out going edges`)

  const handleBlurOrSubmit = useCallback(() => {
    setIsEditing(false);
    updateNodeData(id, {label: nodeName});
  }, [id, nodeName, updateNodeData]);

  const handleKeyDown = (e) => {
    if (e.key === 'Enter') {
      handleBlurOrSubmit();
    } else if (e.key === 'Escape') {
      setNodeName(data.label || '');
      setIsEditing(false);
    }
  }

  // --- DYNAMIC STYLES BASED ON NODE TYPE ---
  const backgroundColor = isLeaf ? '#16a085' : '#2c3e50'; // Greenish for leaf, Dark Blue for intermediate
  const defaultBorder = isLeaf ? '#1abc9c' : '#34495e';
  const borderRadius = isLeaf ? '16px' : '50%'; // Rounded square for leaf, Circle for intermediate

  return (
    <div style={{ 
      width: '80px', height: '80px', 
      borderRadius: borderRadius, // Dynamic Shape
      background: backgroundColor, // Dynamic Color
      color: 'white',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      border: selected ? 
        '2px solid #38bdf8': 
        `2px solid ${defaultBorder}`,
      boxShadow: selected ?
        '0 0 16px 2px rgba(56, 189, 248, 0.65)' :
        '0 2px 6px rgba(0, 0, 0, 0.3)',
      transform: selected ? 'scale(1.06)' : 'scale(1)',
      transition: 'all 0.2s ease-in-out' // Smooth transition when becoming a leaf/intermediate
    }}>
      {/* Target endpoint (Incoming) */}
      <Handle type="target" position={Position.Top} style={{ background: '#e74c3c' }} />
      
      {
        isEditing ? (
          <input
            type="text"
            value={nodeName}
            onChange={(e) => setNodeName(e.target.value)}
            onBlur={handleBlurOrSubmit}
            onKeyDown={handleKeyDown}
            autoFocus
            className="nodrag"
            style={{
              width: '60px',
              fontSize: '11px',
              textAlign: 'center',
              background: '#1a252f',
              color: 'white',
              border: '1px solid #3498db',
              borderRadius: '4px',
              padding: '2px',
              outline: 'none',
            }}
          />
        ) : (
          <div 
            style={{ fontSize: '12px' }}
            onDoubleClick={() => setIsEditing(true)}
          >
            {data.label}
          </div>
        )
      }
      
      {/* Source endpoint (Outgoing) */}
      {/* We keep this visible even on leaves, so the user can drag a new connection out of it! */}
      <Handle type="source" position={Position.Bottom} style={{ background: '#2ecc71' }} />
    </div>
  );
}