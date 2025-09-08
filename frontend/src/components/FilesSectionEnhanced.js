import React, { useState, useEffect } from "react";
import "./FilesSection.css";
import apiService from "../services/api";
import ConfirmDialog from "./ConfirmDialog";

function FilesSection({ activeView = "files", searchResults = null }) {
  const [files, setFiles] = useState([]);
  const [folders, setFolders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [activeMenu, setActiveMenu] = useState(null);
  const [showRenameModal, setShowRenameModal] = useState(null);
  const [showConfirmDialog, setShowConfirmDialog] = useState(null);
  const [newName, setNewName] = useState("");

  useEffect(() => {
    if (searchResults) {
      // Display search results
      console.log("Displaying search results:", searchResults);
      setFolders(searchResults.Folders || searchResults.folders || []);
      setFiles(searchResults.Files || searchResults.files || []);
      setLoading(false);
    } else {
      // Load normal data
      loadData();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeView, searchResults]);

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event) => {
      if (!event.target.closest('.file-menu') && !event.target.closest('.dropdown-menu')) {
        setActiveMenu(null);
      }
    };

    document.addEventListener('click', handleClickOutside);
    return () => document.removeEventListener('click', handleClickOutside);
  }, []);

  const loadData = async () => {
    setLoading(true);
    setError("");
    try {
      let data = { folders: [], files: [] };
      
      if (activeView === "files") {
        console.log("Loading all files and folders using GetAllItems...");
        
        const response = await apiService.getAllItems();
        console.log("GetAllItems response status:", response.status);
        
        if (response.ok) {
          data = await response.json();
          console.log("GetAllItems data received:", data);
        } else {
          const errorText = await response.text();
          console.error("GetAllItems failed:", response.status, errorText);
          setError(`Failed to load files: ${response.status} - ${errorText}`);
        }
      } else if (activeView === "recycle") {
        console.log("Loading deleted items...");
        
        const response = await apiService.getDeletedItems();
        console.log("GetDeletedItems response status:", response.status);
        
        if (response.ok) {
          data = await response.json();
          console.log("GetDeletedItems data received:", data);
        } else {
          const errorText = await response.text();
          console.error("GetDeletedItems failed:", response.status, errorText);
          setError(`Failed to load deleted items: ${response.status} - ${errorText}`);
        }
      }
      
      // Handle different response formats
      setFolders(data.Folders || data.folders || []);
      setFiles(data.Files || data.files || []);
      
      console.log("Final loaded folders:", data.Folders || data.folders || []);
      console.log("Final loaded files:", data.Files || data.files || []);
    } catch (err) {
      console.error("Error loading data:", err);
      setError(`Failed to load data: ${err.message}`);
    } finally {
      setLoading(false);
    }
  };

  const handleMenuClick = (itemId, itemType, event) => {
    event.stopPropagation();
    setActiveMenu(activeMenu === `${itemType}-${itemId}` ? null : `${itemType}-${itemId}`);
  };

  const handleRename = (item, itemType) => {
    setShowRenameModal({ item, itemType });
    setNewName(itemType === 'folder' ? item.FolderName : item.FileName);
    setActiveMenu(null);
  };

  const handleRenameSubmit = async () => {
    if (!showRenameModal || !newName.trim()) return;
    
    try {
      const { item, itemType } = showRenameModal;
      let response;
      
      if (itemType === 'folder') {
        response = await apiService.renameFolder(item.FolderID, newName.trim());
      } else {
        response = await apiService.renameFile(item.FileID, newName.trim());
      }
      
      if (response.ok) {
        alert("Renamed successfully!");
        loadData(); // Refresh the list
      } else {
        const error = await response.text();
        alert(`Error: ${error}`);
      }
    } catch (err) {
      console.error("Rename error:", err);
      alert("Failed to rename");
    } finally {
      setShowRenameModal(null);
      setNewName("");
    }
  };

  const handleDelete = async (item, itemType) => {
    setShowConfirmDialog({
      title: `Delete ${itemType}`,
      message: `Are you sure you want to delete this ${itemType}?`,
      onConfirm: () => confirmDelete(item, itemType),
      onCancel: () => setShowConfirmDialog(null)
    });
    setActiveMenu(null);
  };

  const confirmDelete = async (item, itemType) => {
    setShowConfirmDialog(null);
    
    try {
      let response;
      if (itemType === 'folder') {
        response = await apiService.deleteFolder(item.FolderID);
      } else {
        response = await apiService.deleteFile(item.FileID);
      }
      
      if (response.ok) {
        alert("Moved to recycle bin successfully!");
        loadData(); // Refresh the list
      } else {
        const error = await response.text();
        alert(`Error: ${error}`);
      }
    } catch (err) {
      console.error("Delete error:", err);
      alert("Failed to delete");
    }
  };

  const handleRecover = async (item, itemType) => {
    try {
      let response;
      if (itemType === 'folder') {
        response = await apiService.recoverFolder(item.FolderID);
      } else {
        response = await apiService.recoverFile(item.FileID);
      }
      
      if (response.ok) {
        alert("Recovered successfully!");
        loadData(); // Refresh the list
      } else {
        const error = await response.text();
        alert(`Error: ${error}`);
      }
    } catch (err) {
      console.error("Recover error:", err);
      alert("Failed to recover");
    }
    setActiveMenu(null);
  };

  const handleDetails = (item, itemType) => {
    // Show item details
    const details = itemType === 'folder' 
      ? `Folder: ${item.FolderName}\nCreated: ${item.CreatedAt}`
      : `File: ${item.FileName}.${item.FileType}\nSize: ${item.FileSizeMB} MB\nUploaded: ${item.UploadedAt}`;
    alert(details);
    setActiveMenu(null);
  };

  const getViewTitle = () => {
    if (searchResults) {
      return "Search Results";
    }
    switch (activeView) {
      case "recycle": return "Recycle Bin";
      default: return "Your Files & Folders";
    }
  };

  const testApiDirectly = async () => {
    console.log("=== Direct API Test ===");
    setError(""); // Clear any existing errors
    
    try {
      // Test 1: Check authentication
      console.log("1. Testing authentication...");
      const authResponse = await apiService.getCurrentUser();
      console.log("Auth test - Status:", authResponse.status);
      if (authResponse.ok) {
        const user = await authResponse.json();
        console.log("✅ Authenticated user:", user);
      } else {
        console.log("❌ Not authenticated - Status:", authResponse.status);
        const authError = await authResponse.text();
        console.log("Auth error:", authError);
        setError(`Authentication failed: ${authResponse.status} - ${authError}`);
        return;
      }

      // Test 2: Test GetAllItems endpoint
      console.log("2. Testing GetAllItems endpoint...");
      
      try {
        const response = await apiService.getAllItems();
        console.log("GetAllItems status:", response.status);
        
        if (response.ok) {
          const data = await response.json();
          console.log("✅ GetAllItems SUCCESS! Data:", data);
          console.log("Data structure:", Object.keys(data));
          
          // Try to load this data into the component
          const folders = data.Folders || data.folders || [];
          const files = data.Files || data.files || [];
          
          console.log("Extracted folders:", folders);
          console.log("Extracted files:", files);
          
          setFolders(folders);
          setFiles(files);
          setError(""); // Clear any errors
          
          alert(`✅ Success! Found ${folders.length} folders and ${files.length} files using GetAllItems endpoint`);
        } else {
          const errorText = await response.text();
          console.log("❌ GetAllItems failed:", errorText);
          setError(`GetAllItems failed: ${response.status} - ${errorText}`);
        }
      } catch (err) {
        console.log("❌ GetAllItems error:", err.message);
        setError(`GetAllItems error: ${err.message}`);
      }
      
    } catch (err) {
      console.error("Direct API test error:", err);
      setError(`API test failed: ${err.message}`);
    }
  };

  if (loading) return <div className="files-section">Loading...</div>;
  if (error) return <div className="files-section">
    <div style={{color: 'red', padding: '20px'}}>
      <h3>Error: {error}</h3>
      <button onClick={loadData} style={{marginTop: '10px', padding: '5px 10px'}}>
        Retry
      </button>
    </div>
  </div>;

  return (
    <div className="files-section">
      <h3>{getViewTitle()}</h3>
      
      {/* Test API Button */}
      <button 
        onClick={testApiDirectly}
        style={{
          background: '#007acc', 
          color: 'white', 
          border: 'none', 
          padding: '8px 16px', 
          borderRadius: '4px',
          cursor: 'pointer',
          marginBottom: '10px'
        }}
      >
        🔍 Test API Endpoints
      </button>
      
      {/* Debug information */}
      <div style={{background: '#f0f0f0', padding: '10px', margin: '10px 0', fontSize: '12px'}}>
        <strong>Debug Info:</strong><br/>
        Active View: {activeView}<br/>
        Folders Count: {folders.length}<br/>
        Files Count: {files.length}<br/>
        Loading: {loading.toString()}<br/>
        Error: {error || 'None'}
      </div>
      
      <div className="files-grid">
        {folders.map((folder) => (
          <div key={`folder-${folder.FolderID || folder.id}`} className="file-card folder">
            <div className="file-icon">📁</div>
            <div className="file-name">{folder.FolderName || folder.name}</div>
            <div 
              className="file-menu" 
              onClick={(e) => handleMenuClick(folder.FolderID || folder.id, 'folder', e)}
            >
              ⋮
            </div>
            {activeMenu === `folder-${folder.FolderID || folder.id}` && (
              <div className="dropdown-menu">
                <button onClick={() => handleRename(folder, 'folder')}>Rename</button>
                {activeView === "recycle" ? (
                  <button onClick={() => handleRecover(folder, 'folder')}>Recover</button>
                ) : (
                  <button onClick={() => handleDelete(folder, 'folder')}>Delete</button>
                )}
                <button onClick={() => handleDetails(folder, 'folder')}>Details</button>
              </div>
            )}
          </div>
        ))}
        
        {files.map((file) => (
          <div key={`file-${file.FileID || file.id}`} className="file-card file">
            <div className="file-icon">📄</div>
            <div className="file-name">{(file.FileName || file.name) + (file.FileType ? '.' + file.FileType : '')}</div>
            <div 
              className="file-menu" 
              onClick={(e) => handleMenuClick(file.FileID || file.id, 'file', e)}
            >
              ⋮
            </div>
            {activeMenu === `file-${file.FileID || file.id}` && (
              <div className="dropdown-menu">
                <button onClick={() => handleRename(file, 'file')}>Rename</button>
                {activeView === "recycle" ? (
                  <button onClick={() => handleRecover(file, 'file')}>Recover</button>
                ) : (
                  <button onClick={() => handleDelete(file, 'file')}>Delete</button>
                )}
                <button onClick={() => handleDetails(file, 'file')}>Details</button>
              </div>
            )}
          </div>
        ))}
      </div>

      {/* Rename Modal */}
      {showRenameModal && (
        <div className="modal-overlay">
          <div className="modal">
            <h3>Rename {showRenameModal.itemType}</h3>
            <input
              type="text"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="Enter new name"
            />
            <div className="modal-buttons">
              <button onClick={handleRenameSubmit}>Rename</button>
              <button onClick={() => setShowRenameModal(null)}>Cancel</button>
            </div>
          </div>
        </div>
      )}

      {/* Confirm Dialog */}
      {showConfirmDialog && (
        <ConfirmDialog
          isOpen={true}
          title={showConfirmDialog.title}
          message={showConfirmDialog.message}
          onConfirm={showConfirmDialog.onConfirm}
          onCancel={showConfirmDialog.onCancel}
        />
      )}
    </div>
  );
}

export default FilesSection;
