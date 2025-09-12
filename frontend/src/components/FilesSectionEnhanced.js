import React, { useState, useEffect } from "react";
import "./FilesSection.css";
import apiService from "../services/api";
import ConfirmDialog from "./ConfirmDialog";

function FilesSection({ 
  activeView = "files", 
  searchResults = null, 
  currentFolderId = null, 
  folderPath = [],
  refreshTrigger = 0,
  onFolderDoubleClick,
  onBackToParent,
  onBreadcrumbClick,
  onRefresh
}) {
  // DEBUG: Log the props on every render
  console.log("📋 FilesSection props:", {
    activeView,
    currentFolderId,
    folderPath,
    onFolderDoubleClick: typeof onFolderDoubleClick,
    onBackToParent: typeof onBackToParent,
    onBreadcrumbClick: typeof onBreadcrumbClick
  });
  
  const [files, setFiles] = useState([]);
  const [folders, setFolders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [activeMenu, setActiveMenu] = useState(null);
  const [showRenameModal, setShowRenameModal] = useState(null);
  const [showConfirmDialog, setShowConfirmDialog] = useState(null);
  const [newName, setNewName] = useState("");
  const [navigating, setNavigating] = useState(false);
  const [clickTimeout, setClickTimeout] = useState(null);

  useEffect(() => {
    if (searchResults) {
      // Display search results
      console.log("Displaying search results:", searchResults);
      console.log("First folder in search results:", searchResults.Folders?.[0] || searchResults.folders?.[0]);
      console.log("First file in search results:", searchResults.Files?.[0] || searchResults.files?.[0]);
      setFolders(searchResults.Folders || searchResults.folders || []);
      setFiles(searchResults.Files || searchResults.files || []);
      setLoading(false);
    } else {
      // Load normal data
      loadData();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeView, searchResults, currentFolderId, refreshTrigger]);

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

  // Close menu when view changes
  useEffect(() => {
    setActiveMenu(null);
  }, [activeView, currentFolderId]);

  const loadData = async () => {
    console.log("🔄 LoadData called - activeView:", activeView, "currentFolderId:", currentFolderId);
    setLoading(true);
    setError("");
    try {
      let data = { folders: [], files: [] };
      
      if (activeView === "files") {
        if (currentFolderId) {
          // Load files and folders in specific folder
          console.log("Loading files and folders in folder ID:", currentFolderId);
          const response = await apiService.listItems(currentFolderId);
          console.log("ListItems response status:", response.status);
          
          if (response.ok) {
            data = await response.json();
            console.log("ListItems data received:", data);
          } else {
            const errorText = await response.text();
            console.error("ListItems failed:", response.status, errorText);
            setError(`Failed to load folder contents: ${response.status} - ${errorText}`);
          }
        } else {
          // Load root level files and folders (parentFolderId = null)
          console.log("Loading root level files and folders using listItems...");
          
          const response = await apiService.listItems(null); // null for root level
          console.log("ListItems (root) response status:", response.status);
          
          if (response.ok) {
            data = await response.json();
            console.log("ListItems (root) data received:", data);
          } else {
            const errorText = await response.text();
            console.error("ListItems (root) failed:", response.status, errorText);
            setError(`Failed to load root folder contents: ${response.status} - ${errorText}`);
          }
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
      
      // Handle different response formats and normalize data structure
      const rawFolders = data.Folders || data.folders || [];
      const rawFiles = data.Files || data.files || [];
      
      // Normalize folders to consistent property names
      const loadedFolders = rawFolders.map(f => ({
        id: f.FolderID ?? f.folderID ?? f.folderId ?? f.id,
        name: f.FolderName ?? f.folderName ?? f.name,
        parentId: f.ParentFolderID ?? f.parentFolderID ?? f.parentFolderId ?? null,
        createdAt: f.CreatedAt ?? f.createdAt,
        // Keep original properties for backward compatibility
        ...f
      }));

      // Normalize files to consistent property names  
      const loadedFiles = rawFiles.map(f => ({
        id: f.FileID ?? f.fileID ?? f.fileId ?? f.id,
        name: f.FileName ?? f.fileName ?? f.name,
        type: f.FileType ?? f.fileType,
        folderId: f.FolderID ?? f.folderID ?? f.folderId ?? null,
        size: f.FileSizeMB ?? f.fileSizeMB ?? f.fileSize,
        // Keep original properties for backward compatibility
        ...f
      }));
      
      console.log("📊 Normalized folders:", loadedFolders);
      console.log("📊 Normalized files:", loadedFiles);
      
      // Debug: Check individual folder and file properties
      if (loadedFolders.length > 0) {
        console.log("✅ First folder sample:", loadedFolders[0]);
        console.log("🆔 Normalized folder ID:", loadedFolders[0].id);
      } else {
        console.log("❌ NO FOLDERS FOUND");
      }
      if (loadedFiles.length > 0) {
        console.log("✅ First file sample:", loadedFiles[0]);
        console.log("🆔 Normalized file ID:", loadedFiles[0].id);
      } else {
        console.log("❌ NO FILES FOUND");
      }
      
      console.log("🔧 Setting state - folders:", loadedFolders.length, "files:", loadedFiles.length);
      setFolders(loadedFolders);
      setFiles(loadedFiles);
    } catch (err) {
      console.error("Error loading data:", err);
      setError(`Failed to load data: ${err.message}`);
    } finally {
      setLoading(false);
    }
  };

  const handleMenuClick = (itemId, itemType, event, index) => {
    event.stopPropagation();
    console.log("🎯 Menu clicked for:", itemType, "ID:", itemId, "Index:", index);
    
    // Enhanced ID extraction for search results
    let actualId = itemId;
    if (!actualId && index !== undefined) {
      const item = itemType === 'folder' ? folders[index] : files[index];
      if (item) {
        actualId = item.id || item.FolderID || item.folderID || item.folderId || 
                  item.FileID || item.fileID || item.fileId || 
                  item.Id || item.ID || index;
        console.log("🔧 Extracted ID from item:", actualId, "from item:", item);
      }
    }
    
    if (!actualId && actualId !== 0) {
      console.error("🚨 MISSING ID ERROR - itemType:", itemType, "Index:", index);
      console.log("Current folders state:", folders);
      console.log("Current files state:", files);
      if (itemType === 'folder' && folders[index]) {
        console.log("Folder at index", index, ":", folders[index]);
      } else if (itemType === 'file' && files[index]) {
        console.log("File at index", index, ":", files[index]);
      }
      alert('Internal error: missing file ID. Refresh the page or contact support.');
      return;
    }
    
    // Create unique menu key using both ID and index for safety
    const menuKey = `${itemType}-${actualId || index}`;
    console.log("Menu key:", menuKey);
    setActiveMenu(activeMenu === menuKey ? null : menuKey);
  };

  const handleRename = (item, itemType) => {
    setShowRenameModal({ item, itemType });
    const itemName = item.name || (itemType === 'folder' ? 
      (item.FolderName || item.folderName) : 
      (item.FileName || item.fileName));
    setNewName(itemName || '');
    setActiveMenu(null);
  };

  const handleRenameSubmit = async () => {
    if (!showRenameModal || !newName.trim()) return;
    
    try {
      const { item, itemType } = showRenameModal;
      let response;
      
      console.log("Renaming item:", item, "Type:", itemType, "New name:", newName.trim());
      
      if (itemType === 'folder') {
        const folderId = item.id || item.FolderID || item.folderId || 
                       item.Id || item.folderid || item.FOLDERID;
        console.log("Calling rename folder for", item, "resolved id:", folderId);
        
        if (typeof folderId === 'undefined' || folderId === null) {
          alert('Internal error: missing folder ID. Refresh the page or contact support.');
          console.error('Missing folder ID for item', item);
          return;
        }
        
        response = await apiService.renameFolder(folderId, newName.trim());
      } else {
        const fileId = item.id || item.FileID || item.fileId || 
                     item.Id || item.fileid || item.FILEID;
        console.log("Calling rename file for", item, "resolved id:", fileId);
        
        if (typeof fileId === 'undefined' || fileId === null) {
          alert('Internal error: missing file ID. Refresh the page or contact support.');
          console.error('Missing file ID for item', item);
          return;
        }
        
        response = await apiService.renameFile(fileId, newName.trim());
      }
      
      if (response.ok) {
        console.log("Item renamed successfully");
        loadData(); // Refresh the list
        if (onRefresh) onRefresh(); // Trigger storage update
      } else {
        const error = await response.text();
        alert(`Error: ${error}`);
        console.error("Rename failed:", error);
      }
    } catch (err) {
      console.error("Rename error:", err);
      setError("Failed to rename item");
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
      console.log("Deleting item:", item, "Type:", itemType);
      
      if (itemType === 'folder') {
        const folderId = item.id || item.FolderID || item.folderId || 
                       item.Id || item.folderid || item.FOLDERID;
        console.log("Calling delete folder for", item, "resolved id:", folderId);
        
        if (typeof folderId === 'undefined' || folderId === null) {
          alert('Internal error: missing folder ID. Refresh the page or contact support.');
          console.error('Missing folder ID for item', item);
          return;
        }
        
        response = await apiService.deleteFolder(folderId);
      } else {
        const fileId = item.id || item.FileID || item.fileId || 
                     item.Id || item.fileid || item.FILEID;
        console.log("Calling delete file for", item, "resolved id:", fileId);
        
        if (typeof fileId === 'undefined' || fileId === null) {
          alert('Internal error: missing file ID. Refresh the page or contact support.');
          console.error('Missing file ID for item', item);
          return;
        }
        
        response = await apiService.deleteFile(fileId);
      }
      
      if (response.ok) {
        console.log("Item moved to recycle bin successfully");
        loadData(); // Refresh the list
        if (onRefresh) onRefresh(); // Trigger storage update
      } else {
        const error = await response.text();
        alert(`Error: ${error}`);
        console.error("Delete failed:", error);
      }
    } catch (err) {
      console.error("Delete error:", err);
      alert("Failed to delete");
    }
  };

  const handlePermanentDelete = async (item, itemType) => {
    setShowConfirmDialog({
      title: `Permanently Delete ${itemType}`,
      message: `⚠️ WARNING: This will PERMANENTLY delete this ${itemType}. This action cannot be undone. Are you absolutely sure?`,
      onConfirm: () => confirmPermanentDelete(item, itemType),
      onCancel: () => setShowConfirmDialog(null)
    });
    setActiveMenu(null);
  };

  const confirmPermanentDelete = async (item, itemType) => {
    setShowConfirmDialog(null);
    
    try {
      let response;
      console.log("Permanently deleting item:", item, "Type:", itemType);
      
      if (itemType === 'folder') {
        const folderId = item.id || item.FolderID || item.folderId || 
                       item.Id || item.folderid || item.FOLDERID;
        console.log("Calling permanent delete folder for", item, "resolved id:", folderId);
        
        if (typeof folderId === 'undefined' || folderId === null) {
          alert('Internal error: missing folder ID. Refresh the page or contact support.');
          console.error('Missing folder ID for item', item);
          return;
        }
        
        response = await apiService.permanentDeleteFolder(folderId);
        if (response.ok) {
          // Optimistically remove from UI
          setFolders(prev => prev.filter(f => {
            const id = f.id || f.FolderID || f.folderId || f.Id || f.folderid || f.FOLDERID;
            return String(id) !== String(folderId);
          }));
        }
      } else {
        const fileId = item.id || item.FileID || item.fileId || 
                     item.Id || item.fileid || item.FILEID;
        console.log("Calling permanent delete file for", item, "resolved id:", fileId);
        
        if (typeof fileId === 'undefined' || fileId === null) {
          alert('Internal error: missing file ID. Refresh the page or contact support.');
          console.error('Missing file ID for item', item);
          return;
        }
        
        response = await apiService.permanentDeleteFile(fileId);
        if (response.ok) {
          // Optimistically remove from UI
          setFiles(prev => prev.filter(f => {
            const id = f.id || f.FileID || f.fileId || f.Id || f.fileid || f.FILEID;
            return String(id) !== String(fileId);
          }));
        }
      }
      
      if (response.ok) {
        alert("Permanently deleted successfully!");
        // No immediate reload required due to optimistic update; keep refresh callback for storage sync
        if (onRefresh) onRefresh();
      } else {
        const error = await response.text();
        alert(`Error: ${error}`);
        console.error("Permanent delete failed:", error);
      }
    } catch (err) {
      console.error("Permanent delete error:", err);
      alert("Failed to permanently delete");
    }
  };

  const handleRecover = async (item, itemType) => {
    try {
      let response;
      console.log("Recovering item:", item, "Type:", itemType);
      
      if (itemType === 'folder') {
        const folderId = item.id || item.FolderID || item.folderId || 
                       item.Id || item.folderid || item.FOLDERID;
        console.log("Calling recover folder for", item, "resolved id:", folderId);
        
        if (typeof folderId === 'undefined' || folderId === null) {
          alert('Internal error: missing folder ID. Refresh the page or contact support.');
          console.error('Missing folder ID for item', item);
          return;
        }
        
        response = await apiService.recoverFolder(folderId);
      } else {
        const fileId = item.id || item.FileID || item.fileId || 
                     item.Id || item.fileid || item.FILEID;
        console.log("Calling recover file for", item, "resolved id:", fileId);
        
        if (typeof fileId === 'undefined' || fileId === null) {
          alert('Internal error: missing file ID. Refresh the page or contact support.');
          console.error('Missing file ID for item', item);
          return;
        }
        
        response = await apiService.recoverFile(fileId);
      }
      
      if (response.ok) {
        alert("Recovered successfully!");
        loadData(); // Refresh the list
        if (onRefresh) onRefresh(); // Trigger storage update
      } else {
        const error = await response.text();
        alert(`Error: ${error}`);
        console.error("Recover failed:", error);
      }
    } catch (err) {
      console.error("Recover error:", err);
      alert("Failed to recover");
    }
    setActiveMenu(null);
  };

  const handleDownload = async (item, itemType) => {
    try {
      const itemId = item.id || (itemType === 'folder' ? 
        (item.FolderID || item.folderId || item.Id || item.folderid || item.FOLDERID) : 
        (item.FileID || item.fileId || item.Id || item.fileid || item.FILEID));

      console.log(`Calling download for ${itemType}`, item, "resolved id:", itemId);

      if (typeof itemId === 'undefined' || itemId === null) {
        alert('Internal error: missing ID. Refresh the page or contact support.');
        console.error(`Missing ${itemType} ID for item`, item);
        setActiveMenu(null);
        return;
      }

      let response;
      if (itemType === 'folder') {
        response = await apiService.downloadFolder(itemId);
      } else {
        // Build a best-effort fallback file name (name + extension)
        const name = item.FileName || item.fileName || item.name || 'file';
        const ext = item.FileType || item.fileType || item.extension || '';
        const fallback = ext ? `${name}.${ext}` : name;
        response = await apiService.downloadFile(itemId, fallback);
      }

      if (!response.ok) {
        const error = await response.text();
        alert(`Download failed: ${error}`);
        console.error("Download failed:", error);
      }
    } catch (err) {
      console.error("Download error:", err);
      alert("Failed to download item");
    }
    setActiveMenu(null);
  };

  const handleDetails = async (item, itemType) => {
    try {
      let response;
      const itemId = item.id || (itemType === 'folder' ? 
        (item.FolderID || item.folderId || item.Id || item.folderid || item.FOLDERID) : 
        (item.FileID || item.fileId || item.Id || item.fileid || item.FILEID));

      console.log(`Calling details for ${itemType}`, item, "resolved id:", itemId);

      if (typeof itemId === 'undefined' || itemId === null) {
        alert('Internal error: missing ID. Refresh the page or contact support.');
        console.error(`Missing ${itemType} ID for item`, item);
        setActiveMenu(null);
        return;
      }
      
      if (itemType === 'folder') {
        response = await apiService.getFolderDetails(itemId);
      } else {
        response = await apiService.getFileDetails(itemId);
      }
      
      if (response.ok) {
        const details = await response.json();
        
        let detailsText = '';
        if (itemType === 'folder') {
          const folderName = details.FolderName || details.folderName || 'Unknown Folder';
          const createdAt = details.CreatedAt || details.createdAt;
          const createdDate = createdAt ? new Date(createdAt).toLocaleString() : 'Unknown';
          
          detailsText = `Folder Details:\n\n` +
            `Name: ${folderName}\n` +
            `Created: ${createdDate}\n` +
            `Files: ${details.FileCount || details.fileCount || 0}\n` +
            `Subfolders: ${details.SubfolderCount || details.subfolderCount || 0}\n` +
            `Status: ${details.IsDeleted || details.isDeleted ? 'Deleted' : 'Active'}`;
        } else {
          const fileName = details.FileName || details.fileName || 'Unknown File';
          const fileType = details.FileType || details.fileType || '';
          const fullFileName = fileName + (fileType ? '.' + fileType : '');
          const uploadedAt = details.UploadedAt || details.uploadedAt;
          const uploadedDate = uploadedAt ? new Date(uploadedAt).toLocaleString() : 'Unknown';
          
          detailsText = `File Details:\n\n` +
            `Name: ${fullFileName}\n` +
            `Size: ${details.FileSizeMB || details.fileSizeMB || 0} MB\n` +
            `Uploaded: ${uploadedDate}\n` +
            `Status: ${details.IsDeleted || details.isDeleted ? 'Deleted' : 'Active'}`;
        }
        
        alert(detailsText);
      } else {
        const error = await response.text();
        alert(`Error getting details: ${error}`);
      }
    } catch (err) {
      console.error("Details error:", err);
      alert("Failed to get details");
    }
    setActiveMenu(null);
  };

  const getViewTitle = () => {
    if (searchResults) {
      return "🔍 Search Results";
    }
    switch (activeView) {
      case "recycle": return "🗑️ Recycle Bin";
      case "files":
        if (folderPath.length > 0) {
          return `📁 ${folderPath[folderPath.length - 1].name}`;
        }
        return "🏠 My Files";
      default: 
        return "📁 Files & Folders";
    }
  };

  const getCurrentLocationPath = () => {
    if (activeView !== 'files') return '';
    
    if (folderPath.length === 0) {
      return 'Home';
    }
    
    return folderPath.map(folder => folder.name).join(' › ');
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
      <div style={{marginBottom: '15px'}}>
        <div style={{display: 'flex', alignItems: 'center', justifyContent: 'space-between'}}>
          <h3 style={{margin: 0}}>{getViewTitle()}</h3>
          {activeView === 'files' && (
            <button 
              onClick={onBackToParent}
              style={{
                background: '#4299e1',
                color: 'white',
                border: 'none',
                padding: '6px 12px',
                borderRadius: '4px',
                cursor: 'pointer',
                opacity: currentFolderId ? 1 : 0.5
              }}
              disabled={!currentFolderId}
            >
              ← Back
            </button>
          )}
        </div>
        {activeView === 'files' && getCurrentLocationPath() && (
          <div style={{
            fontSize: '12px',
            color: '#666',
            marginTop: '4px',
            fontStyle: 'italic'
          }}>
            📍 Location: {getCurrentLocationPath()}
          </div>
        )}
      </div>
      
      {/* Navigation Status */}
      {navigating && (
        <div style={{
          padding: '8px 12px',
          backgroundColor: '#e3f2fd',
          border: '1px solid #2196f3',
          borderRadius: '4px',
          marginBottom: '15px',
          fontSize: '14px',
          color: '#1565c0'
        }}>
          🔄 Navigating...
        </div>
      )}

      {/* Breadcrumb Navigation */}
      {activeView === 'files' && (
        <div style={{
          display: 'flex', 
          alignItems: 'center', 
          marginBottom: '15px',
          padding: '8px',
          backgroundColor: '#f8f9fa',
          borderRadius: '4px',
          fontSize: '14px'
        }}>
          <span 
            onClick={() => onBreadcrumbClick(-1)}
            style={{
              cursor: 'pointer',
              color: '#007acc',
              textDecoration: 'none',
              fontWeight: currentFolderId ? 'normal' : 'bold'
            }}
            onMouseOver={(e) => e.target.style.textDecoration = 'underline'}
            onMouseOut={(e) => e.target.style.textDecoration = 'none'}
          >
            🏠 Home
          </span>
          {folderPath.map((folder, index) => (
            <React.Fragment key={`breadcrumb-${folder.id}-${index}`}>
              <span style={{margin: '0 8px', color: '#666'}}>›</span>
              <span 
                onClick={() => onBreadcrumbClick(index)}
                style={{
                  cursor: 'pointer',
                  color: '#007acc',
                  textDecoration: 'none',
                  fontWeight: index === folderPath.length - 1 ? 'bold' : 'normal'
                }}
                onMouseOver={(e) => e.target.style.textDecoration = 'underline'}
                onMouseOut={(e) => e.target.style.textDecoration = 'none'}
              >
                📁 {folder.name}
              </span>
            </React.Fragment>
          ))}
        </div>
      )}
      
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
        Error: {error || 'None'}<br/>
        Sample Folder: {folders.length > 0 ? JSON.stringify(folders[0]) : 'None'}<br/>
        Sample File: {files.length > 0 ? JSON.stringify(files[0]) : 'None'}
      </div>
      
      {/* Test Data Display */}
      <div style={{background: '#e6fffa', padding: '10px', margin: '10px 0', fontSize: '12px'}}>
        <strong>Test Display:</strong><br/>
        {folders.length > 0 && (
          <div>First Folder Name: "{folders[0].FolderName || folders[0].folderName || 'NO NAME FOUND'}"</div>
        )}
        {files.length > 0 && (
          <div>First File: "{(files[0].FileName || files[0].fileName || 'NO NAME')}</div>
        )}
      </div>
      
      <div className="files-grid">
          {folders.map((folder, index) => {
            console.log("Rendering folder:", folder);
            console.log("Folder keys:", Object.keys(folder));
            const folderName = folder.name || folder.FolderName || folder.folderName || 'Unnamed Folder';
            // Enhanced ID extraction with more fallback options
            const folderId = folder.id || folder.FolderID || folder.folderId || 
                           folder.Id || folder.folderid || folder.FOLDERID;
            console.log("Extracted folderId:", folderId);
            console.log("All folder properties:", Object.keys(folder));
            
            return (
              <div 
                key={`folder-${folderId}-${index}`} 
                className="file-card folder"
                onClick={() => {
                  console.log("🔥 CLICKED FOLDER:", folderName);
                  console.log("🔧 onFolderDoubleClick type:", typeof onFolderDoubleClick);
                  console.log("🔧 onFolderDoubleClick function:", onFolderDoubleClick);
                  alert("Folder clicked: " + folderName);
                  if (onFolderDoubleClick) {
                    alert("Calling navigation handler...");
                    console.log("🚀 Calling handler with folder:", folder);
                    try {
                      onFolderDoubleClick(folder);
                      alert("Navigation handler called!");
                    } catch (error) {
                      console.error("❌ Error calling handler:", error);
                      alert("❌ Error calling handler: " + error.message);
                    }
                  } else {
                    alert("❌ No handler function provided!");
                  }
                }}
                style={{ cursor: 'pointer', position: 'relative' }}
              >
                <div className="file-icon" style={{pointerEvents: 'none'}}>📁</div>
                <div className="file-name" style={{pointerEvents: 'none'}} title={folderName}>{folderName}</div>
                <div style={{pointerEvents: 'none', fontSize: '10px', color: '#666', marginTop: '5px'}}>
                  Click to open
                </div>
                <div 
                  className="file-menu" 
                  onClick={(e) => {
                    console.log("🔧 Menu clicked, stopping propagation");
                    e.stopPropagation();
                    e.preventDefault();
                    handleMenuClick(folderId, 'folder', e, index);
                  }}
                  style={{
                    position: 'absolute',
                    top: '8px',
                    right: '8px',
                    zIndex: 10,
                    pointerEvents: 'auto'
                  }}
                >
                  ⋮
                </div>
                {activeMenu === `folder-${folderId || index}` && (
                  <div className="dropdown-menu" onClick={(e) => e.stopPropagation()}>
                    {activeView === "recycle" ? (
                      <>
                        <button onClick={() => handleDetails(folder, 'folder')}>Details</button>
                        <button onClick={() => handleRecover(folder, 'folder')}>Recover</button>
                        <button onClick={() => handlePermanentDelete(folder, 'folder')} style={{color: 'red'}}>Delete Permanently</button>
                      </>
                    ) : (
                      <>
                        <button onClick={() => handleRename(folder, 'folder')}>Rename</button>
                        <button onClick={() => handleDownload(folder, 'folder')}>Download</button>
                        <button onClick={() => handleDelete(folder, 'folder')}>Delete</button>
                        <button onClick={() => handleDetails(folder, 'folder')}>Details</button>
                      </>
                    )}
                  </div>
                )}
            </div>
            );
          })}

          {files.map((file, index) => {
            console.log("Rendering file:", file);
            console.log("File keys:", Object.keys(file));
            const fileName = file.name || file.FileName || file.fileName || 'Unnamed File';
            const fileType = file.FileType || file.fileType || file.extension || '';
            const fullFileName = fileName + (fileType ? '.' + fileType : '');
            // Enhanced ID extraction with more fallback options
            const fileId = file.id || file.FileID || file.fileId || 
                         file.Id || file.fileid || file.FILEID;
            console.log("Extracted fileId:", fileId);
            console.log("All file properties:", Object.keys(file));
            
            return (
              <div key={`file-${fileId}-${index}`} className="file-card file">
                <div className="file-icon">📄</div>
                <div className="file-name" title={fullFileName}>{fullFileName}</div>
                <div 
                  className="file-menu" 
                  onClick={(e) => handleMenuClick(fileId, 'file', e, index)}
                >
                  ⋮
                </div>
                {activeMenu === `file-${fileId || index}` && (
                  <div className="dropdown-menu" onClick={(e) => e.stopPropagation()}>
                    {activeView === "recycle" ? (
                      <>
                        <button onClick={() => handleDetails(file, 'file')}>Details</button>
                        <button onClick={() => handleRecover(file, 'file')}>Recover</button>
                        <button onClick={() => handlePermanentDelete(file, 'file')} style={{color: 'red'}}>Delete Permanently</button>
                      </>
                    ) : (
                      <>
                        <button onClick={() => handleRename(file, 'file')}>Rename</button>
                        <button onClick={() => handleDownload(file, 'file')}>Download</button>
                        <button onClick={() => handleDelete(file, 'file')}>Delete</button>
                        <button onClick={() => handleDetails(file, 'file')}>Details</button>
                      </>
                    )}
                  </div>
                )}
            </div>
            );
          })}
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
