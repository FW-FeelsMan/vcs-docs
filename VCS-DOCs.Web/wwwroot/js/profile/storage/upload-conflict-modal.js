(function () {
    function $(sel, root) { return (root || document).querySelector(sel); }

    function showConflictModal(fileName, conflictType, handlers) {
        var modal = $("#upload-version-modal");
        if (!modal) { console.error("upload-version-modal not found"); return; }

        var title = $("#version-modal-title", modal);
        var message = $("#version-conflict-message", modal);
        var cancelBtn = $("#conflict-cancel", modal);
        var newVersionBtn = $("#conflict-new-version", modal);

        var replaceContainer = $("#split-button", modal);
        var selectedVersionSpan = $("#selected-version", modal);
        var dropdownArrow = $("#version-dropdown", modal);
        var versionList = $("#version-list", modal);

        if (!title || !message || !cancelBtn || !newVersionBtn || !replaceContainer || !selectedVersionSpan || !dropdownArrow || !versionList) {
            console.error("modal parts missing");
            return;
        }

        title.textContent = "Конфликт версий";
        message.textContent = 'Файл "' + fileName + '" уже существует. Выберите действие.';

        var selectedVersion = null;
        selectedVersionSpan.textContent = "V?";
        versionList.innerHTML = "";
        versionList.style.display = "none";

        fetch('/api/Upload/versions?fileName=' + encodeURIComponent(fileName))
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (versions) {
                if (Array.isArray(versions) && versions.length) {
                    selectedVersion = versions[0].version ?? versions[0].Version;
                    selectedVersionSpan.textContent = "V" + selectedVersion;
                    versions.forEach(function (ver) {
                        var v = ver.version ?? ver.Version;
                        var dt = ver.uploadedAt ?? ver.UploadedAt;
                        var item = document.createElement("div");
                        item.className = "dropdown-item";
                        item.textContent = "V" + v + " (" + new Date(dt).toLocaleString() + ")";
                        item.onclick = function () {
                            selectedVersion = v;
                            selectedVersionSpan.textContent = "V" + v;
                            versionList.style.display = "none";
                        };
                        versionList.appendChild(item);
                    });
                } else {
                    selectedVersionSpan.textContent = "Нет версий";
                }
            })
            .catch(function (e) {
                console.error("versions load error", e);
                selectedVersionSpan.textContent = "Ошибка";
            });

        function toggleList(e) {
            var vis = versionList.style.display === "block";
            versionList.style.display = vis ? "none" : "block";
            if (e) e.stopPropagation();
        }
        dropdownArrow.onclick = toggleList;
        selectedVersionSpan.onclick = toggleList;
        document.addEventListener("click", function (e) {
            if (!replaceContainer.contains(e.target)) versionList.style.display = "none";
        });

        replaceContainer.onclick = function (e) {
            if (e.target === dropdownArrow || e.target === selectedVersionSpan) return;
            if (selectedVersion == null) { alert("Выберите версию для замены"); return; }
            modal.style.display = "none";
            if (handlers && typeof handlers.onReplace === "function") handlers.onReplace(selectedVersion);
        };
        newVersionBtn.onclick = function () {
            modal.style.display = "none";
            if (handlers && typeof handlers.onNewVersion === "function") handlers.onNewVersion();
        };
        cancelBtn.onclick = function () {
            modal.style.display = "none";
            if (handlers && typeof handlers.onCancel === "function") handlers.onCancel();
        };

        modal.style.display = "block";
    }

    window.showConflictModal = showConflictModal;
})();