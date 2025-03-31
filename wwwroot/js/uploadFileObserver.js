const uploadFileObserver = new MutationObserver((mutations) => {
    document.querySelectorAll('#uploadFileButton').forEach(button => {
        if (!button.classList.contains('uploadFile-observed')) {
            button.addEventListener('click', function () {
                document.getElementById('hiddenFileInput').click();
            });
            button.classList.add('uploadFile-observed');
        }
    });
    const fileInput = document.getElementById('hiddenFileInput');
    if (fileInput && !fileInput.classList.contains('uploadFileInput-observed')) {
        fileInput.addEventListener('change', function (e) {
            e.preventDefault();
            const formData = new FormData(document.getElementById('uploadForm'));
            fetch("/Content/profile_page?handler=UploadFile", {
                method: "POST",
                headers: { "Accept": "application/json" },
                body: formData
            })
                .then(response => response.json())
                .then(data => {
                    console.log("Файл успешно загружен", data);
                    fileInput.value = "";
                })
                .catch(error => {
                    console.error("Ошибка при загрузке файла", error);
                    fileInput.value = "";
                });
        });
        fileInput.classList.add('uploadFileInput-observed');
    }
});
uploadFileObserver.observe(document.body, {
    childList: true,
    subtree: true
});
