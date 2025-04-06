const MAX_CHUNK_SIZE = 2 * 1024 * 1024;

function setupUpload() {
    const uploadButton = document.getElementById("uploadFileButton");
    const fileInput = document.getElementById("hiddenFileInput");

    if (!uploadButton || !fileInput || uploadButton.dataset.initialized === "true") return;

    uploadButton.dataset.initialized = "true";

    uploadButton.addEventListener("click", () => {
        fileInput.click();
    });

    fileInput.addEventListener("change", async () => {
        const file = fileInput.files[0];
        if (!file) return;

        const totalChunks = Math.ceil(file.size / MAX_CHUNK_SIZE);

        for (let i = 0; i < totalChunks; i++) {
            const chunk = file.slice(i * MAX_CHUNK_SIZE, (i + 1) * MAX_CHUNK_SIZE);
            const formData = new FormData();
            formData.append("chunk", chunk);
            formData.append("metadata.FileName", file.name);
            formData.append("metadata.ChunkIndex", i);
            formData.append("metadata.TotalChunks", totalChunks);

            try {
                const response = await fetch("/Content/profile_page?handler=UploadChunk", {
                    method: "POST",
                    headers: {
                        "Accept": "application/json",
                        "X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').getAttribute("content")
                    },
                    body: formData
                });

                const result = await response.json();
                if (!result.success) {
                    console.error("Ошибка при загрузке чанка:", result.error);
                    break;
                }
            } catch (err) {
                console.error("Ошибка при отправке чанка:", err);
                break;
            }
        }

        fileInput.value = "";
    });
}

// Сначала пробуем сразу
setupUpload();

// Затем следим через MutationObserver
const uploadFileMutationObserver = new MutationObserver(() => {
    setupUpload();
});

uploadFileMutationObserver.observe(document.body, { childList: true, subtree: true });
