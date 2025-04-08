const MAX_CHUNK_SIZE = 2 * 1024 * 1024; // 2MB

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

        const formData = new FormData();
        formData.append("fileName", file.name);
        formData.append("fileSize", file.size);

        const reserveResponse = await fetch("/Content/profile_page?handler=TryReserve", {
            method: "POST",
            headers: {
                "Accept": "application/json",
                "X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').getAttribute("content")
            },
            body: formData
        });

        const reserveResult = await reserveResponse.json();
        if (!reserveResult.success) {
            alert(reserveResult.error);
            fileInput.value = "";
            return;
        }

        const totalChunks = Math.ceil(file.size / MAX_CHUNK_SIZE);
        for (let i = 0; i < totalChunks; i++) {
            const chunk = file.slice(i * MAX_CHUNK_SIZE, (i + 1) * MAX_CHUNK_SIZE);
            const chunkForm = new FormData();
            chunkForm.append("chunk", chunk);
            chunkForm.append("metadata.FileName", file.name);
            chunkForm.append("metadata.ChunkIndex", i);
            chunkForm.append("metadata.TotalChunks", totalChunks);

            try {
                const response = await fetch("/Content/profile_page?handler=UploadChunk", {
                    method: "POST",
                    headers: {
                        "Accept": "application/json",
                        "X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').getAttribute("content")
                    },
                    body: chunkForm
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

// Сразу при загрузке
setupUpload();

// И на динамический DOM
const uploadObserver = new MutationObserver(() => {
    setupUpload();
});
uploadObserver.observe(document.body, { childList: true, subtree: true });
