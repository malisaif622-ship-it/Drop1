import React, { useState, useEffect } from "react";
import "./Sidebar.css";
import apiService from "../services/api";

function Sidebar({ activeView, onViewChange, refreshTrigger }) {
  const [storageInfo, setStorageInfo] = useState({
    usedStorage: 0,
    totalStorage: 1000, // Default 1GB
    percentage: 0
  });

  useEffect(() => {
    loadStorageInfo();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Refresh storage info when files are uploaded/deleted
  useEffect(() => {
    if (refreshTrigger > 0) {
      loadStorageInfo();
    }
  }, [refreshTrigger]);

  const loadStorageInfo = async () => {
    try {
      console.log("Loading storage info from /auth/me endpoint...");
      const response = await apiService.getUserStorageInfo();
      console.log("Storage API response status:", response.status);
      
      if (response.ok) {
        const userData = await response.json();
        console.log("✅ Storage API success! User data:", userData);
        console.log("User data properties:", Object.keys(userData));
        
        // Try different property names that might contain storage data
        const usedMB = userData.UsedStorageMB || userData.usedStorageMB || 
                      userData.StorageUsed || userData.storageUsed || 0;
        const totalMB = userData.TotalStorageMB || userData.totalStorageMB || 
                       userData.StorageLimit || userData.storageLimit || 1000;
        
        console.log("Extracted storage values - Used:", usedMB, "Total:", totalMB);
        
        const percentage = totalMB > 0 ? (usedMB / totalMB) * 100 : 0;
        
        const newStorageInfo = {
          usedStorage: usedMB,
          totalStorage: totalMB,
          percentage: Math.min(percentage, 100)
        };
        
        console.log("Setting new storage info:", newStorageInfo);
        setStorageInfo(newStorageInfo);
      } else {
        const errorText = await response.text();
        console.error("❌ Storage API failed:", response.status, errorText);
      }
    } catch (err) {
      console.error("❌ Storage API error:", err);
    }
  };

  const formatStorage = (mb) => {
    if (mb >= 1024) {
      return `${(mb / 1024).toFixed(2)}GB`;
    }
    return `${mb.toFixed(2)}MB`;
  };

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
        <div className="storage-text">
          Storage Used: {formatStorage(storageInfo.usedStorage)} / {formatStorage(storageInfo.totalStorage)}
        </div>
        <div className="storage-bar">
          <div 
            className="storage-fill" 
            style={{ width: `${storageInfo.percentage}%` }}
          ></div>
        </div>
      </div>
    </aside>
  );
}

export default Sidebar;
