import React, { useState, useRef, useCallback } from 'react';

export default function PropertyPanel({ selectedNode, onUpdateNode, onDeleteNode, onClose }) {
  const [panelWidth, setPanelWidth] = useState(320);
  const isDraggingRef = useRef(false);
  const fileInputRef = useRef(null); 

  const handleMouseDown = useCallback((e) => {
    e.preventDefault();
    isDraggingRef.current = true;
    const startX = e.clientX;
    const startWidth = panelWidth;

    const onMouseMove = (moveEvent) => {
      if (!isDraggingRef.current) return;
      const deltaX = startX - moveEvent.clientX;
      setPanelWidth(Math.min(Math.max(startWidth + deltaX, 260), 650)); 
    };

    const onMouseUp = () => {
      isDraggingRef.current = false;
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
    };

    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
  }, [panelWidth]);

  if (!selectedNode) return null;

  const { id, data } = selectedNode;
  const content = data?.content || { type: 'text', value: data?.label || '' };

  const handleLabelChange = (e) => {
    const newLabel = e.target.value;
    onUpdateNode(id, {
      data: {
        ...data,
        label: newLabel,
        content: content.type === 'text' ? { ...content, value: newLabel } : content,
      },
    });
  };

  const handleContentTypeChange = (e) => {
    const newType = e.target.value;
    onUpdateNode(id, {
      data: {
        ...data,
        content: newType === 'text'
          ? { type: 'text', value: data?.label || '' }
          : { type: 'image', path: '' },
      },
    });
  };

  const handleContentValueChange = (field, value) => {
    onUpdateNode(id, {
      data: {
        ...data,
        content: {
          ...content,
          [field]: value,
        },
      },
    });
  };

  // --- NEW SINGLE IMAGE UPLOAD LOGIC ---
  const handleImageUpload = (e) => {
    const file = e.target.files[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onloadend = () => {
      const base64String = reader.result;
      onUpdateNode(id, {
        data: {
          ...data,
          content: {
            type: "image",
            path: base64String, // Saves base64 directly to path for exporting
            blobUrl: null       // Clears any old zip blobUrl since we have a new file
          }
        }
      });
    };
    reader.readAsDataURL(file);
    
    if (fileInputRef.current) fileInputRef.current.value = '';
  };

  return (
    <aside style={{ ...styles.panel, width: `${panelWidth}px` }}>
      
      <div style={styles.resizer} onMouseDown={handleMouseDown} title="Drag to resize panel">
        <div style={styles.resizerGrip} />
      </div>

      <div style={styles.header}>
        <h3 style={styles.title}>Node Properties</h3>
        <button onClick={onClose} style={styles.closeBtn}>✕</button>
      </div>

      <div style={styles.section}>
        <label style={styles.label}>Label</label>
        <input
          style={styles.input}
          type="text"
          value={data?.label || ''}
          onChange={handleLabelChange}
        />
      </div>

      <div style={styles.section}>
        <label style={styles.label}>Content Type</label>
        <select
          style={styles.select}
          value={content.type || 'text'}
          onChange={handleContentTypeChange}
        >
          <option value="text">Text Node</option>
          <option value="image">Image Node</option>
        </select>
      </div>

      {/* --- CONDITIONAL RENDERING BASED ON TYPE --- */}
      {content.type === 'text' ? (
        <div style={styles.section}>
          <label style={styles.label}>Text Value</label>
          <textarea
            style={styles.textarea}
            rows={4}
            value={content.value || ''}
            onChange={(e) => handleContentValueChange('value', e.target.value)}
          />
        </div>
      ) : (
        <div style={styles.section}>
          <div style={styles.mediaHeader}>
            <label style={styles.label}>Image File</label>
            <button style={styles.uploadBtn} onClick={() => fileInputRef.current?.click()}>
              Upload Image
            </button>
            <input 
              type="file" 
              accept="image/*"
              ref={fileInputRef}
              style={{ display: 'none' }}
              onChange={handleImageUpload}
            />
          </div>

          {/* Show the image preview if a path or blobUrl exists */}
          {(content.blobUrl || content.path) ? (
            <div style={styles.imageContainer}>
              <img 
                src={content.blobUrl || content.path} 
                alt="Node Graphic" 
                style={styles.thumbnail} 
              />
            </div>
          ) : (
            <div style={styles.emptyImageState}>
              No image uploaded
            </div>
          )}
        </div>
      )}

      <div style={styles.footer}>
        <button onClick={() => onDeleteNode(id)} style={styles.deleteBtn}>
          Delete Node
        </button>
      </div>
    </aside>
  );
}

const styles = {
  panel: {
    position: 'absolute',     
    top: '16px', right: '16px',            
    height: 'calc(100% - 32px)', 
    backgroundColor: '#161626',
    border: '1px solid #2a2a40', 
    borderRadius: '10px',        
    boxShadow: '-4px 4px 15px rgba(0,0,0,0.5)', 
    color: '#e0e0e0',
    display: 'flex', flexDirection: 'column',
    padding: '16px', boxSizing: 'border-box',
    zIndex: 10, overflowY: 'auto',
  },
  resizer: {
    position: 'absolute', top: 0, left: 0,
    width: '8px', height: '100%', cursor: 'col-resize',
    display: 'flex', alignItems: 'center', justifyContent: 'center',
    zIndex: 20, userSelect: 'none',
  },
  resizerGrip: {
    width: '2px', height: '32px',
    backgroundColor: '#3b3b5c', borderRadius: '2px',
  },
  header: {
    display: 'flex', justifyContent: 'space-between', alignItems: 'center',
    marginBottom: '16px', borderBottom: '1px solid #2a2a40', paddingBottom: '8px',
  },
  title: { margin: 0, fontSize: '16px', fontWeight: '600', color: '#ffffff' },
  closeBtn: { background: 'none', border: 'none', color: '#888', cursor: 'pointer', fontSize: '16px' },
  section: { marginBottom: '14px', display: 'flex', flexDirection: 'column', gap: '6px' },
  label: { fontSize: '12px', textTransform: 'uppercase', letterSpacing: '0.5px', color: '#8b8ba7' },
  input: {
    width: '100%', backgroundColor: '#202034', border: '1px solid #33334d',
    borderRadius: '4px', color: '#ffffff', padding: '6px 8px', fontSize: '13px',
    outline: 'none', boxSizing: 'border-box',
  },
  select: {
    width: '100%', backgroundColor: '#202034', border: '1px solid #33334d',
    borderRadius: '4px', color: '#ffffff', padding: '6px 8px', fontSize: '13px',
    outline: 'none',
  },
  textarea: {
    width: '100%', backgroundColor: '#202034', border: '1px solid #33334d',
    borderRadius: '4px', color: '#ffffff', padding: '6px 8px', fontSize: '13px',
    outline: 'none', resize: 'vertical', boxSizing: 'border-box',
  },
  
  // New Media Styles
  mediaHeader: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
  uploadBtn: { 
    background: '#3498db', border: 'none', color: '#ffffff', 
    padding: '4px 10px', borderRadius: '4px', fontSize: '11px', 
    cursor: 'pointer', transition: 'background 0.2s', fontWeight: 'bold'
  },
  imageContainer: { 
    position: 'relative', borderRadius: '4px', overflow: 'hidden', 
    border: '1px solid #33334d', background: '#000', marginTop: '8px'
  },
  thumbnail: { 
    width: '100%', height: 'auto', maxHeight: '200px', 
    objectFit: 'contain', display: 'block' 
  },
  emptyImageState: {
    width: '100%', padding: '32px 0', marginTop: '8px',
    backgroundColor: '#1c1c2b', border: '1px dashed #33334d',
    borderRadius: '4px', color: '#666', fontSize: '12px',
    textAlign: 'center'
  },

  footer: { marginTop: 'auto', paddingTop: '16px', borderTop: '1px solid #2a2a40' },
  deleteBtn: {
    width: '100%', backgroundColor: '#d9383a', border: 'none', borderRadius: '4px',
    color: '#ffffff', padding: '8px', cursor: 'pointer', fontWeight: 'bold', fontSize: '13px',
  },
};