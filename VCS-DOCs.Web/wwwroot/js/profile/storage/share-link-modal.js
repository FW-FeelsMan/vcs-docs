// wwwroot/js/profile/storage/share-link-modal.js
(function () {
    'use strict';

    let currentFileData = {
        fileGroupId: null,
        version: null,
        fileName: null
    };

    function getModal() {
        return document.getElementById('shareModal');
    }

    function getFileExtension(fileName) {
        if (!fileName) return '';

        const base = String(fileName).split(/[\\/]/).pop(); // если вдруг придёт путь
        const dot = base.lastIndexOf('.');
        if (dot <= 0 || dot >= base.length - 1) return '';

        let ext = base.substring(dot + 1).trim();
        ext = ext.replace(/[^a-zA-Z0-9]+/g, ''); // чистим мусор
        if (!ext) return '';

        if (ext.length > 10) ext = ext.slice(0, 10);
        return ext.toUpperCase();
    }

    function resetGeneratedLinkUi(modal) {
        const linkBox = modal.querySelector('#shareLinkBox');
        const linkInput = modal.querySelector('#shareLinkUrl');
        if (linkBox) linkBox.style.display = 'none';
        if (linkInput) linkInput.value = '';

        const hint = modal.querySelector('.share-link-hint');
        if (hint) hint.style.display = 'none';

        const genBtnLocal = modal.querySelector('#shareGenerateBtn');
        if (genBtnLocal) {
            genBtnLocal.disabled = false;
            genBtnLocal.innerHTML = `
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
                    <polyline points="22 4 12 14.01 9 11.01" />
                </svg>
                Создать ссылку`;
        }
    }

    function fillFileCard(modal) {
        const extLabel = modal.querySelector('[data-share-file-ext]');
        const nameLabel = modal.querySelector('[data-share-file-name]');
        const versionLabel = modal.querySelector('[data-share-file-version]');

        if (nameLabel) nameLabel.textContent = currentFileData.fileName || 'Файл';
        if (versionLabel) versionLabel.textContent = 'Версия ' + (currentFileData.version || '1');

        if (extLabel) {
            const ext = getFileExtension(currentFileData.fileName);
            extLabel.textContent = ext || 'FILE';
            extLabel.title = ext ? `Формат: ${ext}` : 'Формат: неизвестен';
        }
    }

    function setModalOpen(modal, isOpen) {
        if (isOpen) {
            modal.classList.add('active');
            modal.setAttribute('aria-hidden', 'false');
        } else {
            modal.classList.remove('active');
            modal.setAttribute('aria-hidden', 'true');
        }
    }

    // ✅ ВАЖНО: функция создаётся ВСЕГДА, независимо от наличия modal в DOM
    window.openShareLinkModalFromRow = function (row) {
        const modal = getModal();
        if (!modal || !row) return;

        currentFileData.fileGroupId = row.dataset.fileGroupId || null;
        currentFileData.version = row.dataset.currentVersion || row.querySelector('.version-button')?.dataset.version || '1';
        currentFileData.fileName = row.dataset.fileName || 'Файл';

        fillFileCard(modal);
        resetGeneratedLinkUi(modal);
        setModalOpen(modal, true);
    };

    // Ленивая инициализация обработчиков закрытия/копирования/генерации:
    // (чтобы не зависеть от того, когда modal появился)
    function ensureHandlers() {
        const modal = getModal();
        if (!modal || modal.__shareHandlersBound) return;
        modal.__shareHandlersBound = true;

        // закрытие
        modal.querySelectorAll('[data-share-close]').forEach(btn => {
            btn.onclick = () => setModalOpen(modal, false);
        });

        // генерация
        const genBtn = modal.querySelector('#shareGenerateBtn');
        if (genBtn) {
            genBtn.onclick = async function () {
                const ttl = modal.querySelector('[data-share-expire]')?.value || 24;
                const limit = modal.querySelector('[data-share-download-limit]')?.value || 'unlimited';
                const authOnly = modal.querySelector('[data-share-auth-only]')?.checked || false;

                genBtn.disabled = true;
                genBtn.textContent = 'Генерация...';

                try {
                    const fd = new FormData();
                    fd.append('fileGroupId', currentFileData.fileGroupId);
                    fd.append('version', currentFileData.version);
                    fd.append('ttlHours', ttl);
                    if (limit !== 'unlimited') fd.append('maxDownloads', limit);
                    fd.append('requireAuth', authOnly);

                    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                    const headers = {};
                    if (token) headers['RequestVerificationToken'] = token;

                    const response = await fetch('/api/Upload/share-db', {
                        method: 'POST',
                        body: fd,
                        headers
                    });

                    if (!response.ok) throw new Error('Ошибка сервера');

                    const data = await response.json();

                    if (data.url) {
                        const linkInput = modal.querySelector('#shareLinkUrl');
                        const linkBox = modal.querySelector('#shareLinkBox');

                        if (linkInput) linkInput.value = data.url;
                        if (linkBox) linkBox.style.display = 'block';

                        try { await navigator.clipboard.writeText(data.url); } catch { }

                        genBtn.textContent = 'Готово!';
                    } else {
                        resetGeneratedLinkUi(modal);
                    }
                } catch {
                    alert('Не удалось создать ссылку');
                    resetGeneratedLinkUi(modal);
                }
            };
        }

        // копирование
        const copyBtn = modal.querySelector('[data-share-copy]');
        if (copyBtn) {
            copyBtn.onclick = function () {
                const input = modal.querySelector('#shareLinkUrl');
                if (!input) return;

                const url = input.value;
                if (!url) return;

                navigator.clipboard.writeText(url).catch(() => { });

                const hint = modal.querySelector('.share-link-hint');
                if (hint) {
                    hint.style.display = 'flex';
                    setTimeout(() => { hint.style.display = 'none'; }, 2000);
                }
            };
        }
    }

    // попытка навесить хендлеры сразу + при появлении DOM
    ensureHandlers();
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', ensureHandlers);
    }

})();
