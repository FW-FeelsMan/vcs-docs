// storage-table.js — renders table & formats dates in MSK robustly
(function () {
    window.initStorageTable = function initStorageTable() {
        const tableBody = document.querySelector('#userFilesTable tbody');
        const counter = document.getElementById('storageCounter');
        if (!tableBody) return;

        function ensureIsoUtc(s) {
            // Normalizes various SQL/ISO formats to strict ISO8601 UTC.
            // Examples in: "2025-08-10 20:09:17.6131803", "2025-08-10T20:09:17Z", "2025-08-10T20:09:17.613Z"
            const raw = String(s || '').trim();
            if (!raw) return null;
            // If has explicit TZ, return as-is
            if (/Z$|[+\-]\d{2}:\d{2}$/.test(raw)) {
                // replace space with 'T' if needed
                return raw.replace(' ', 'T');
            }
            // No TZ part: treat it as UTC from server, append Z
            const withT = raw.includes('T') ? raw : raw.replace(' ', 'T');
            return withT + 'Z';
        }

        function formatSize(bytes) {
            const val = Number(bytes || 0);
            return (val / 1024 / 1024).toFixed(2) + ' МБ';
        }

        function formatDate(dateStr) {
            const iso = ensureIsoUtc(dateStr);
            if (!iso) return '';
            // Try native IANA tz formatting first
            try {
                const d = new Date(iso);
                if (isNaN(d.getTime())) throw new Error('bad date');
                return d.toLocaleString('ru-RU', { timeZone: 'Europe/Moscow', hour12: false });
            } catch {
                // Fallback: manual +03:00 shift
                try {
                    const d = new Date(iso);
                    if (isNaN(d.getTime())) return String(dateStr || '');
                    const mskOffsetMin = 180; // MSK fixed offset
                    const localOffsetMin = d.getTimezoneOffset() * -1; // minutes east of UTC
                    // Convert UTC -> MSK
                    const utcMs = d.getTime() + (0 - localOffsetMin) * 60000; // normalize to UTC
                    const mskMs = utcMs + mskOffsetMin * 60000;
                    const md = new Date(mskMs);
                    const pad = n => String(n).padStart(2, '0');
                    return `${pad(md.getDate())}.${pad(md.getMonth() + 1)}.${md.getFullYear()} ${pad(md.getHours())}:${pad(md.getMinutes())}:${pad(md.getSeconds())}`;
                } catch { return String(dateStr || ''); }
            }
        }

        function escapeHtml(str) {
            const div = document.createElement('div');
            div.textContent = String(str ?? '');
            return div.innerHTML;
        }

        function wrapCell(text) {
            const esc = escapeHtml(String(text ?? ''));
            return `<div class="cell-content" title="${esc}">${esc}</div>`;
        }

        function normalizeFile(file) {
            const normVersions = (file.Versions ?? file.versions ?? []).map(v => ({
                Version: v.Version ?? v.version,
                UploadedAt: v.UploadedAt ?? v.uploadedAt,
                FileSize: v.FileSize ?? v.fileSize ?? 0
            }));
            return {
                FileId: file.FileId ?? file.fileId,
                FileGroupId: file.FileGroupId ?? file.fileGroupId,
                FileName: file.FileName ?? file.fileName,
                FileSize: file.FileSize ?? file.fileSize ?? 0,
                UpdatedAt: file.UpdatedAt ?? file.updatedAt,
                LatestVersion: file.LatestVersion ?? file.latestVersion ?? 1,
                Versions: normVersions
            };
        }

        async function fetchFiles() {
            try {
                const res = await fetch('/api/Upload/user-files', { cache: 'no-store' });
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                const data = await res.json();

                const files = Array.isArray(data.files) ? data.files.map(normalizeFile) : [];
                renderTable(files);

                if (counter) {
                    const used = formatSize(data.usedBytes);
                    const temp = formatSize(data.tempBytes);
                    const limit = formatSize(data.limitBytes);
                    const free = formatSize((data.limitBytes - data.usedBytes - data.tempBytes));
                    counter.textContent = `Использовано: ${used} из ${limit} (временных: ${temp}); свободно: ${free}`;
                }
            } catch (err) {
                console.error('Не удалось загрузить список файлов', err);
                if (counter) counter.textContent = 'Ошибка загрузки';
            }
        }

        function renderTable(files) {
            tableBody.innerHTML = '';

            files.forEach(file => {
                const row = document.createElement('tr');
                row.dataset.fileGroupId = file.FileGroupId;
                row.dataset.fileId = file.FileId;
                row.dataset.fileName = file.FileName;
                row.dataset.currentVersion = file.LatestVersion;
                try { row.dataset.versions = JSON.stringify(file.Versions || []); } catch { row.dataset.versions = '[]'; }

                const fileNameCell = document.createElement('td');
                fileNameCell.innerHTML = wrapCell(file.FileName);

                const versionCell = document.createElement('td');
                versionCell.innerHTML = renderVersionDropdown(file);

                const sizeCell = document.createElement('td');
                sizeCell.className = 'size-cell';
                sizeCell.innerHTML = wrapCell(formatSize(file.FileSize));

                const dateCell = document.createElement('td');
                dateCell.className = 'date-cell';
                dateCell.innerHTML = wrapCell(formatDate(file.UpdatedAt));

                const actionsCell = document.createElement('td');
                actionsCell.innerHTML = renderActions();

                row.append(fileNameCell, versionCell, sizeCell, dateCell, actionsCell);
                tableBody.appendChild(row);

                const versions = JSON.parse(row.dataset.versions || '[]');
                updateRowByVersion(row, Number(row.dataset.currentVersion), versions);
            });

            setupAllVersionDropdowns();
            setupAllActionDropdowns();
            if (typeof window.reapplyStorageSort === 'function') window.reapplyStorageSort();
        }

        function renderVersionDropdown(file) {
            return `
        <div class="multi-button compact version-multibutton"
             data-current-version="${file.LatestVersion}">
          <button class="button-sliding primary compact version-button"
                  data-version="${file.LatestVersion}" data-click="throttle:800" >
            v${file.LatestVersion}
          </button>
          <div class="dropdown-arrow compact">&#9662;</div>
        </div>`;
        }

        function renderActions() {
            return `
        <div class="multi-button compact action-multibutton" style="position: relative;">
          <button class="button-sliding primary compact action-button" data-action="download" data-click="throttle:800" >
            Скачать
          </button>
          <div class="dropdown-arrow compact">&#9662;</div>
          <div class="action-dropdown-menu compact"
               style="display: none; position: absolute; min-width: 140px;">
            <div class="dropdown-item" data-action="download">Скачать</div>
            <div class="dropdown-item" data-action="view">Просмотр</div>
            <div class="dropdown-item" data-action="share">Поделиться</div>
            <div class="dropdown-item" data-action="delete">Удалить</div>
          </div>
        </div>`;
        }

        function updateRowByVersion(row, selectedVersion, versions) {
            row.dataset.currentVersion = selectedVersion;

            const versionBtn = row.querySelector('.version-button');
            if (versionBtn) {
                versionBtn.dataset.version = selectedVersion;
                versionBtn.textContent = 'v' + selectedVersion;
            }

            const sizeCell = row.querySelector('.size-cell .cell-content') || row.querySelector('.size-cell');
            const dateCell = row.querySelector('.date-cell .cell-content') || row.querySelector('.date-cell');

            const meta = (versions || []).find(v => Number(v.Version ?? v.version) === Number(selectedVersion));
            if (meta) {
                if (sizeCell) sizeCell.textContent = formatSize(meta.FileSize ?? meta.fileSize ?? 0);
                if (dateCell) dateCell.textContent = formatDate(meta.UploadedAt ?? meta.uploadedAt);
            }
        }

        function setupAllVersionDropdowns() {
            document.getElementById('version-dropdown-menu')?.remove();

            document.querySelectorAll('.version-multibutton').forEach(wrapper => {
                const arrow = wrapper.querySelector('.dropdown-arrow');
                const button = wrapper.querySelector('.version-button');
                const row = wrapper.closest('tr');
                if (!row || !arrow || !button) return;

                const versions = JSON.parse(row.dataset.versions || '[]');
                let currentVersion = Number(wrapper.dataset.currentVersion);

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
                    menu.style.zIndex = '9999';

                    versions.slice().sort((a, b) => Number(b.Version ?? b.version) - Number(a.Version ?? a.version))
                        .forEach(v => {
                            const ver = Number(v.Version ?? v.version);
                            const item = document.createElement('div');
                            item.className = 'dropdown-item';
                            item.textContent = 'v' + ver;
                            if (ver === currentVersion) {
                                item.style.background = '#e5f1fb';
                                item.style.fontWeight = 'bold';
                            }
                            item.onclick = () => {
                                button.textContent = 'v' + ver;
                                wrapper.dataset.currentVersion = String(ver);
                                currentVersion = ver;
                                updateRowByVersion(row, ver, versions);
                                menu.remove();
                            };
                            menu.appendChild(item);
                        });

                    document.body.appendChild(menu);
                    setTimeout(() => {
                        function closeOnOutside(e2) {
                            if (!menu.contains(e2.target)) {
                                menu.remove();
                                document.removeEventListener('click', closeOnOutside);
                            }
                        }
                        document.addEventListener('click', closeOnOutside);
                    }, 0);
                };
            });
        }

        function setupAllActionDropdowns() {
            document.querySelectorAll('.action-multibutton').forEach(dropdown => {
                const btn = dropdown.querySelector('.action-button');
                const arrow = dropdown.querySelector('.dropdown-arrow');
                const menu = dropdown.querySelector('.action-dropdown-menu');
                const items = menu ? menu.querySelectorAll('.dropdown-item') : [];
                let selectedAction = btn?.dataset.action || 'download';
                if (!btn || !arrow || !menu) return;

                arrow.onclick = e => {
                    e.stopPropagation();
                    document.querySelectorAll('.action-dropdown-menu')
                        .forEach(m => m !== menu && (m.style.display = 'none'));
                    menu.style.display = menu.style.display === 'block' ? 'none' : 'block';
                };

                btn.onclick = () => {
                    const row = dropdown.closest('tr');
                    if (!row) return;
                    const fileGroupId = row.dataset.fileGroupId;
                    const version = row.querySelector('.version-button')?.dataset.version || row.dataset.currentVersion;

                    if (selectedAction === 'share') {
                        if (window.openShareLinkModalFromRow) {
                            window.openShareLinkModalFromRow(row);
                            return;
                        }
                    }
                    handleActionClick(selectedAction, fileGroupId, version, row);
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

        function handleActionClick(action, fileGroupId, version, row) {
            const v = Number(version);
            const downloadUrl = `/api/upload/download/${fileGroupId}/${v}`;

            switch (action) {
                case 'download':
                    window.location.href = downloadUrl;
                    break;
                case 'view':
                    window.open(downloadUrl, '_blank');
                    break;
                case 'share':
                    if (window.openShareLinkModalFromRow && row) {
                        window.openShareLinkModalFromRow(row);
                        return;
                    }
                    const fd = new FormData();
                    fd.append('fileGroupId', fileGroupId);
                    fd.append('version', String(v));
                    fetch('/api/upload/share-link', { method: 'POST', body: fd })
                        .then(r => r.ok ? r.json() : Promise.reject(new Error('share-link failed')))
                        .then(data => {
                            const url = data && data.url;
                            if (!url) throw new Error('no url');
                            return navigator.clipboard.writeText(url)
                                .then(() => alert('Публичная ссылка скопирована в буфер'))
                                .catch(() => { prompt('Публичная ссылка (скопируйте вручную):', url); });
                        })
                        .catch(err => {
                            console.error('share error', err);
                            const ownLink = window.location.origin + downloadUrl;
                            navigator.clipboard.writeText(ownLink)
                                .then(() => alert('Скопирована ссылка для владельца (требуется авторизация)'))
                                .catch(() => { prompt('Ссылка для владельца:', ownLink); });
                        });
                    break;
                case 'delete':
                    if (!confirm(`Удалить ${fileGroupId} v${v}?`)) return;
                    fetch(`/api/upload/delete/${fileGroupId}/${v}`, { method: 'DELETE' })
                        .then(r => {
                            if (!r.ok) throw new Error('Ошибка удаления');
                            return r.json();
                        })
                        .then(data => {
                            if (data.status === 'deleted') {
                                fetchFiles();
                            } else {
                                alert('Не удалось удалить файл');
                            }
                        })
                        .catch(err => {
                            console.error('Ошибка при удалении файла:', err);
                            alert('Не удалось удалить файл');
                        });
                    break;
            }
        }

        window.fetchFiles = fetchFiles;
        fetchFiles();
    };
})();

// Optional: sorting helper kept separate. If you already include your own, you may remove this.
window.initStorageSorting = window.initStorageSorting || function () {
    if (window.__storageSortingInitialized) return;
    window.__storageSortingInitialized = true;
    const table = document.getElementById("userFilesTable");
    if (!table) return;
    const tbody = table.querySelector("tbody");
    const headers = table.querySelectorAll("th");

    let currentSort = {
        index: 0,
        ascending: true,
        type: headers[0]?.dataset.type || 'string'
    };

    headers.forEach((header, idx) => {
        const type = header.dataset.type;
        if (!type) return;

        header.style.cursor = "pointer";
        header.addEventListener("click", () => {
            if (currentSort.index === idx) {
                currentSort.ascending = !currentSort.ascending;
            } else {
                currentSort = { index: idx, ascending: true, type: type };
            }
            applySorting();
        });
    });

    function parseCustomDate(dateStr) {
        // expects "dd.MM.yyyy HH:mm:ss"
        const parts = (dateStr || '').split(/[.\s:]/);
        if (parts.length < 3) return new Date(0);
        const [day, month, year] = parts;
        return new Date(parseInt(year, 10), parseInt(month, 10) - 1, parseInt(day, 10));
    }

    function applySorting() {
        const rows = Array.from(tbody.querySelectorAll("tr"));

        rows.sort((a, b) => {
            let x = a.children[currentSort.index]?.textContent.trim() || "";
            let y = b.children[currentSort.index]?.textContent.trim() || "";

            if (currentSort.type === "number") {
                x = parseFloat(x.replace(",", ".")) || 0;
                y = parseFloat(y.replace(",", ".")) || 0;
            } else if (currentSort.type === "date") {
                x = parseCustomDate(x);
                y = parseCustomDate(y);
            } else {
                x = x.toLowerCase();
                y = y.toLowerCase();
            }

            if (x === y) return 0;
            return currentSort.ascending
                ? (x > y ? -1 : 1)
                : (x < y ? -1 : 1);
        });

        headers.forEach(h => h.classList.remove("asc", "desc"));
        headers[currentSort.index].classList.add(currentSort.ascending ? "asc" : "desc");

        rows.forEach(row => tbody.appendChild(row));
    }

    const initialIndex = Array.from(headers).findIndex(h => h.dataset.type === "date");
    if (initialIndex !== -1) {
        currentSort.index = initialIndex;
        currentSort.type = headers[initialIndex].dataset.type;
        currentSort.ascending = true;
    }

    applySorting();

    window.reapplyStorageSort = function () {
        applySorting();
    };
};
