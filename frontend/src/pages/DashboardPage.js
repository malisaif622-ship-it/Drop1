import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import Sidebar from '../components/Sidebar';
import Topbar from '../components/Topbar';
import FilesSection from '../components/FilesSectionEnhanced';
import ConfirmDialog from '../components/ConfirmDialog';
import api from '../services/api';
import './DashboardPage.css';

function DashboardPage() {
  const navigate = useNavigate();
  
  // Navigation State
  const [currentFolderId, setCurrentFolderId] = useState(null);
  const [folderPath, setFolderPath] = useState([]);
  
  // Data State
  const [folders, setFolders] = useState([]);
  const [files, setFiles] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  
  // UI State
  const [activeView, setActiveView] = useState("files");
  const [searchResults, setSearchResults] = useState(null);
  const [refreshTrigger, setRefreshTrigger] = useState(0);
  const [showConfirmDialog, setShowConfirmDialog] = useState(false);
  const [itemToDelete, setItemToDelete] = useState(null);

  const loadContents = useCallback(async () => {
    console.log("🔄 Loading contents for folder:", currentFolderId);
    
    try {
      setLoading(true);
      setError(null);
      
      const response = await api.getAllItems();
      
      if (response.ok) {
        const data = await response.json();
        console.log("📦 Loaded all items:", data);
        
        // Filter items by current folder
        const filteredFolders = currentFolderId 
          ? (data.Folders || []).filter(f => f.ParentFolderID === currentFolderId)
          : (data.Folders || []).filter(f => f.ParentFolderID === null);
          
        const filteredFiles = currentFolderId
          ? (data.Files || []).filter(f => f.FolderID === currentFolderId)
          : (data.Files || []).filter(f => f.FolderID === null);
        
        console.log("� Filtered folders:", filteredFolders);
        console.log("📄 Filtered files:", filteredFiles);
        
        setFolders(filteredFolders);
        setFiles(filteredFiles);
      } else {
        throw new Error(`API call failed with status: ${response.status}`);
      }
      
    } catch (err) {
      console.error("❌ Error loading contents:", err);
      
      // Use mock data when API is not available (for development)
      console.log("📦 Using mock data for development");
      const mockData = {
        folders: [
          { folderID: 1, folderName: 'Muhammad Bilal Ibrahim - 230973', parentFolderID: currentFolderId, createdAt: new Date().toISOString() },
          { folderID: 2, folderName: 'Documents', parentFolderID: currentFolderId, createdAt: new Date().toISOString() },
          { folderID: 3, folderName: 'Pictures', parentFolderID: currentFolderId, createdAt: new Date().toISOString() },
        ],
        files: [
          { id: 101, name: 'report.pdf', size: '2.5 MB', parentId: currentFolderId },
          { id: 102, name: 'presentation.pptx', size: '5.1 MB', parentId: currentFolderId },
        ]
      };
      
      setFolders(mockData.folders);
      setFiles(mockData.files);
      setError(null); // Clear error since we have mock data
    } finally {
      setLoading(false);
    }
  }, [currentFolderId, activeView]);

  // Load folders and files when currentFolderId changes
  useEffect(() => {
    loadContents();
  }, [loadContents, refreshTrigger]);

  // 🎯 MAIN NAVIGATION FUNCTION - Handle folder double-click
  const handleFolderDoubleClick = (folder) => {
    console.log("🎯 DASHBOARD: handleFolderDoubleClick called with:", folder);
    console.log("� Folder object keys:", Object.keys(folder));
    console.log("🔍 Full folder object:", JSON.stringify(folder, null, 2));
    
    try {
      // Handle your specific API folder object structure
      const folderId = folder.folderID || folder.FolderID || folder.folderId || folder.id;
      const folderName = folder.folderName || folder.FolderName || folder.name || 'Unknown Folder';
      
      console.log("🚀 DASHBOARD: Extracted ID:", folderId, "Name:", folderName);
      console.log("🔍 Available properties:", {
        FolderID: folder.FolderID,
        folderId: folder.folderId,
        id: folder.id,
        Id: folder.Id,
        ID: folder.ID,
        allKeys: Object.keys(folder)
      });
      
      if (!folderId && folderId !== 0) {
        console.error("❌ DASHBOARD: No folder ID found in folder object:", folder);
        console.error("❌ Available keys:", Object.keys(folder));
        alert(`❌ Error: No folder ID found! Available properties: ${Object.keys(folder).join(', ')}`);
        return;
      }

      // Add current folder to breadcrumb path
      const newPath = [...folderPath, { id: folderId, name: folderName }];
      
      // Update states
      setCurrentFolderId(folderId);
      setFolderPath(newPath);
      
      console.log("✅ Navigation successful!");
      console.log("📍 New folder ID:", folderId);
      console.log("🧭 New path:", newPath);
      
      // Show success feedback
      alert(`✅ Successfully navigated into: ${folderName}`);
      
    } catch (error) {
      console.error("❌ Navigation error:", error);
      alert(`❌ Failed to navigate into folder`);
    }
  };

  // Handle breadcrumb navigation
  const handleBreadcrumbClick = (targetIndex) => {
    console.log("🧭 Breadcrumb click - navigating to index:", targetIndex);
    
    if (targetIndex === -1) {
      // Navigate to root
      setCurrentFolderId(null);
      setFolderPath([]);
    } else {
      // Navigate to specific folder in path
      const targetFolder = folderPath[targetIndex];
      setCurrentFolderId(targetFolder.id);
      setFolderPath(folderPath.slice(0, targetIndex + 1));
    }
  };

  // Handle back button navigation
  const handleBackToParent = () => {
    console.log("⬅️ Back button clicked");
    
    if (folderPath.length > 0) {
      // Remove current folder from path and go to parent
      const newPath = [...folderPath];
      newPath.pop(); // Remove current folder
      setFolderPath(newPath);
      
      if (newPath.length > 0) {
        // Go to the parent folder
        const parentFolder = newPath[newPath.length - 1];
        setCurrentFolderId(parentFolder.id);
      } else {
        // Go back to root
        setCurrentFolderId(null);
      }
    }
  };

  // Other handlers
  const handleViewChange = (view) => {
    setActiveView(view);
    setSearchResults(null);
    setCurrentFolderId(null);
    setFolderPath([]);
  };

  const handleSearchResults = (results) => {
    setSearchResults(results);
    if (results) {
      setActiveView("files");
    }
  };

  const handleSearchInput = async (keyword) => {
    console.log("🔍 Search input changed:", keyword);
    console.log("📍 Current context - folderId:", currentFolderId, "activeView:", activeView);
    
    try {
      const isRecycleView = activeView === "recycle";
      
      if (!keyword || keyword.trim() === '') {
        // Clear search and return to current folder view
        console.log("📋 Clearing search, returning to folder view");
        setSearchResults(null);
        // The normal loadContents will be triggered by useEffect
        return;
      }

      // Perform contextual search
      // In recycle bin, ignore folder context and search all deleted items
      const searchFolderId = isRecycleView ? null : currentFolderId;
      
      console.log("🔎 Performing contextual search:", {
        keyword: keyword.trim(),
        parentFolderId: searchFolderId,
        deletedOnly: isRecycleView
      });
      
      const response = await api.search(keyword.trim(), searchFolderId, isRecycleView);
      if (response.ok) {
        const data = await response.json();
        console.log("🎯 Contextual search results:", data);
        setSearchResults(data);
        // Don't change activeView - keep current context
      } else {
        const errorText = await response.text();
        console.error("❌ Search failed:", errorText);
        setError("Search failed: " + errorText);
      }
    } catch (error) {
      console.error("❌ Search error:", error);
      setError("Search error: " + error.message);
    }
  };

  const handleFolderCreated = () => {
    setRefreshTrigger(prev => prev + 1);
  };

  const handleFilesUploaded = () => {
    setRefreshTrigger(prev => prev + 1);
  };

  const handleFileClick = (file) => {
    console.log("📄 File clicked:", file);
    alert(`Opening file: ${file.name}`);
  };

  const handleFileDownload = async (file) => {
    console.log("⬇️ Downloading file:", file);
    try {
      const response = await api.get(`/files/${file.id}/download`, {
        responseType: 'blob'
      });
      
      const url = window.URL.createObjectURL(new Blob([response.data]));
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', file.name);
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      
    } catch (error) {
      console.error("❌ Download error:", error);
      alert('Failed to download file');
    }
  };

  const handleCreateFolder = async () => {
    const folderName = prompt('Enter folder name:');
    if (!folderName || !folderName.trim()) return;

    try {
      console.log("Creating folder:", folderName.trim(), "in parent folder ID:", currentFolderId);
      
      // Defensive check for currentFolderId
      if (currentFolderId !== null && (typeof currentFolderId === 'undefined' || currentFolderId === 'undefined')) {
        alert('Internal error: invalid current folder ID. Refresh the page or contact support.');
        console.error('Invalid currentFolderId:', currentFolderId);
        return;
      }

      const response = await api.createFolder(folderName.trim(), currentFolderId);
      
      if (response.ok) {
        handleFolderCreated();
        alert(`✅ Folder "${folderName}" created successfully`);
      } else {
        const error = await response.text();
        alert(`Failed to create folder: ${error}`);
        console.error("Create folder failed:", error);
      }
      
    } catch (error) {
      console.error("❌ Create folder error:", error);
      alert('Failed to create folder');
    }
  };

  const handleUploadFile = () => {
    const input = document.createElement('input');
    input.type = 'file';
    input.multiple = true;
    
    input.onchange = async (e) => {
      const files = Array.from(e.target.files);
      
      if (files.length === 0) return;
      
      try {
        console.log("Uploading files:", files.map(f => f.name), "to parent folder ID:", currentFolderId);
        
        // Defensive check for currentFolderId
        if (currentFolderId !== null && (typeof currentFolderId === 'undefined' || currentFolderId === 'undefined')) {
          alert('Internal error: invalid current folder ID. Refresh the page or contact support.');
          console.error('Invalid currentFolderId for upload:', currentFolderId);
          return;
        }

        const response = await api.uploadFiles(files, currentFolderId);
        
        if (response.ok) {
          handleFilesUploaded();
          alert(`✅ Successfully uploaded ${files.length} file(s)`);
        } else {
          const error = await response.text();
          alert(`Failed to upload files: ${error}`);
          console.error("Upload files failed:", error);
        }
        
      } catch (error) {
        console.error("❌ Upload files error:", error);
        alert('Failed to upload files');
      }
    };
    
    input.click();
  };

  const handleDelete = (item) => {
    setItemToDelete(item);
    setShowConfirmDialog(true);
  };

  const confirmDelete = async () => {
    if (!itemToDelete) return;

    try {
      const endpoint = itemToDelete.size ? `/files/${itemToDelete.id}` : `/folders/${itemToDelete.id}`;
      await api.delete(endpoint);
      
      setShowConfirmDialog(false);
      setItemToDelete(null);
      setRefreshTrigger(prev => prev + 1);
      
    } catch (error) {
      console.error("❌ Delete error:", error);
      alert('Failed to delete item');
    }
  };

  const handleLogout = () => {
    localStorage.removeItem('token');
    navigate('/login');
  };

  // Render breadcrumb
  const renderBreadcrumb = () => (
    <div className="breadcrumb">
      <span 
        className="breadcrumb-item" 
        onClick={() => handleBreadcrumbClick(-1)}
      >
        🏠 Home
      </span>
      {folderPath.map((folder, index) => (
        <React.Fragment key={folder.id}>
          <span className="breadcrumb-separator">›</span>
          <span 
            className="breadcrumb-item"
            onClick={() => handleBreadcrumbClick(index)}
          >
            📁 {folder.name}
          </span>
        </React.Fragment>
      ))}
    </div>
  );

  return (
    <div className="dashboard-container">
      <Sidebar 
        activeView={activeView} 
        onViewChange={handleViewChange} 
        refreshTrigger={refreshTrigger}
      />
      
      <div className="main-content">
        <Topbar 
          onSearch={handleSearchResults}
          onSearchInput={handleSearchInput}
          onLogout={handleLogout}
          onFolderCreated={handleFolderCreated}
          onFilesUploaded={handleFilesUploaded}
          currentFolderId={currentFolderId}
          activeView={activeView}
        />
        
        <div className="content-area">
          <div className="page-header">
            <h1>📂 File Manager</h1>
            {renderBreadcrumb()}
          </div>

          {loading && <div className="loading">Loading...</div>}
          {error && <div className="error">Error: {error}</div>}

          {!loading && (
            <FilesSection
              key={refreshTrigger}
              activeView={activeView}
              searchResults={searchResults}
              currentFolderId={currentFolderId}
              folderPath={folderPath}
              onFolderCreated={handleFolderCreated}
              onFilesUploaded={handleFilesUploaded}
              onFolderDoubleClick={handleFolderDoubleClick}
              onBreadcrumbClick={handleBreadcrumbClick}
              onBackToParent={handleBackToParent}
              onRefresh={() => setRefreshTrigger(prev => prev + 1)}
              folders={folders}
              files={files}
              onFileClick={handleFileClick}
              onFileDownload={handleFileDownload}
              onCreateFolder={handleCreateFolder}
              onUploadFile={handleUploadFile}
              onDelete={handleDelete}
            />
          )}
        </div>
      </div>

      {showConfirmDialog && (
        <ConfirmDialog
          message={`Are you sure you want to delete "${itemToDelete?.name}"?`}
          onConfirm={confirmDelete}
          onCancel={() => setShowConfirmDialog(false)}
        />
      )}
    </div>
  );
}

export default DashboardPage;
