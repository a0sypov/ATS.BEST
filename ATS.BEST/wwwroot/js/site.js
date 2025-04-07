﻿// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

function toggleJobDetails(id) {
    var element = document.getElementById(id);
    element.classList.toggle("open");
}


const dropZone = document.getElementById("drop-zone");
const fileInput = document.getElementById("file-input");
const jobDescription = document.getElementById("job-description-1");

// Open file dialog on click
dropZone.addEventListener("click", () => fileInput.click());

// Handle files from file dialog
fileInput.addEventListener("change", () => handleFiles(fileInput.files));

// Drag-and-drop functionality
dropZone.addEventListener("dragover", (e) => {
    e.preventDefault();
    dropZone.style.borderColor = "#333";
});

dropZone.addEventListener("dragleave", () => {
    dropZone.style.borderColor = "#999";
});

dropZone.addEventListener("drop", (e) => {
    e.preventDefault();
    dropZone.style.borderColor = "#999";
    const files = e.dataTransfer.files;
    handleFiles(files);
});

function handleFiles(files) {
    const pdfFiles = [...files].filter(file => file.type === "application/pdf");
    if (pdfFiles.length === 0) {
        alert("Please upload only PDF files.");
        return;
    }

    const formData = new FormData();
    pdfFiles.forEach((file, index) => {
        // Use 'files' as the key and allow multiple files
        formData.append("cvs", file);
    });

    const jobDesc = jobDescription.value;
    if (jobDesc) {
        formData.append("jobDescription", jobDesc);
    }
    else {
        alert("Please enter a job description.");
        return;
    }

    fetch("https://localhost:32769/api/routing/upload", {
        method: "POST",
        body: formData,
    })
        .then(response => response.json())
        .then(data => {
            console.log("Parsed server response:", data);
            const sortedData = data.sort((a, b) => {
                const aScore = parseFloat(a.scores?.finalScore ?? -Infinity);
                const bScore = parseFloat(b.scores?.finalScore ?? -Infinity);
                return bScore - aScore; // Descending
            });
            populateTable(data);
        })
        .catch(err => console.error("Upload error:", err));
}

function populateTable(data) {
    const tableBody = document.querySelector("#resultsTable tbody");
    tableBody.innerHTML = "";

    data.forEach(entry => {
        const name = entry.cv?.name || "Unknown";
        const email = entry.cv?.contacts?.email || "No email";
        const score = entry.scores?.finalScore/entry.scores?.maxScore * 100 ?? "N/A";

        const row = document.createElement("tr");

        row.innerHTML = `
            <td>${name}</td>
            <td>${email}</td>
            <td>${Number(score).toFixed(0)}%</td>
        `;

        tableBody.appendChild(row);
    });
}

