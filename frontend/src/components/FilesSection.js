import React from "react";
import "./FilesSection.css";

function FilesSection() {
  // Dummy data for now – later we’ll replace this with API data
  const files = [
    { id: 1, name: "Uploaded Folder", type: "folder" },
    { id: 2, name: "Project.docx", type: "file" },
    { id: 3, name: "Notes.pdf", type: "file" },
    { id: 4, name: "Uploaded Folder (2)", type: "folder" },
  ];

  return (
    <div className="files-section">
      <h3>Your Files & Folders</h3>
      <div className="files-grid">
        {files.map((file) => (
          <div
            key={file.id}
            className={`file-card ${file.type === "folder" ? "folder" : "file"}`}
          >
            <div className="file-icon">
              {file.type === "folder" ? "📁" : "📄"}
            </div>
            <div className="file-name">{file.name}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

export default FilesSection;
