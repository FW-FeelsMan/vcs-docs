// storage-table.js — production cleaned (no debug), stable menus, share integration.

(function () {
    'use strict';

    const API_USER_FILES = '/api/Upload/user-files';

    const MENU_IDS = {
        version: 'version-dropdown-menu',
        action: 'action-dropdown-menu'
    };

    window.initStorageTable = function initStorageTable() {
        const tableBody = document.querySelector('#userFilesTable tbody');
        const counter = document.getElementById('storageCounter');
        if (!tableBody) return;

        const state = {
            actionMenuCloseHandler: null,
            versionMenuCloseHandler: null
        };

        const ensureIsoWithZone = (s) => {
            const raw = String(s || '');
            return /Z$|[+\-]\d{2}:?\d{2}$/.test(raw) ? raw : (raw + 'Z');
        };

        const formatSize = (bytes) => {
            const val = Number(bytes || 0);
            return (val / 1024 / 1024).toFixed(2) + ' МБ';
        };

        const formatDate = (dateStr) => {
            const d = new Date(ensureIsoWithZone(dateStr));
            return d.toLocaleString('ru-RU', { timeZone: 'Europe/Moscow', hour12: false });
        };

        const escapeHtml = (str) => {
            const div = document.createElement('div');
            div.textContent = String(str ?? '');
            return div.innerHTML;
        };

        const wrapCell = (text) => {
            const esc = escapeHtml(String(text ?? ''));
            return `<div class="cell-content" title="${esc}">${esc}</div>`;
        };

        const normalizeFile = (file) => {
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
        };

        const removeMenu = (menuId, handlerKey) => {
            document.getElementById(menuId)?.remove();

            const handler = state[handlerKey];
            if (handler) {
                document.removeEventListener('click', handler);
                state[handlerKey] = null;
            }
        };

        async function fetchFiles() {
            try {
                const res = await fetch(API_USER_FILES, { cache: 'no-store' });
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
                console.error('storage-table.js: fetchFiles failed', err);
                if (counter) counter.textContent = 'Ошибка загрузки';
            }
        }

        function renderTable(files) {
            tableBody.innerHTML = '';

            files.forEach(file => {
                const row = document.createElement('tr');

                row.dataset.fileGroupId = String(file.FileGroupId ?? '');
                row.dataset.fileId = String(file.FileId ?? '');
                row.dataset.fileName = String(file.FileName ?? '');
                row.dataset.currentVersion = String(file.LatestVersion ?? 1);

                try {
                    row.dataset.versions = JSON.stringify(file.Versions || []);
                } catch {
                    row.dataset.versions = '[]';
                }

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

                const versions = safeParseJson(row.dataset.versions, []);
                updateRowByVersion(row, Number(row.dataset.currentVersion), versions);
            });

            setupAllVersionDropdowns();
            setupAllActionDropdowns();

            if (typeof window.reapplyStorageSort === 'function') window.reapplyStorageSort();
            if (typeof window.applyStorageColumnWidth === 'function') window.applyStorageColumnWidth();
        }

        const safeParseJson = (s, fallback) => {
            try { return JSON.parse(s || ''); } catch { return fallback; }
        };

        function renderVersionDropdown(file) {
            const v = Number(file.LatestVersion ?? 1);
            return `
<div class="multi-button compact version-multibutton" data-current-version="${v}">
  <button class="button-sliding primary compact version-button" data-version="${v}" data-click="throttle:800">
    v${v}
  </button>
  <div class="dropdown-arrow compact">&#9662;</div>
</div>`;
        }

        function renderActions() {
            return `
<div class="multi-button compact version-multibutton action-multibutton" data-current-action="download">
  <button class="button-sliding primary compact action-button" data-action="download" data-click="throttle:800">
    Скачать
  </button>
  <div class="dropdown-arrow compact">&#9662;</div>
</div>`;
        }

        function updateRowByVersion(row, selectedVersion, versions) {
            row.dataset.currentVersion = String(selectedVersion);

            const versionBtn = row.querySelector('.version-button');
            if (versionBtn) {
                versionBtn.dataset.version = String(selectedVersion);
                versionBtn.textContent = 'v' + selectedVersion;
            }

            const sizeCell = row.querySelector('.size-cell .cell-content') || row.querySelector('.size-cell');
            const dateCell = row.querySelector('.date-cell .cell-content') || row.querySelector('.date-cell');

            const meta = (versions || []).find(v => Number(v.Version ?? v.version) === Number(selectedVersion));
            if (!meta) return;

            if (sizeCell) sizeCell.textContent = formatSize(meta.FileSize ?? meta.fileSize ?? 0);
            if (dateCell) dateCell.textContent = formatDate(meta.UploadedAt ?? meta.uploadedAt);
        }

        function setupAllVersionDropdowns() {
            removeMenu(MENU_IDS.version, 'versionMenuCloseHandler');

            document.querySelectorAll('.version-multibutton').forEach(wrapper => {
                const arrow = wrapper.querySelector('.dropdown-arrow');
                const button = wrapper.querySelector('.version-button');
                const row = wrapper.closest('tr');
                if (!row || !arrow || !button) return;

                const versions = safeParseJson(row.dataset.versions, []);
                let currentVersion = Number(wrapper.dataset.currentVersion);

                arrow.onclick = (e) => {
                    e.stopPropagation();
                    removeMenu(MENU_IDS.version, 'versionMenuCloseHandler');

                    const menu = document.createElement('div');
                    menu.id = MENU_IDS.version;
                    menu.className = 'compact';

                    const rect = wrapper.getBoundingClientRect();
                    menu.style.position = 'absolute';
                    menu.style.left = rect.left + 'px';
                    menu.style.top = rect.bottom + 'px';
                    menu.style.width = rect.width + 'px';
                    menu.style.zIndex = '9999';

                    versions
                        .slice()
                        .sort((a, b) => Number(b.Version ?? b.version) - Number(a.Version ?? a.version))
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
                                removeMenu(MENU_IDS.version, 'versionMenuCloseHandler');
                            };

                            menu.appendChild(item);
                        });

                    document.body.appendChild(menu);

                    state.versionMenuCloseHandler = function closeOnOutside(ev) {
                        if (!menu.contains(ev.target)) {
                            menu.remove();
                            removeMenu(MENU_IDS.version, 'versionMenuCloseHandler');
                        }
                    };

                    setTimeout(() => document.addEventListener('click', state.versionMenuCloseHandler), 0);
                };
            });
        }

        function setupAllActionDropdowns() {
            removeMenu(MENU_IDS.action, 'actionMenuCloseHandler');

            document.querySelectorAll('.action-multibutton').forEach(wrapper => {
                const btn = wrapper.querySelector('.action-button');
                const arrow = wrapper.querySelector('.dropdown-arrow');
                const row = wrapper.closest('tr');
                if (!btn || !arrow || !row) return;

                let selectedAction = btn.dataset.action || 'download';

                btn.onclick = () => {
                    const fileGroupId = row.dataset.fileGroupId;
                    const version = row.querySelector('.version-button')?.dataset.version || row.dataset.currentVersion;

                    if (selectedAction === 'share' && typeof window.openShareLinkModalFromRow === 'function') {
                        window.openShareLinkModalFromRow(row);
                        return;
                    }

                    handleActionClick(selectedAction, fileGroupId, version, row);
                };

                arrow.onclick = (e) => {
                    e.stopPropagation();
                    removeMenu(MENU_IDS.action, 'actionMenuCloseHandler');

                    const menu = document.createElement('div');
                    menu.id = MENU_IDS.action;
                    menu.className = 'action-dropdown-menu compact';

                    const rect = wrapper.getBoundingClientRect();
                    menu.style.position = 'fixed';
                    menu.style.left = rect.left + 'px';
                    menu.style.top = rect.bottom + 'px';
                    menu.style.width = rect.width + 'px';
                    menu.style.zIndex = '9999999';

                    const actions = [
                        { key: 'download', text: 'Скачать' },
                        { key: 'view', text: 'Просмотр' },
                        { key: 'share', text: 'Поделиться' },
                        { key: 'delete', text: 'Удалить' }
                    ];

                    actions.forEach(a => {
                        const item = document.createElement('div');
                        item.className = 'dropdown-item';
                        item.textContent = a.text;
                        item.dataset.action = a.key;

                        if (a.key === selectedAction) {
                            item.style.background = '#e5f1fb';
                            item.style.fontWeight = 'bold';
                        }

                        item.onmousedown = (ev) => {
                            ev.preventDefault();
                            ev.stopPropagation();
                        };

                        item.onclick = (ev) => {
                            ev.preventDefault();
                            ev.stopPropagation();

                            selectedAction = a.key;
                            btn.dataset.action = selectedAction;
                            btn.textContent = a.text;

                            menu.remove();
                            removeMenu(MENU_IDS.action, 'actionMenuCloseHandler');
                        };

                        menu.appendChild(item);
                    });

                    document.body.appendChild(menu);

                    state.actionMenuCloseHandler = function closeOnOutside(ev) {
                        if (!menu.contains(ev.target)) {
                            menu.remove();
                            removeMenu(MENU_IDS.action, 'actionMenuCloseHandler');
                        }
                    };

                    setTimeout(() => document.addEventListener('click', state.actionMenuCloseHandler), 0);
                };
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
                    if (typeof window.openShareLinkModalFromRow === 'function' && row) {
                        window.openShareLinkModalFromRow(row);
                        return;
                    }
                    shareFallback(fileGroupId, v, downloadUrl);
                    break;

                case 'delete':
                    deleteFile(fileGroupId, v);
                    break;
            }
        }

        function shareFallback(fileGroupId, v, downloadUrl) {
            const fd = new FormData();
            fd.append('fileGroupId', fileGroupId);
            fd.append('version', String(v));

            fetch('/api/upload/share-link', { method: 'POST', body: fd })
                .then(r => (r.ok ? r.json() : Promise.reject(new Error('share-link failed'))))
                .then(data => {
                    const url = data?.url;
                    if (!url) throw new Error('no url');
                    return navigator.clipboard.writeText(url)
                        .then(() => alert('Публичная ссылка скопирована в буфер'))
                        .catch(() => { prompt('Публичная ссылка (скопируйте вручную):', url); });
                })
                .catch(err => {
                    console.error('storage-table.js: shareFallback failed', err);
                    const ownLink = window.location.origin + downloadUrl;
                    navigator.clipboard.writeText(ownLink)
                        .then(() => alert('Скопирована ссылка для владельца (требуется авторизация)'))
                        .catch(() => { prompt('Ссылка для владельца:', ownLink); });
                });
        }

        function deleteFile(fileGroupId, v) {
            if (!confirm(`Удалить ${fileGroupId} v${v}?`)) return;

            fetch(`/api/upload/delete/${fileGroupId}/${v}`, { method: 'DELETE' })
                .then(r => {
                    if (!r.ok) throw new Error('Ошибка удаления');
                    return r.json();
                })
                .then(data => {
                    if (data?.status === 'deleted') fetchFiles();
                    else alert('Не удалось удалить файл');
                })
                .catch(err => {
                    console.error('storage-table.js: delete failed', err);
                    alert('Не удалось удалить файл');
                });
        }

        window.fetchFiles = fetchFiles;
        fetchFiles();
    };
})();

(function autoInit() {
    const run = () => (window.initStorageTable && window.initStorageTable());
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', run);
    else run();
})();

/* share ext hard-fix (keep at the end) */
(function () {
    'use strict';

    function getExt(name) {
        if (!name) return '';
        const base = String(name).split(/[\\/]/).pop();
        const dot = base.lastIndexOf('.');
        if (dot <= 0 || dot >= base.length - 1) return '';
        let ext = base.slice(dot + 1).trim();
        ext = ext.replace(/[^a-zA-Z0-9]+/g, '');
        return ext ? ext.toUpperCase().slice(0, 10) : '';
    }

    function applyExt(row) {
        const modal = document.getElementById('shareModal');
        if (!modal) return;

        const extEl = modal.querySelector('[data-share-file-ext]');
        if (!extEl) return;

        const fileName =
            row?.dataset?.fileName ||
            modal.querySelector('[data-share-file-name]')?.textContent ||
            '';

        const ext = getExt(fileName) || 'FILE';
        extEl.textContent = ext;
        extEl.title = `Формат: ${ext}`;
    }

    const prev = window.openShareLinkModalFromRow;

    window.openShareLinkModalFromRow = function (row) {
        if (typeof prev === 'function') prev(row);
        applyExt(row);
    };
})();
