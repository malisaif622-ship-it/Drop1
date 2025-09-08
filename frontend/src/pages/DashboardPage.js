import React, { useState } from "react";
import Sidebar from "../components/Sidebar";
import Topbar from "../components/Topbar";
import FilesSection from "../components/FilesSectionEnhanced";
import "./DashboardPage.css";

function DashboardPage() {
  const [activeView, setActiveView] = useState("files");
  const [searchResults, setSearchResults] = useState(null);

  const handleViewChange = (view) => {
    setActiveView(view);
    // Clear search when changing views
    setSearchResults(null);
  };

  const handleSearchResults = (results) => {
    setSearchResults(results);
    // Switch to files view when searching
    if (results) {
      setActiveView("files");
    }
    // If results is null, it means clear search - just set searchResults to null
    // and the component will automatically reload
  };

  return (
    <div className="dashboard-container">
      <Sidebar activeView={activeView} onViewChange={handleViewChange} />
      <div className="main-content">
        <Topbar onSearchResults={handleSearchResults} />
        <div className="content-area">
          <h2>Welcome to your Dashboard</h2>
          <FilesSection 
            activeView={activeView} 
            searchResults={searchResults}
          />
        </div>
      </div>
    </div>
  );
}

export default DashboardPage;
