// sidebar.js
const contentCache = new Map();
let currentContentId = null;
let clickLock = false;

let cleanupSupportPrefill = null;

document.addEventListener('DOMContentLoaded', () => {
    const firstButton = document.querySelector('.sidebar-button');

    document.querySelectorAll('.sidebar-button').forEach(button => {
        button.addEventListener('click', () => window.selectButton(button));
    });

    if (firstButton) window.selectButton(firstButton);
});

window.selectButton = function (button) {
    if (clickLock) return;
    clickLock = true;
    setTimeout(() => (clickLock = false), 300);

    const contentId = button.getAttribute('data-content');
    const styleId = button.getAttribute('data-style');
    if (currentContentId === contentId) return;

    if (cleanupSupportPrefill) {
        try { cleanupSupportPrefill(); } catch { }
        cleanupSupportPrefill = null;
    }

    if (currentContentId === 'extra_page') {
        const contentElement = document.querySelector('[data-cached-content="extra_page"]');
        if (contentElement) {
            contentCache.set('extra_page', {
                html: contentElement.innerHTML,
                state: getPageState('extra_page'),
            });
        }
    }

    loadStyles(styleId);
    showLoader();

    if (contentId === 'extra_page' && contentCache.has('extra_page')) {
        showCachedContent(contentId);
    } else {
        loadContent(contentId);
    }

    currentContentId = contentId;
    updateButtonSelection(button);
};

function updateButtonSelection(button) {
    document.querySelectorAll('.sidebar-button').forEach(btn => btn.classList.remove('selected'));
    button.classList.add('selected');
}

function setupSupportPrefill(iframe, startAnim) {
    let disposed = false;
    let sentOnce = false;
    let fallbackId = null;

    const getTargetOrigin = () => {
        try { return new URL(iframe.src, location.href).origin; }
        catch { return null; }
    };

    const postPrefill = () => {
        if (disposed || sentOnce) return;

        const targetOrigin = getTargetOrigin();
        if (!targetOrigin) return;

        const cu = window.currentUser || {};
        const payload = {
            type: 'vdocs.prefill',
            lock: true,
            fullName: cu.fullName || '',
            login: cu.login || '',
            email: cu.email || ''
        };

        try {
            iframe.contentWindow?.postMessage(payload, targetOrigin);
            sentOnce = true;
        } catch {
            /* ignore */
        }
    };

    const onMsg = (e) => {
        const targetOrigin = getTargetOrigin();
        if (!targetOrigin) return;
        if (e.origin !== targetOrigin) return;
        if (e.data?.type !== 'support.ready') return;
        postPrefill();
    };

    const onLoad = () => {
        if (disposed) return;
        startAnim();
        postPrefill();
        if (fallbackId) { clearTimeout(fallbackId); fallbackId = null; }
    };

    window.addEventListener('message', onMsg);
    iframe.addEventListener('load', onLoad, { once: true });

    fallbackId = setTimeout(() => {
        if (disposed) return;
        startAnim();
        postPrefill();
    }, 3000);

    return () => {
        disposed = true;
        window.removeEventListener('message', onMsg);
        try { iframe.removeEventListener('load', onLoad); } catch { }
        if (fallbackId) { clearTimeout(fallbackId); fallbackId = null; }
    };
}

async function loadContent(contentId) {
    const contentContainer = document.getElementById('content');
    if (!contentContainer) return;

    try {
        contentContainer.innerHTML = '';

        const url = contentId === 'profile_page'
            ? `/Content/${contentId}?ts=${Date.now()}`
            : `/Content/${contentId}`;

        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const html = await response.text();

        const panel = document.createElement('div');
        panel.className = 'view-panel view-pre';
        panel.innerHTML = html;
        contentContainer.replaceChildren(panel);

        const startAnim = (() => {
            let started = false;
            return () => {
                if (started) return;
                started = true;

                panel.getBoundingClientRect();

                panel.classList.add('view-enter');
                panel.addEventListener('animationend', () => {
                    panel.classList.remove('view-enter', 'view-pre');
                    hideLoader();
                }, { once: true });
            };
        })();

        const iframe = panel.querySelector('iframe');
        if (iframe) {
            cleanupSupportPrefill = setupSupportPrefill(iframe, startAnim);
        } else {
            startAnim();
        }

        if (contentId === 'profile_page') {
            await loadProfileScripts();
            if (typeof window.initUploadFile === 'function') window.initUploadFile();
        }
    } catch (error) {
        console.error('Ошибка загрузки:', error);
        contentContainer.innerHTML = `<div class="error-message">Ошибка загрузки</div>`;
        hideLoader();
    }
}

