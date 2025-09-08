import React, { useState } from "react";
import "./Topbar.css";
import apiService from "../services/api";

function Topbar({ onSearchResults }) {
  const [showUploadMenu, setShowUploadMenu] = useState(false);
  const [searchTerm, setSearchTerm] = useState("");
  const [isSearching, setIsSearching] = useState(false);

  const handleSearch = async (e) => {
    e.preventDefault();
    if (!searchTerm.trim()) {
      alert("Please enter a search term");
      return;
    }

    setIsSearching(true);
    try {
      console.log("Searching for:", searchTerm);
      const response = await apiService.search(searchTerm.trim());
      
      if (response.ok) {
        const searchResults = await response.json();
        console.log("Search results:", searchResults);
        
        // Pass results to parent component if callback is provided
        if (onSearchResults) {
          onSearchResults(searchResults);
        }
        
        const folders = searchResults.Folders || searchResults.folders || [];
        const files = searchResults.Files || searchResults.files || [];
        alert(`Search found ${folders.length} folders and ${files.length} files`);
      } else {
        const errorText = await response.text();
        console.error("Search failed:", errorText);
        alert(`Search failed: ${response.status} - ${errorText}`);
      }
    } catch (err) {
      console.error("Search error:", err);
      alert(`Search error: ${err.message}`);
    } finally {
      setIsSearching(false);
    }
  };

  const handleSearchInputChange = (e) => {
    setSearchTerm(e.target.value);
  };

  const clearSearch = () => {
    setSearchTerm("");
    // Reset to show all items if callback is provided
    if (onSearchResults) {
      onSearchResults(null); // null indicates to reload all items
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

  const handleCreateFolder = async () => {
    try {
      console.log("Creating folder with API service...");
      
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
      
      const response = await apiService.createFolder("New Folder");
      console.log("Create folder response status:", response.status);
      console.log("Create folder response ok:", response.ok);
      
      if (!response.ok) {
        const errorText = await response.text();
        console.error("Create folder error response:", errorText);
        throw new Error(errorText || "Failed to create folder");
      }
      const data = await response.json();
      alert("Folder created successfully!");
      console.log("Created folder:", data);
      // You might want to refresh the file list here
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
        const response = await apiService.uploadFiles(files);
        if (!response.ok) {
          const errorText = await response.text();
          throw new Error(errorText || "Failed to upload file");
        }
        alert("File(s) uploaded successfully!");
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
        const response = await apiService.uploadFolder(files);
        if (!response.ok) {
          const errorText = await response.text();
          throw new Error(errorText || "Failed to upload folder");
        }
        alert("Folder uploaded successfully!");
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

  return (
    <header className="topbar">
      <form onSubmit={handleSearch} className="search-form">
        <input 
          type="text" 
          placeholder="Search files and folders..." 
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

      <div className="actions">
        <button className="create-btn" onClick={checkCurrentUser}>
          Check Auth
        </button>

        <button className="create-btn" onClick={handleCreateFolder}>
          + Create Folder
        </button>

        <div className="upload-dropdown">
          <button
            className="upload-btn"
            onClick={() => setShowUploadMenu(!showUploadMenu)}
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
    </header>
  );
}

export default Topbar;
