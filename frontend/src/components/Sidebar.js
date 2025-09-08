import React from "react";
import "./Sidebar.css";

function Sidebar({ activeView, onViewChange }) {
  return (
    <aside className="sidebar">
      <div className="sidebar-logo">Drop1</div>
      <nav className="sidebar-nav">
        <button 
          className={`sidebar-btn ${activeView === 'files' ? 'active' : ''}`}
          onClick={() => onViewChange('files')}
        >
          📁 Files
        </button>
        <button 
          className={`sidebar-btn ${activeView === 'recycle' ? 'active' : ''}`}
          onClick={() => onViewChange('recycle')}
        >
          🗑️ Recycle Bin
        </button>
      </nav>
      <div className="storage-section">
        <div className="storage-text">Storage Used: 120MB / 200MB</div>
        <div className="storage-bar">
          <div className="storage-fill" style={{ width: "60%" }}></div>
        </div>
      </div>
    </aside>
  );
}

export default Sidebar;
