import React, { useState } from "react";
import "./Topbar.css";
import apiService from "../services/api";

function Topbar({ onSearchResults, onSearchInput, onFolderCreated, onFilesUploaded, currentFolderId, activeView }) {
  const [showUploadMenu, setShowUploadMenu] = useState(false);
  const [searchTerm, setSearchTerm] = useState("");
  const [isSearching, setIsSearching] = useState(false);
  const [showCreateFolderModal, setShowCreateFolderModal] = useState(false);
  const [folderName, setFolderName] = useState("");

  const handleSearch = async (e) => {
    e.preventDefault();
    console.log("🔍 Manual search triggered for:", searchTerm);
    if (onSearchInput) {
      onSearchInput(searchTerm);
    }
  };

  const handleSearchInputChange = (e) => {
    const value = e.target.value;
    setSearchTerm(value);
    
    // Clear any existing timeout
    clearTimeout(window.searchTimeout);
    
    // Debounced search (300ms as recommended)
    window.searchTimeout = setTimeout(() => {
      console.log("🔍 Debounced search triggered for:", value);
      if (onSearchInput) {
        onSearchInput(value);
      }
    }, 300); // Wait 300ms after user stops typing
  };

  const clearSearch = () => {
    console.log("🧹 Clearing search");
    setSearchTerm("");
    if (onSearchInput) {
      onSearchInput(""); // Empty string triggers "load all" in parent
    }
  };

  const checkCurrentUser = async () => {
    try {
      console.log("Checking current user authentication...");
      const response = await apiService.getCurrentUser();
      
      console.log("Auth check response status:", response.status);
      console.log("Auth check response headers:", response.headers);
      
      if (response.ok) {
        const userData = await response.json();
        console.log("Current user data:", userData);
        alert(`✅ AUTHENTICATED!\n\nUser ID: ${userData.UserID}\nFull Name: ${userData.FullName}\nDepartment: ${userData.Department || 'N/A'}`);
      } else {
        console.error("Authentication failed with status:", response.status);
        const errorText = await response.text();
        console.error("Error response:", errorText);
        
        if (response.status === 401) {
          alert(`❌ NOT AUTHENTICATED\n\nYou need to login first to establish a session.\n\nSteps to test:\n1. Login with your credentials\n2. Then click "Check Auth"\n\nError details: ${errorText}`);
        } else {
          alert(`❌ Authentication check failed. Status: ${response.status}\nError: ${errorText}`);
        }
      }
    } catch (err) {
      console.error("Auth check network error:", err);
      alert(`❌ NETWORK ERROR\n\nFailed to connect to backend.\n\nPlease check:\n1. Backend is running on https://localhost:7294\n2. Your browser trusts the SSL certificate\n3. CORS is properly configured\n\nError: ${err.message}`);
    }
  };

  const handleCreateFolder = () => {
    setShowCreateFolderModal(true);
  };

  const handleCreateFolderSubmit = async () => {
    if (!folderName.trim()) {
      return;
    }

    try {
      console.log("Creating folder with name:", folderName.trim());
      
      // First, let's test if we're authenticated
      try {
        const authTest = await apiService.getCurrentUser();
        console.log("Auth test response:", authTest.status);
        if (!authTest.ok) {
          console.log("Not authenticated, auth test failed with status:", authTest.status);
          alert("You are not logged in. Please login first.");
          return;
        }
      } catch (authErr) {
        console.error("Auth test failed:", authErr);
        alert("Authentication test failed. Please login again.");
        return;
      }
      
      // Defensive check for currentFolderId
      console.log("Creating folder in currentFolderId:", currentFolderId, "type:", typeof currentFolderId);
      
      if (currentFolderId !== null && (typeof currentFolderId === 'undefined' || currentFolderId === 'undefined')) {
        alert('Internal error: invalid current folder ID. Refresh the page or contact support.');
        console.error('Invalid currentFolderId in Topbar:', currentFolderId);
        return;
      }
      
      const response = await apiService.createFolder(folderName.trim(), currentFolderId);
      console.log("Create folder response status:", response.status);
      console.log("Create folder response ok:", response.ok);
      
      if (!response.ok) {
        const errorText = await response.text();
        console.error("Create folder error response:", errorText);
        throw new Error(errorText || "Failed to create folder");
      }
      const data = await response.json();
      console.log("Folder created successfully:", data);
      
      // Notify parent to refresh file list
      if (onFolderCreated) {
        onFolderCreated();
      }

      // Close modal and clear input
      setShowCreateFolderModal(false);
      setFolderName("");
    } catch (err) {
      console.error("Create folder error:", err);
      if (err.message.includes('Network error')) {
        alert('Network Error: Please ensure your backend server is running on https://localhost:7294 and your browser trusts the SSL certificate.');
      } else {
        alert(`Error creating folder: ${err.message}`);
      }
    }
  };

  const handleUploadFile = async () => {
    // Create a file input element to select files
    const fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.multiple = true;
    fileInput.accept = '*/*';
    
    fileInput.onchange = async (event) => {
      const files = event.target.files;
      if (!files || files.length === 0) {
        alert("No files selected");
        return;
      }

      try {
        // Defensive check for currentFolderId
        console.log("Uploading files to currentFolderId:", currentFolderId, "type:", typeof currentFolderId);
        
        if (currentFolderId !== null && (typeof currentFolderId === 'undefined' || currentFolderId === 'undefined')) {
          alert('Internal error: invalid current folder ID. Refresh the page or contact support.');
          console.error('Invalid currentFolderId for upload in Topbar:', currentFolderId);
          return;
        }
        
        const response = await apiService.uploadFiles(files, currentFolderId);
        if (!response.ok) {
          const errorText = await response.text();
          throw new Error(errorText || "Failed to upload file");
        }
        console.log("File(s) uploaded successfully");
        
        // Notify parent to refresh file list
        if (onFilesUploaded) {
          onFilesUploaded();
        }
      } catch (err) {
        console.error(err);
        if (err.message.includes('Network error')) {
          alert('Network Error: Please ensure your backend server is running on https://localhost:7294 and your browser trusts the SSL certificate.');
        } else {
          alert(`Error uploading file: ${err.message}`);
        }
      }
    };
    
    fileInput.click();
  };

  const handleUploadFolder = async () => {
    // Create a file input element to select multiple files (folder upload simulation)
    const fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.webkitdirectory = true; // This enables folder selection
    fileInput.multiple = true;
    
    fileInput.onchange = async (event) => {
      const files = event.target.files;
      if (!files || files.length === 0) {
        alert("No folder selected");
        return;
      }

      try {
        // Defensive check for currentFolderId
        console.log("Uploading folder to currentFolderId:", currentFolderId, "type:", typeof currentFolderId);
        
        if (currentFolderId !== null && (typeof currentFolderId === 'undefined' || currentFolderId === 'undefined')) {
          alert('Internal error: invalid current folder ID. Refresh the page or contact support.');
          console.error('Invalid currentFolderId for folder upload in Topbar:', currentFolderId);
          return;
        }
        
        const response = await apiService.uploadFolder(files, currentFolderId);
        if (!response.ok) {
          const errorText = await response.text();
          throw new Error(errorText || "Failed to upload folder");
        }
        console.log("Folder uploaded successfully");
        
        // Notify parent to refresh file list
        if (onFilesUploaded) {
          onFilesUploaded();
        }
      } catch (err) {
        console.error(err);
        if (err.message.includes('Network error')) {
          alert('Network Error: Please ensure your backend server is running on https://localhost:7294 and your browser trusts the SSL certificate.');
        } else {
          alert(`Error uploading folder: ${err.message}`);
        }
      }
    };
    
    fileInput.click();
  };

  const handleLogout = async () => {
    try {
      const response = await apiService.logout();
      if (!response.ok) throw new Error("Logout failed");
      alert("Logged out successfully!");
      window.location.href = "/"; // redirect to login page
    } catch (err) {
      console.error(err);
      alert("Error logging out");
    }
  };

  // Get search context label
  const getSearchContext = () => {
    if (activeView === "recycle") {
      return "Recycle Bin";
    } else if (currentFolderId) {
      return `Current Folder`;
    } else {
      return "Root";
    }
  };

  return (
    <header className="topbar">
      <div className="search-container">
        <form onSubmit={handleSearch} className="search-form">
          <input 
            type="text" 
            placeholder={`Search in ${getSearchContext()}...`}
            className="search-bar"
            value={searchTerm}
            onChange={handleSearchInputChange}
            disabled={isSearching}
          />
          <button 
            type="submit" 
            className="search-btn"
            disabled={isSearching || !searchTerm.trim()}
          >
            {isSearching ? "🔍..." : "🔍"}
          </button>
          {searchTerm && (
            <button 
              type="button" 
              className="clear-search-btn"
              onClick={clearSearch}
              title="Clear search"
            >
              ✕
            </button>
          )}
        </form>
        <div className="search-context">
          Searching in: <span className="context-label">{getSearchContext()}</span>
        </div>
      </div>

      <div className="actions">
        <button className="create-btn" onClick={checkCurrentUser}>
          Check Auth
        </button>

        <button 
          className={`create-btn ${activeView === "recycle" ? "disabled" : ""}`} 
          onClick={activeView === "recycle" ? () => alert("Cannot create folders in recycle bin") : handleCreateFolder}
          disabled={activeView === "recycle"}
        >
          + Create Folder
        </button>

        <div className="upload-dropdown">
          <button
            className={`upload-btn ${activeView === "recycle" ? "disabled" : ""}`}
            onClick={activeView === "recycle" ? () => alert("Cannot upload files in recycle bin") : () => setShowUploadMenu(!showUploadMenu)}
            disabled={activeView === "recycle"}
          >
            ⬆ Upload
          </button>
          {showUploadMenu && (
            <div className="dropdown-menu">
              <button onClick={handleUploadFile}>Upload File</button>
              <button onClick={handleUploadFolder}>Upload Folder</button>
            </div>
          )}
        </div>

        <button className="logout-btn" onClick={handleLogout}>
          🚪 Logout
        </button>
      </div>

      {/* Create Folder Modal */}
      {showCreateFolderModal && (
        <div className="modal-overlay" onClick={() => setShowCreateFolderModal(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <h3>Create New Folder</h3>
            <input
              type="text"
              placeholder="Enter folder name..."
              value={folderName}
              onChange={(e) => setFolderName(e.target.value)}
              onKeyPress={(e) => e.key === 'Enter' && handleCreateFolderSubmit()}
              autoFocus
            />
            <div className="modal-buttons">
              <button onClick={() => setShowCreateFolderModal(false)}>Cancel</button>
              <button onClick={handleCreateFolderSubmit} disabled={!folderName.trim()}>
                Create
              </button>
            </div>
          </div>
        </div>
      )}
    </header>
  );
}

export default Topbar;
