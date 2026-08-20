import { Handle, Position, useReactFlow } from '@xyflow/react';
import { useState, useCallback } from 'react';

export default function Node({ id, data, selected }) {
  const [nodeName, setNodeName] = useState(data.label || '');
  const [isEditing, setIsEditing] = useState(false);
  const { updateNodeData } = useReactFlow();

  const handleBlurOrSubmit = useCallback(() => {
    setIsEditing(false),
    updateNodeData(id, {label: nodeName})
  })

  const handleKeyDown = (e) => {
    if (e.key == 'Enter') {
      handleBlurOrSubmit();
    } else if (e.key == 'Escape') {
      setNodeName(data.label || '');
      setIsEditing(false)
    }
  }

  return (
    <div style={{ 
      width: '80px', height: '80px', borderRadius: '50%', 
      background: '#2c3e50', color: 'white',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      border: selected ? 
        '2px solid #38bdf8': 
        '2px solid #34495e',
      boxShadow: selected ?
        '0 0 16px 2px rgba(56, 189, 248, 0.65)' :
        '0 2px 6px rgba(0, 0, 0, 0.3)',
      transform: selected ? 'scale(1.06)' : 'scale(1)',
    }}>
      {/* Target endpoint */}
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
            className="nodrag" // Prevents dragging node while typing or selecting text
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
      
      {/* Source endpoint */}
      <Handle type="source" position={Position.Bottom} style={{ background: '#2ecc71' }} />
    </div>
  );
}