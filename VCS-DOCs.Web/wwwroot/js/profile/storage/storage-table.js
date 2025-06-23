initStorageTable();

function initStorageTable() {
    const tableBody = document.querySelector('#userFilesTable tbody');
    const counter = document.getElementById('storageCounter');
    if (!tableBody) return console.warn("Элемент таблицы не найден");

    // Загрузка и отрисовка списка файлов + счётчика
    async function fetchFiles() {
        try {
            const res = await fetch('/api/storage/files');

            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            renderTable(data.files);

            const used = formatSize(data.usedBytes);
            const temp = formatSize(data.tempBytes);
            const limit = formatSize(data.limitBytes);
            const free = formatSize(data.limitBytes - data.usedBytes - data.tempBytes);

            counter.textContent =
                `Использовано: ${used} из ${limit} (временных: ${temp}); свободно: ${free}`;
        } catch (err) {
            console.error("Не удалось загрузить список файлов", err);
            counter.textContent = "Ошибка загрузки";
        }
    }

    function renderTable(files) {
        tableBody.innerHTML = '';
        files.forEach(file => {
            const row = document.createElement('tr');
            row.innerHTML = `
                <td>${escapeHtml(file.FileName)}</td>
                <td>${renderVersionDropdown(file)}</td>
                <td>${formatSize(file.FileSize)}</td>
                <td>${formatDate(file.UpdatedAt)}</td>
                <td>${renderActions()}</td>
            `;
            tableBody.appendChild(row);
        });
        setupAllVersionDropdowns(files);
        setupAllActionDropdowns(files);
    }

    function renderVersionDropdown(file) {
        return `
            <div class="multi-button compact version-multibutton"
                 data-file-id="${file.FileId}"
                 data-current-version="${file.LatestVersion}">
                <button class="button-sliding primary compact version-button"
                        data-version="${file.LatestVersion}">
                    v${file.LatestVersion}
                </button>
                <div class="dropdown-arrow compact">&#9662;</div>
            </div>`;
    }

    function renderActions() {
        return `
            <div class="multi-button compact action-multibutton" style="position: relative;">
                <button class="button-sliding primary compact action-button" data-action="delete">
                    Удалить
                </button>
                <div class="dropdown-arrow compact">&#9662;</div>
                <div class="action-dropdown-menu compact"
                     style="display: none; position: absolute; min-width: 120px;">
                    <div class="dropdown-item" data-action="download">Скачать</div>
                    <div class="dropdown-item" data-action="delete">Удалить</div>
                    <div class="dropdown-item" data-action="view">Просмотр</div>
                    <div class="dropdown-item" data-action="share">Поделиться</div>
                </div>
            </div>`;
    }

    function handleActionClick(action, file) {
        const fileId = file.FileId;
        const version = file.LatestVersion;
        const downloadUrl = `/api/upload/download/${fileId}/${version}`;

        switch (action) {
            case 'download':
                window.location.href = downloadUrl;
                break;
            case 'view':
                window.open(downloadUrl, '_blank');
                break;
            case 'share':
                navigator.clipboard.writeText(window.location.origin + downloadUrl)
                    .then(() => alert("Ссылка скопирована в буфер"))
                    .catch(() => alert("Не удалось скопировать ссылку"));
                break;
            case 'delete':
                if (!confirm("Удалить файл?")) return;
                fetch(`/api/upload/delete/${fileId}/${version}`, { method: 'DELETE' })
                    .then(r => {
                        if (!r.ok) throw new Error("Ошибка удаления");
                        return r.json();
                    })
                    .then(data => {
                        if (data.status === 'deleted') {
                            fetchFiles();
                        } else {
                            alert("Не удалось удалить файл");
                        }
                    })
                    .catch(err => {
                        console.error("Ошибка при удалении файла:", err);
                        alert("Не удалось удалить файл");
                    });
                break;
        }
    }

    function escapeHtml(str) {
        const div = document.createElement("div");
        div.textContent = str;
        return div.innerHTML;
    }

    function formatSize(bytes) {
        return (bytes / 1024 / 1024).toFixed(2) + ' МБ';
    }

    function formatDate(dateStr) {
        return new Date(dateStr).toLocaleString();
    }

    // Версионный дропдаун
    function setupAllVersionDropdowns(files) {
        document.getElementById('version-dropdown-menu')?.remove();

        document.querySelectorAll('.version-multibutton').forEach(wrapper => {
            const arrow = wrapper.querySelector('.dropdown-arrow');
            const button = wrapper.querySelector('.version-button');
            const fileId = wrapper.dataset.fileId;
            let currentVersion = wrapper.dataset.currentVersion;
            const file = files.find(f => f.FileId == fileId);
            if (!file) return;

            arrow.onclick = e => {
                e.stopPropagation();
                document.getElementById('version-dropdown-menu')?.remove();

                const menu = document.createElement('div');
                menu.id = 'version-dropdown-menu';
                menu.className = 'compact';
                const rect = wrapper.getBoundingClientRect();
                menu.style.position = 'absolute';
                menu.style.left = rect.left + 'px';
                menu.style.top = rect.bottom + 'px';
                menu.style.width = rect.width + 'px';

                file.Versions.forEach(v => {
                    const item = document.createElement('div');
                    item.className = 'dropdown-item';
                    item.textContent = 'v' + v.Version;
                    if (v.Version == currentVersion) {
                        item.style.background = '#e5f1fb';
                        item.style.fontWeight = 'bold';
                    }
                    item.onclick = () => {
                        button.textContent = 'v' + v.Version;
                        wrapper.dataset.currentVersion = v.Version;
                        button.dataset.version = v.Version;
                        currentVersion = v.Version;
                        menu.remove();
                    };
                    menu.appendChild(item);
                });

                document.body.appendChild(menu);
                setTimeout(() => {
                    document.addEventListener('click', function close(e) {
                        if (!menu.contains(e.target)) {
                            menu.remove();
                            document.removeEventListener('click', close);
                        }
                    });
                }, 0);
            };
        });
    }

    // Дропдаун действий
    function setupAllActionDropdowns(files) {
        document.querySelectorAll('.action-multibutton').forEach(dropdown => {
            const btn = dropdown.querySelector('.action-button');
            const arrow = dropdown.querySelector('.dropdown-arrow');
            const menu = dropdown.querySelector('.action-dropdown-menu');
            const items = menu.querySelectorAll('.dropdown-item');
            let selectedAction = btn.dataset.action || 'delete';

            arrow.onclick = e => {
                e.stopPropagation();
                document.querySelectorAll('.action-dropdown-menu')
                    .forEach(m => m !== menu && (m.style.display = 'none'));
                menu.style.display = menu.style.display === 'block' ? 'none' : 'block';
            };

            btn.onclick = () => {
                const row = dropdown.closest('tr');
                const fileId = row.querySelector('.version-multibutton').dataset.fileId;
                const version = row.querySelector('.version-button').dataset.version;
                handleActionClick(selectedAction, { FileId: fileId, LatestVersion: version });
            };

            items.forEach(item => {
                item.onclick = () => {
                    selectedAction = item.dataset.action;
                    btn.textContent = item.textContent;
                    btn.dataset.action = selectedAction;
                    menu.style.display = 'none';
                };
            });

            document.addEventListener('click', e => {
                if (!dropdown.contains(e.target)) menu.style.display = 'none';
            });
        });
    }

    // Запустить
    fetchFiles();
    window.fetchFiles = fetchFiles;
}
