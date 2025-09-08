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

    const url = parentFolderId 
      ? `/api/file/upload-file?parentFolderId=${parentFolderId}`
      : '/api/file/upload-file';

    return this.fetchWithCredentials(url, {
      method: 'POST',
      headers: {}, // Remove Content-Type for FormData
      body: formData,
    });
  }

  async deleteFile(fileId) {
    return this.fetchWithCredentials(`/api/file/delete-file?fileId=${fileId}`, {
      method: 'DELETE',
    });
  }

  async renameFile(fileId, newName) {
    return this.fetchWithCredentials(`/api/file/rename/${fileId}?newName=${newName}`, {
      method: 'PUT',
    });
  }

  async downloadFile(fileId) {
    return this.fetchWithCredentials(`/api/file/download-file/${fileId}`);
  }

  async recoverFile(fileId) {
    return this.fetchWithCredentials(`/api/file/recover-file/${fileId}`, {
      method: 'PUT',
    });
  }

  // Folder APIs
  async createFolder(folderName, parentFolderId = null) {
    const url = parentFolderId 
      ? `/api/folder/create?folderName=${encodeURIComponent(folderName)}&parentFolderId=${parentFolderId}`
      : `/api/folder/create?folderName=${encodeURIComponent(folderName)}`;
    
    return this.fetchWithCredentials(url, {
      method: 'POST',
    });
  }

  async uploadFolder(files, parentFolderId = null) {
    const formData = new FormData();
    for (let i = 0; i < files.length; i++) {
      formData.append('files', files[i]);
    }

    const url = parentFolderId 
      ? `/api/folder/upload-folder?parentFolderId=${parentFolderId}`
      : '/api/folder/upload-folder';

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
    return this.fetchWithCredentials(`/api/folder/rename?folderId=${folderId}&newName=${encodeURIComponent(newName)}`, {
      method: 'PUT',
    });
  }

  async deleteFolder(folderId) {
    return this.fetchWithCredentials(`/api/folder/delete?folderId=${folderId}`, {
      method: 'DELETE',
    });
  }

  async recoverFolder(folderId) {
    return this.fetchWithCredentials(`/api/folder/recover/${folderId}`, {
      method: 'PUT',
    });
  }

  async downloadFolder(folderId) {
    return this.fetchWithCredentials(`/api/folder/download/${folderId}`);
  }

  async getFolderDetails(folderId) {
    return this.fetchWithCredentials(`/api/folder/details/${folderId}`);
  }

  // Get all items API
  async getAllItems() {
    console.log("Calling getAllItems endpoint");
    const response = await this.fetchWithCredentials('/api/search/all');
    console.log("GetAllItems response status:", response.status);
    return response;
  }

  // Search API (for search bar functionality)
  async search(keyword) {
    if (!keyword || keyword.trim() === '') {
      throw new Error('Search keyword is required');
    }
    const url = `/api/search?keyword=${encodeURIComponent(keyword)}`;
    console.log("Calling search endpoint:", `${API_BASE_URL}${url}`);
    const response = await this.fetchWithCredentials(url);
    console.log("Search response status:", response.status);
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
}

const apiService = new ApiService();
export default apiService;
