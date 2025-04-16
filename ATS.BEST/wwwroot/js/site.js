// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
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
    // Show immediate feedback that upload is starting
    showLoading("Starting file upload...", 1);

    const pdfFiles = [...files].filter(file => file.type === "application/pdf");
    if (pdfFiles.length === 0) {
        alert("Please upload only PDF files.");
        hideLoading();
        return;
    }

    const formData = new FormData();
    pdfFiles.forEach((file, index) => {
        formData.append("cvs", file);
        console.log(`Adding file: ${file.name}, size: ${file.size}`);
    });

    const jobDesc = jobDescription.value;
    if (jobDesc) {
        formData.append("jobDescription", jobDesc);
    }
    else {
        alert("Please enter a job description.");
        hideLoading();
        return;
    }

    // Add connection ID for our fixed signalR issue
    formData.append("connectionId", connection.connectionId);

    console.log("Starting fetch request...");
    fetch(`${window.location.origin}/api/routing/upload`, {
        method: "POST",
        body: formData,
    })
        .then(response => {
            console.log("Response status:", response.status);
            if (!response.ok) {
                throw new Error(`HTTP error! Status: ${response.status}`);
            }
            return response.json();
        })
        .then(data => {
            console.log("Parsed server response:", data);
            populateTable(data);
        })
        .catch(err => {
            console.error("Upload error:", err);
            alert("Error uploading files: " + err.message);
            hideLoading();
        });
}

function populateTable(data) {
    const tableBody = document.querySelector("#resultsTable tbody");
    tableBody.innerHTML = "";

    data.forEach(entry => {
        const name = entry.cv?.name || "Unknown";
        const email = entry.cv?.contacts?.email || "No email";
        const score = entry.scores?.finalScore ?? "N/A";

        const row = document.createElement("tr");

        row.innerHTML = `
            <td>${name}</td>
            <td>${email}</td>
            <td>${Number(score).toFixed(0)}%</td>
        `;

        // Attach click listener to show the panel
        row.addEventListener("click", () => {
            showPanel(entry.aiEvaluation || "No AI evaluation provided.");
        });

        tableBody.appendChild(row);
    });
}

function showPanel(text) {
    const panel = document.getElementById("infoPanel");
    const content = document.getElementById("panelContent");

    content.innerHTML = text.replace(/\n/g, "<br>")

    // content.textContent = text;
    panel.classList.add("visible");
}

document.getElementById("closePanel").addEventListener("click", () => {
    const panel = document.getElementById("infoPanel");
    panel.classList.remove("visible");
});


const connection = new signalR.HubConnectionBuilder()
    .withUrl("/progressHub")
    .build();

connection.on("ReceiveProgress", function (message, percentage) {
    showLoading(message, percentage);
    if (message === "Done!" || percentage === 100) {
        setTimeout(() => hideLoading(), 1000); // Show 100% briefly before hiding
    }
});

connection.start();

function showLoading(message, percentage) {
    document.getElementById("loadingContainer").style.display = "flex";
    document.getElementById("loadingText").innerText = message;
    document.getElementById("progressBar").style.width = percentage + "%";
    document.getElementById("progressPercentage").innerText = percentage + "%";
}

function hideLoading() {
    document.getElementById("loadingContainer").style.display = "none";
}


