const uploadFileObserver = new MutationObserver((mutations) => {
    document.querySelectorAll('#uploadFileButton').forEach(button => {
        if (!button.classList.contains('uploadFile-observed')) {
            button.addEventListener('click', function () {
                document.getElementById('hiddenFileInput').click();
            });
            button.classList.add('uploadFile-observed');
        }
    });
});

uploadFileObserver.observe(document.body, {
    childList: true,
    subtree: true
});
