console.warn("storage-table загружен");
initStorageTable();

function initStorageTable() {
    const tableBody = document.querySelector('#userFilesTable tbody');
    const counter = document.getElementById('storageCounter');
    if (!tableBody) return console.warn("Элемент таблицы не найден");

    async function fetchFiles() {
        try {
            const res = await fetch('/api/Upload/list');
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const files = await res.json();
            renderTable(files);
            counter.textContent = `Файлов: ${files.length}`;
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
                <td>${renderActions(file)}</td>
            `;
            tableBody.appendChild(row);
        });
        setupAllVersionDropdowns(files);
        setupAllActionDropdowns(files);
    }

    function renderVersionDropdown(file) {
        return `
            <div class="multi-button compact version-multibutton" data-file-id="${file.FileId}" data-current-version="${file.LatestVersion}">
                <button class="button-sliding primary compact version-button" data-version="${file.LatestVersion}">v${file.LatestVersion}</button>
                <div class="dropdown-arrow compact">&#9662;</div>
            </div>
        `;
    }

    function renderActions(file) {
        return `
            <div class="multi-button compact action-multibutton" style="position: relative;">
                <button class="button-sliding primary compact action-button" data-action="delete">Удалить</button>
                <div class="dropdown-arrow compact">&#9662;</div>
                <div class="action-dropdown-menu compact" style="display: none; position: absolute; min-width: 120px;">
                    <div class="dropdown-item" data-action="download">Скачать</div>
                    <div class="dropdown-item" data-action="delete">Удалить</div>
                    <div class="dropdown-item" data-action="view">Просмотр</div>
                    <div class="dropdown-item" data-action="share">Поделиться</div>
                </div>
            </div>
        `;
    }

    function handleActionClick(action, file) {
        const url = `/api/Upload/download/${file.FileId}?v=${file.LatestVersion}`;
        switch (action) {
            case 'download':
                window.location.href = url;
                break;
            case 'view':
                window.open(url, '_blank');
                break;
            case 'share':
                navigator.clipboard.writeText(window.location.origin + url)
                    .then(() => alert("Ссылка скопирована в буфер"))
                    .catch(() => alert("Не удалось скопировать ссылку"));
                break;
            case 'delete':
                if (!confirm("Удалить файл?")) return;
                fetch(`/api/Upload/delete/${file.FileId}?v=${file.LatestVersion}`, { method: 'DELETE' })
                    .then(r => {
                        if (!r.ok) throw new Error("Ошибка удаления");
                        fetchFiles();
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

    // ------- Версионный дропдаун (split button, 1 меню на всю страницу) -------
    function setupAllVersionDropdowns(files) {
        const oldMenu = document.getElementById('version-dropdown-menu');
        if (oldMenu) oldMenu.remove();

        document.querySelectorAll('.version-multibutton').forEach(wrapper => {
            const arrow = wrapper.querySelector('.dropdown-arrow');
            const button = wrapper.querySelector('.version-button');
            const fileId = wrapper.getAttribute('data-file-id');
            let currentVersion = wrapper.getAttribute('data-current-version');
            const file = files.find(f => f.FileId == fileId);
            if (!arrow || !button || !file) return;

            arrow.onclick = function (e) {
                e.stopPropagation();
                // Закрыть другие меню
                const existing = document.getElementById('version-dropdown-menu');
                if (existing) existing.remove();

                // Создать меню
                const menu = document.createElement('div');
                menu.id = 'version-dropdown-menu';
                menu.className = 'compact';

                // Позиционирование меню точно под кнопкой
                const rect = wrapper.getBoundingClientRect();
                menu.style.position = 'absolute';
                menu.style.left = (rect.left + window.scrollX) + 'px';
                menu.style.top = (rect.bottom + window.scrollY) + 'px';
                menu.style.width = rect.width + 'px';

                file.Versions.forEach(v => {
                    const item = document.createElement('div');
                    item.className = 'dropdown-item';
                    item.textContent = 'v' + v.Version;
                    if (v.Version == currentVersion) {
                        item.style.background = '#e5f1fb';
                        item.style.fontWeight = 'bold';
                    }
                    item.onclick = function () {
                        button.textContent = 'v' + v.Version;
                        wrapper.setAttribute('data-current-version', v.Version);
                        button.setAttribute('data-version', v.Version);
                        menu.remove();
                        currentVersion = v.Version;
                    };
                    menu.appendChild(item);
                });

                document.body.appendChild(menu);

                // Клик вне меню — закрыть
                function closeMenuOnClick(e) {
                    if (!menu.contains(e.target)) {
                        menu.remove();
                        document.removeEventListener('click', closeMenuOnClick);
                    }
                }
                setTimeout(() => document.addEventListener('click', closeMenuOnClick), 0);
            };
        });
    }

    // ------- Дропдаун действий (split-button с отдельным действием) -------
    function setupAllActionDropdowns(files) {
        document.querySelectorAll('.action-multibutton').forEach(dropdown => {
            const button = dropdown.querySelector('.action-button');
            const arrow = dropdown.querySelector('.dropdown-arrow');
            const menu = dropdown.querySelector('.action-dropdown-menu');
            const items = menu.querySelectorAll('.dropdown-item');
            let selectedAction = button.getAttribute('data-action') || 'delete';

            // Открытие меню только по стрелке
            arrow.onclick = function (e) {
                e.stopPropagation();
                document.querySelectorAll('.action-dropdown-menu').forEach(m => {
                    if (m !== menu) m.style.display = 'none';
                });
                menu.style.display = (menu.style.display === 'block') ? 'none' : 'block';
            };

            // Клик по основной кнопке — выполнить действие
            button.onclick = function (e) {
                const row = dropdown.closest('tr');
                const fileId = row.querySelector('.version-multibutton')?.getAttribute('data-file-id');
                const version = row.querySelector('.version-button')?.getAttribute('data-version');
                handleActionClick(selectedAction, { FileId: fileId, LatestVersion: version });
            };

            // Меняем текст кнопки и выбранное действие при выборе пункта меню
            items.forEach(item => {
                item.onclick = function (e) {
                    selectedAction = item.dataset.action;
                    button.textContent = item.textContent;
                    button.setAttribute('data-action', selectedAction);
                    menu.style.display = 'none';
                };
            });

            // Клик вне меню — закрыть меню
            document.addEventListener('click', function (e) {
                if (!dropdown.contains(e.target)) menu.style.display = 'none';
            });
        });
    }

    fetchFiles();
}

