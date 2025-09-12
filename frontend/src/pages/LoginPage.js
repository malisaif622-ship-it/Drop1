import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import "./LoginPage.css";

function LoginPage() {
  const [userId, setUserId] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [debugInfo, setDebugInfo] = useState("");
  const navigate = useNavigate();

  const testConnection = async () => {
    setDebugInfo("Testing connection...");
    try {
      console.log("Testing connection to backend...");
      const response = await fetch("https://localhost:7294/auth/me", {
        method: "GET",
        credentials: "include",
      });
      
      console.log("Test response status:", response.status);
      
      if (response.status === 401) {
        setDebugInfo("✅ Backend is reachable! (401 = Not logged in, which is expected before login)");
      } else if (response.ok) {
        const userData = await response.json();
        setDebugInfo(`✅ Backend is reachable and you're already logged in! User: ${JSON.stringify(userData)}`);
      } else {
        setDebugInfo(`⚠️ Backend responded with unexpected status: ${response.status}`);
      }
    } catch (err) {
      console.error("Connection test failed:", err);
      setDebugInfo(`❌ Connection failed: ${err.message} - Please ensure backend is running on https://localhost:7294`);
    }
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError(""); 
    setDebugInfo("Starting login process...");

    // Validate inputs
    if (!userId || !password) {
      setError("Please enter both UserID and Password");
      return;
    }

    if (isNaN(parseInt(userId))) {
      setError("UserID must be a number");
      return;
    }

    try {
      setDebugInfo("Sending login request...");
      console.log("Attempting login with UserID:", userId);
      
      const requestBody = { 
        UserID: parseInt(userId),
        Password: password 
      };
      
      console.log("Request body:", requestBody);
      setDebugInfo(`Request body: ${JSON.stringify(requestBody)}`);
      
      const response = await fetch("https://localhost:7294/auth/login", {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(requestBody),
      });

      console.log("Response status:", response.status);
      console.log("Response ok:", response.ok);
      setDebugInfo(`Response status: ${response.status}, ok: ${response.ok}`);

      if (response.ok) {
        const data = await response.json();
        console.log("Login successful:", data);
        setDebugInfo("Login successful! Redirecting...");
        // Store user info in localStorage for quick access
        localStorage.setItem('user', JSON.stringify(data));
        navigate("/dashboard");
      } else {
        const errorData = await response.text();
        console.error("Login failed:", errorData);
        setError(errorData || "Invalid UserID or Password");
        setDebugInfo(`Login failed: ${errorData}`);
      }
    } catch (err) {
      console.error("Login error:", err);
      setDebugInfo(`Error: ${err.message}`);
      
      if (err.name === 'TypeError' && (err.message.includes('fetch') || err.message.includes('Failed to fetch'))) {
        setError("Network Error: Cannot connect to server. Please ensure your backend is running on https://localhost:7294 and your browser trusts the SSL certificate.");
      } else {
        setError("Something went wrong. Try again. Error: " + err.message);
      }
    }
  };

  return (
    <div className="login-container">
      <form className="login-form" onSubmit={handleSubmit}>
        <h2>Login</h2>
        {error && <p className="error" style={{color: 'red'}}>{error}</p>}
        {debugInfo && <p className="debug" style={{color: 'blue', fontSize: '12px'}}>{debugInfo}</p>}

        <label>User ID</label>
        <input
          type="number"
          value={userId}
          onChange={(e) => setUserId(e.target.value)}
          placeholder="Enter your User ID (number)"
          required
        />

        <label>Password</label>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />

        <button type="submit">Login</button>
        
        <button type="button" onClick={testConnection} style={{marginTop: '10px', backgroundColor: '#6c757d'}}>
          Test Backend Connection
        </button>
      </form>
    </div>
  );
}

export default LoginPage;