async function loadProfileScripts() {
    const scripts = [
        '/js/profile/profile.js',
        '/js/profile/profile-edit-info.js?v=20260112a',
        '/js/profile/profile-delete-account.js',
        '/js/profile/storage/storage-sortable.js',
        '/js/profile/storage/upload-file.js?v=20250926a',
        '/js/profile/storage/upload-conflict-modal.js',
        '/js/profile/storage/storage-table.js',
        '/js/profile/storage/share-link-modal.js',
    ];

    const loaders = scripts.map(src => new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = src;
        s.defer = true;
        s.onload = resolve;
        s.onerror = () => reject(new Error(`Ошибка загрузки: ${src}`));
        document.body.appendChild(s);
    }));

    await Promise.all(loaders);

    if (typeof window.initAvatarUpload === 'function') initAvatarUpload();
    if (typeof window.initUserStorage === 'function') window.initUserStorage();

    if (window.taskManager) {
        try {
            const response = await fetch('/api/tasks/active', { credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const tasks = await response.json();

            if (Array.isArray(tasks)) {
                tasks.forEach(task => window.taskManager.addTask(task));

                const observer = new MutationObserver((_, obs) => {
                    const list = document.querySelector('#tasks .tasks-grid#taskCardList');
                    if (list) {
                        obs.disconnect();
                        taskManager.render();
                    }
                });

                observer.observe(document.getElementById('content'), { childList: true, subtree: true });
                observer.observe(document.body, { childList: true, subtree: true });
            }
        } catch (err) {
            console.error('Ошибка при получении задач с сервера:', err);
        }
    }
}

function showCachedContent(contentId) {
    const contentContainer = document.getElementById('content');
    const cachedData = contentCache.get(contentId);

    if (!cachedData || !contentContainer) {
        hideLoader();
        return;
    }

    const panel = document.createElement('div');
    panel.className = 'view-panel view-pre';
    panel.innerHTML = cachedData.html;
    contentContainer.replaceChildren(panel);

    panel.getBoundingClientRect();
    panel.classList.add('view-enter');
    panel.addEventListener('animationend', () => {
        panel.classList.remove('view-enter', 'view-pre');
        hideLoader();
    }, { once: true });

    if (contentId === 'extra_page') {
        initExtraPage();
        if (cachedData.state?.model) restoreModel(cachedData.state.model);
    }
}

function restoreModel(modelData) {
    const viewer = document.getElementById('model-viewer');
    if (!viewer) return;

    try {
        viewer.innerHTML = `<iframe src="/ifcjs/index.html?model=${encodeURIComponent(modelData)}"></iframe>`;
    } catch (error) {
        console.error('Ошибка восстановления модели:', error);
    }
}

function getPageState(contentId) {
    if (contentId === 'extra_page') return { model: window.uploadedModel };
    return null;
}

function loadStyles(styleId) {
    document.querySelectorAll('link[rel=stylesheet][id]').forEach(link => {
        if (link.dataset.persistent === 'true') return;
        link.disabled = link.id !== styleId;
    });
}

function showLoader() {
    document.getElementById('loader')?.classList.remove('hidden');
}

function hideLoader() {
    document.getElementById('loader')?.classList.add('hidden');
}

function initExtraPage() {
    setTimeout(() => {
        const uploader = document.querySelector('#extra_page input[type="file"]');
        if (!uploader) return;

        uploader.addEventListener('change', handleFileUpload);
    }, 50);
}

function handleFileUpload(e) {
    const file = e.target.files[0];
    if (!file) return;
    window.uploadedModel = processFile(file);
}