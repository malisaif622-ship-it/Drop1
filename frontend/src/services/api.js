// API service for communicating with the backend
const API_BASE_URL = 'https://localhost:7294';

class ApiService {
  // Common fetch wrapper with credentials and better error handling
  async fetchWithCredentials(url, options = {}) {
    const config = {
      credentials: 'include', // This is crucial for session cookies
      mode: 'cors', // Explicitly set CORS mode
      headers: {
        'Content-Type': 'application/json',
        ...options.headers,
      },
      ...options,
    };

    try {
      console.log(`Making request to: ${API_BASE_URL}${url}`);
      console.log('Request config:', config);
      
      const response = await fetch(`${API_BASE_URL}${url}`, config);
      
      console.log(`Response status for ${url}:`, response.status);
      console.log(`Response headers:`, response.headers);
      
      return response;
    } catch (error) {
      // Handle network errors or CORS issues
      console.error('Network error:', error);
      throw new Error('Network error: Please ensure the backend server is running and CORS is configured properly');
    }
  }

  // Auth APIs
  async login(userID, password) {
    return this.fetchWithCredentials('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ UserID: parseInt(userID), Password: password }),
    });
  }

  async logout() {
    return this.fetchWithCredentials('/auth/logout', {
      method: 'POST',
    });
  }

  async getCurrentUser() {
    return this.fetchWithCredentials('/auth/me');
  }

  // File APIs
  async uploadFiles(files, parentFolderId = null) {
    const formData = new FormData();
    for (let i = 0; i < files.length; i++) {
      formData.append('files', files[i]);
    }

    // Defensive check and logging for parentFolderId
    console.log("API: uploadFiles called with parentFolderId:", parentFolderId, "type:", typeof parentFolderId);
    
    if (parentFolderId !== null && (typeof parentFolderId === 'undefined' || parentFolderId === 'undefined')) {
      console.error('API: Invalid parentFolderId passed to uploadFiles:', parentFolderId);
      throw new Error('Invalid parent folder ID');
    }

    const url = parentFolderId 
      ? `/api/file/upload-file?parentFolderId=${encodeURIComponent(parentFolderId)}`
      : '/api/file/upload-file';

    console.log("API: uploadFiles URL:", url);

    return this.fetchWithCredentials(url, {
      method: 'POST',
      headers: {}, // Remove Content-Type for FormData
      body: formData,
    });
  }

  async deleteFile(fileId) {
    return this.fetchWithCredentials(`/api/file/delete-file?fileId=${encodeURIComponent(fileId)}`, {
      method: 'DELETE',
    });
  }

  async renameFile(fileId, newName) {
    const url = `/api/file/rename/${encodeURIComponent(fileId)}?newName=${encodeURIComponent(newName)}`;
    return this.fetchWithCredentials(url, {
      method: 'PUT',
    });
  }

  async downloadFile(fileId) {
    const response = await this.fetchWithCredentials(`/api/file/download-file/${encodeURIComponent(fileId)}`);
    if (response.ok) {
      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = ''; // Let browser determine filename from headers
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
    }
    return response;
  }

  async recoverFile(fileId) {
    return this.fetchWithCredentials(`/api/file/recover-file/${fileId}`, {
      method: 'PUT',
    });
  }

  async getFileDetails(fileId) {
    return this.fetchWithCredentials(`/api/file/file/details/${encodeURIComponent(fileId)}`);
  }

  // Folder APIs
  async createFolder(folderName, parentFolderId = null) {
    // Defensive check and logging for parentFolderId
    console.log("API: createFolder called with folderName:", folderName, "parentFolderId:", parentFolderId, "type:", typeof parentFolderId);
    
    if (parentFolderId !== null && (typeof parentFolderId === 'undefined' || parentFolderId === 'undefined')) {
      console.error('API: Invalid parentFolderId passed to createFolder:', parentFolderId);
      throw new Error('Invalid parent folder ID');
    }

    const url = parentFolderId 
      ? `/api/folder/create?folderName=${encodeURIComponent(folderName)}&parentFolderId=${encodeURIComponent(parentFolderId)}`
      : `/api/folder/create?folderName=${encodeURIComponent(folderName)}`;
    
    console.log("API: createFolder URL:", url);

    return this.fetchWithCredentials(url, {
      method: 'POST',
    });
  }

  async uploadFolder(files, parentFolderId = null) {
    const formData = new FormData();
    for (let i = 0; i < files.length; i++) {
      formData.append('files', files[i]);
    }

    // Defensive check and logging for parentFolderId
    console.log("API: uploadFolder called with parentFolderId:", parentFolderId, "type:", typeof parentFolderId);
    
    if (parentFolderId !== null && (typeof parentFolderId === 'undefined' || parentFolderId === 'undefined')) {
      console.error('API: Invalid parentFolderId passed to uploadFolder:', parentFolderId);
      throw new Error('Invalid parent folder ID');
    }

    const url = parentFolderId 
      ? `/api/folder/upload-folder?parentFolderId=${encodeURIComponent(parentFolderId)}`
      : '/api/folder/upload-folder';

    console.log("API: uploadFolder URL:", url);

    return this.fetchWithCredentials(url, {
      method: 'POST',
      headers: {}, // Remove Content-Type for FormData
      body: formData,
    });
  }

  async listItems(parentFolderId = null) {
    const url = parentFolderId 
      ? `/api/search/list?parentFolderId=${parentFolderId}`
      : `/api/search/list`;
    
    console.log("Calling listItems endpoint:", `${API_BASE_URL}${url}`);
    const response = await this.fetchWithCredentials(url);
    console.log("listItems response status:", response.status);
    
    return response;
  }


  async renameFolder(folderId, newName) {
    return this.fetchWithCredentials(`/api/folder/rename?folderId=${encodeURIComponent(folderId)}&newName=${encodeURIComponent(newName)}`, {
      method: 'PUT',
    });
  }

  async deleteFolder(folderId) {
    return this.fetchWithCredentials(`/api/folder/delete?folderId=${encodeURIComponent(folderId)}`, {
      method: 'DELETE',
    });
  }

  async recoverFolder(folderId) {
    return this.fetchWithCredentials(`/api/folder/recover/${folderId}`, {
      method: 'PUT',
    });
  }

  async downloadFolder(folderId) {
    const response = await this.fetchWithCredentials(`/api/folder/download/${encodeURIComponent(folderId)}`);
    if (response.ok) {
      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = ''; // Let browser determine filename from headers
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
    }
    return response;
  }

  async getFolderDetails(folderId) {
    return this.fetchWithCredentials(`/api/folder/details/${encodeURIComponent(folderId)}`);
  }

  // Get all items API
  async getAllItems() {
    console.log("Calling getAllItems endpoint");
    const response = await this.fetchWithCredentials('/api/search/all');
    console.log("GetAllItems response status:", response.status);
    return response;
  }

  // Get user storage info
  async getUserStorageInfo() {
    console.log("Getting user storage information");
    const response = await this.fetchWithCredentials('/auth/me');
    console.log("User storage info response status:", response.status);
    return response;
  }

  // Search API (for search bar functionality with context)
  async search(keyword, parentFolderId = null, deletedOnly = false) {
    if (!keyword || keyword.trim() === '') {
      // For empty searches, use the contextual list endpoint
      return this.getContextualList(parentFolderId, deletedOnly);
    }
    
    let url = `/api/search?keyword=${encodeURIComponent(keyword)}`;
    if (parentFolderId !== null) {
      url += `&parentFolderId=${parentFolderId}`;
    }
    if (deletedOnly) {
      url += `&deletedOnly=true`;
    }
    
    console.log("Calling contextual search endpoint:", `${API_BASE_URL}${url}`);
    const response = await this.fetchWithCredentials(url);
    console.log("Contextual search response status:", response.status);
    return response;
  }

  // Get contextual list (for folder browsing and empty searches)
  async getContextualList(parentFolderId = null, deletedOnly = false) {
    let url = `/api/search/contextual-list`;
    const params = [];
    
    if (parentFolderId !== null) {
      params.push(`parentFolderId=${parentFolderId}`);
    }
    if (deletedOnly) {
      params.push(`deletedOnly=true`);
    }
    
    if (params.length > 0) {
      url += `?${params.join('&')}`;
    }
    
    console.log("Calling contextual list endpoint:", `${API_BASE_URL}${url}`);
    const response = await this.fetchWithCredentials(url);
    console.log("Contextual list response status:", response.status);
    return response;
  }



  // New APIs for getting files and folders
  async getUserFilesAndFolders(folderId = null) {
    // Try different potential endpoints
    const endpoints = [
      '/api/file/list',
      '/api/files',
      '/api/user/files',
      '/api/folder/contents',
      '/api/files-and-folders'
    ];
    
    for (const endpoint of endpoints) {
      try {
        console.log(`Trying endpoint: ${endpoint}`);
        const response = await this.fetchWithCredentials(endpoint);
        if (response.ok) {
          console.log(`Success with endpoint: ${endpoint}`);
          return response;
        }
        console.log(`Endpoint ${endpoint} failed with status:`, response.status);
      } catch (err) {
        console.log(`Endpoint ${endpoint} error:`, err.message);
      }
    }
    
    // If all fail, return search as final fallback
    return this.search('');
  }

  async getDeletedItems() {
    // Use the correct backend endpoint for deleted items
    console.log("Calling deleted items endpoint");
    const response = await this.fetchWithCredentials('/api/search/deleted');
    console.log("Deleted items response status:", response.status);
    return response;
  }

  // Permanent delete APIs
  async permanentDeleteFile(fileId) {
    return this.fetchWithCredentials(`/api/file/permanent-delete?fileId=${fileId}`, {
      method: 'DELETE',
    });
  }

  async permanentDeleteFolder(folderId) {
    return this.fetchWithCredentials(`/api/folder/permanent-delete?folderId=${folderId}`, {
      method: 'DELETE',
    });
  }

  // Details APIs
  async getFileDetails(fileId) {
    return this.fetchWithCredentials(`/api/file/details?fileId=${fileId}`);
  }

  async getFolderDetails(folderId) {
    return this.fetchWithCredentials(`/api/folder/details?folderId=${folderId}`);
  }

  // Generic GET method for backward compatibility
  async get(url) {
    console.log("Making generic GET request to:", url);
    const response = await this.fetchWithCredentials(url);
    console.log("Generic GET response status:", response.status);
    return response;
  }
}

const apiService = new ApiService();
export default apiService;
