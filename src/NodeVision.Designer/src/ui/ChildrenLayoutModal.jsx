import React, { useState, useRef, useEffect, useCallback } from 'react';

// A single draggable child dot
const DraggableChild = ({ childNode, containerRef, onUpdateNode }) => {
  const [isDragging, setIsDragging] = useState(false);
  const [localPos, setLocalPos] = useState({ 
    x: childNode.data?.position?.x || 0, 
    y: childNode.data?.position?.y || 0 
  });

  // Use a ref to track the latest position so we avoid stale closures
  // without needing to put side-effects inside setState!
  const latestPosRef = useRef(localPos);

  // Sync local position if it changes externally
  useEffect(() => {
    if (!isDragging) {
      const newPos = {
        x: childNode.data?.position?.x || 0,
        y: childNode.data?.position?.y || 0
      };
      setLocalPos(newPos);
      latestPosRef.current = newPos;
    }
  }, [childNode.data?.position?.x, childNode.data?.position?.y, isDragging]);

  const handleMouseDown = useCallback((e) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(true);

    const startX = e.clientX;
    const startY = e.clientY;
    const startNodeX = latestPosRef.current.x;
    const startNodeY = latestPosRef.current.y;

    const onMouseMove = (moveEvent) => {
      const rect = containerRef.current.getBoundingClientRect();
      const dx = moveEvent.clientX - startX;
      const dy = moveEvent.clientY - startY;

      // Translate pixel movement into our 1920x1080 coordinate space
      const scaledDx = (dx / rect.width) * 1920;
      const scaledDy = (dy / rect.height) * 1080;

      let newX = Math.round(startNodeX + scaledDx);
      let newY = Math.round(startNodeY + scaledDy);

      // Clamp to screen bounds (0 to 1920, 0 to 1080)
      newX = Math.max(0, Math.min(1920, newX));
      newY = Math.max(0, Math.min(1080, newY));

      const newPos = { x: newX, y: newY };
      
      setLocalPos(newPos);
      latestPosRef.current = newPos; // Keep ref updated for onMouseUp
    };

    const onMouseUp = () => {
      setIsDragging(false);
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
      
      // Safely call onUpdateNode using the ref, completely outside of React's render phase
      onUpdateNode(childNode.id, {
        data: {
          ...childNode.data,
          position: latestPosRef.current
        }
      });
    };

    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
  }, [containerRef, onUpdateNode, childNode.id, childNode.data]);

  // Convert the 1920x1080 coordinates into CSS percentages so it scales cleanly
  const leftPercent = (localPos.x / 1920) * 100;
  const topPercent = (localPos.y / 1080) * 100;

  return (
    <div
      onMouseDown={handleMouseDown}
      style={{
        position: 'absolute',
        left: `${leftPercent}%`,
        top: `${topPercent}%`,
        transform: 'translate(-50%, -50%)',
        width: '40px',
        height: '40px',
        backgroundColor: '#3498db',
        borderRadius: '50%',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: 'white',
        fontSize: '11px',
        fontWeight: 'bold',
        cursor: isDragging ? 'grabbing' : 'grab',
        boxShadow: isDragging ? '0 0 12px rgba(52,152,219,0.8)' : '0 2px 5px rgba(0,0,0,0.5)',
        border: '2px solid #fff',
        zIndex: isDragging ? 10 : 1,
        userSelect: 'none'
      }}
      title={`X: ${localPos.x}, Y: ${localPos.y}`}
    >
      {childNode.data?.label || childNode.id}
    </div>
  );
};

export default function ChildrenLayoutModal({ isOpen, onClose, parentId, nodes, edges, onUpdateNode }) {
  const containerRef = useRef(null);

  if (!isOpen) return null;

  // Find all edges where this node is the source, then get the target nodes
  const childEdgeIds = edges.filter(e => e.source === parentId).map(e => e.target);
  const childNodes = nodes.filter(n => childEdgeIds.includes(n.id));

  return (
    <div style={styles.overlay}>
      <div style={styles.modal}>
        <div style={styles.header}>
          <h2 style={styles.title}>Edit Display Layout (16:9)</h2>
          <button onClick={onClose} style={styles.closeButton}>✕</button>
        </div>

        <p style={styles.subtitle}>
          Drag nodes to position them on a simulated 1920x1080 display.
        </p>

        {/* 16:9 Aspect Ratio Container */}
        <div style={styles.canvasContainer}>
          <div ref={containerRef} style={styles.canvas}>
            
            {/* Guide Lines / Crosshair */}
            <div style={styles.verticalGuide} />
            <div style={styles.horizontalGuide} />

            {childNodes.map(child => (
              <DraggableChild 
                key={child.id} 
                childNode={child} 
                containerRef={containerRef} 
                onUpdateNode={onUpdateNode} 
              />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

const styles = {
  overlay: {
    position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
    backgroundColor: 'rgba(0,0,0,0.7)', backdropFilter: 'blur(3px)',
    display: 'flex', alignItems: 'center', justifyContent: 'center',
    zIndex: 1000
  },
  modal: {
    backgroundColor: '#161626', border: '1px solid #33334d',
    borderRadius: '12px', padding: '24px', width: '80%', maxWidth: '900px',
    boxShadow: '0 10px 30px rgba(0,0,0,0.8)',
    display: 'flex', flexDirection: 'column'
  },
  header: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' },
  title: { margin: 0, color: 'white', fontSize: '20px' },
  closeButton: { background: 'none', border: 'none', color: '#888', cursor: 'pointer', fontSize: '20px' },
  subtitle: { color: '#8b8ba7', marginTop: 0, marginBottom: '24px', fontSize: '14px' },
  
  canvasContainer: {
    width: '100%',
    aspectRatio: '16/9',
    backgroundColor: '#0a0a10',
    border: '2px dashed #33334d',
    borderRadius: '8px',
    position: 'relative',
    overflow: 'hidden'
  },
  canvas: { width: '100%', height: '100%', position: 'relative' },
  verticalGuide: { position: 'absolute', left: '50%', top: 0, bottom: 0, width: '1px', backgroundColor: 'rgba(255,255,255,0.05)', pointerEvents: 'none' },
  horizontalGuide: { position: 'absolute', top: '50%', left: 0, right: 0, height: '1px', backgroundColor: 'rgba(255,255,255,0.05)', pointerEvents: 'none' }
};