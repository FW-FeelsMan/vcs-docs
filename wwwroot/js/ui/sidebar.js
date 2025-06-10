//sidebar.js
const contentCache = new Map();
let currentContentId = null;

document.addEventListener("DOMContentLoaded", function () {
    const firstButton = document.querySelector('.sidebar-button');
    const initialContentId = firstButton?.getAttribute('data-content');

    document.querySelectorAll('.sidebar-button').forEach(button => {
        button.addEventListener('click', function () {
            window.selectButton(this);
        });
    });

    if (firstButton) window.selectButton(firstButton);
});

window.selectButton = function (button) {
    const contentId = button.getAttribute('data-content');
    const styleId = button.getAttribute('data-style');

    if (currentContentId === contentId) return;

    if (currentContentId === 'extra_page') {
        const contentElement = document.querySelector(`[data-cached-content="extra_page"]`);
        if (contentElement) {
            contentCache.set('extra_page', {
                html: contentElement.innerHTML,
                state: getPageState('extra_page')
            });
        }
    }

    loadStyles(styleId);
    showLoader();

    if (contentId === 'extra_page' && contentCache.has('extra_page')) {
        showCachedContent(contentId);
        hideLoader();
    } else {
        loadContent(contentId);
    }

    currentContentId = contentId;
    updateButtonSelection(button);
};

function updateButtonSelection(button) {
    document.querySelectorAll('.sidebar-button').forEach(btn => {
        btn.classList.remove('selected');
    });
    button.classList.add('selected');
}

async function loadContent(contentId) {
    const contentContainer = document.getElementById('content');
    if (!contentContainer) {
        console.error('Контейнер контента не найден');
        return;
    }

    try {
        contentContainer.innerHTML = '';

        const url = contentId === 'profile_page'
            ? `/Content/${contentId}?ts=${Date.now()}`
            : `/Content/${contentId}`;

        const response = await fetch(url);
        if (!response.ok) throw new Error(`HTTP error! ${response.status}`);
        const html = await response.text();

        contentContainer.innerHTML = html;
        if (contentId === 'profile_page') {
            await loadProfileScripts();
            if (typeof window.initUploadFile === "function") {
                window.initUploadFile();
            }
        }
    } catch (error) {
        console.error('Ошибка загрузки:', error);
        contentContainer.innerHTML = `<div class="error-message">Ошибка загрузки</div>`;
    } finally {
        hideLoader();
    }
}

    async function loadProfileScripts() {
        const scripts = [
            "/js/profile/profile.js",
            "/js/profile/profile-edit-info.js",
            "/js/profile/storage/upload-file.js",
            "/js/profile/storage/storage-table.js",
            "/js/profile/storage/upload-conflict-modal.js",
            "/js/profile/storage/sorttable.js",
            "/js/profile/taskManager/taskManager.js"
        ];

    const promises = scripts.map(src => {
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = src;
            script.defer = true;
            script.onload = resolve;
            script.onerror = () => reject(new Error(`Ошибка загрузки: ${src}`));
            document.body.appendChild(script);
        });
    });

    await Promise.all(promises);

    initAvatarUpload();
    if (typeof window.initUserStorage === "function") {
        window.initUserStorage();
    }

    if (window.taskManager) {
        try {
            const response = await fetch("/api/tasks/active");
            if (!response.ok) throw new Error(`Ошибка ответа: ${response.status}`);
            const tasks = await response.json();

            if (Array.isArray(tasks)) {
                tasks.forEach(task => {
                    window.taskManager.addTask(task);
                });
            }
        } catch (err) {
            console.error("Ошибка при получении задач с сервера:", err);
        }
    }
}
function showCachedContent(contentId) {
    const contentContainer = document.getElementById('content');
    const cachedData = contentCache.get(contentId);

    if (!cachedData) {
        console.error('Нет данных в кеше для:', contentId);
        return;
    }

    contentContainer.innerHTML = cachedData.html;

    if (contentId === 'extra_page') {
        initExtraPage();
        if (cachedData.state?.model) {
            restoreModel(cachedData.state.model);
        }
    }
}

function restoreModel(modelData) {
    const viewer = document.getElementById('model-viewer');
    if (!viewer) {
        console.error('Контейнер для модели не найден');
        return;
    }

    try {
        viewer.innerHTML = `<iframe src="/ifcjs/index.html?model=${encodeURIComponent(modelData)}"></iframe>`;
    } catch (error) {
        console.error('Ошибка восстановления модели:', error);
    }
}

function getPageState(contentId) {
    if (contentId === 'extra_page') {
        return { model: window.uploadedModel };
    }
    return null;
}

function restorePageState(contentId, container) {
    const state = contentCache.get(contentId).state;
    if (!state) return;

    if (contentId === 'extra_page' && state.model) {
        window.uploadedModel = state.model;
        restoreModel(container);
    }
}

function loadStyles(styleId) {
    document.querySelectorAll("link[rel=stylesheet][id]").forEach(link => {
        link.disabled = link.id !== styleId;
    });
}

function showLoader() {
    const loader = document.getElementById('loader');
    if (loader) loader.classList.remove('hidden');
}

function hideLoader() {
    const loader = document.getElementById('loader');
    if (loader) loader.classList.add('hidden');
}

function initExtraPage() {
    setTimeout(() => {
        const uploader = document.querySelector('#extra_page input[type="file"]');
        if (!uploader) {
            console.error('Input не найден. Проверьте:');
            console.log('Доступный HTML:', document.getElementById('extra_page')?.innerHTML);
            return;
        }

        uploader.addEventListener('change', handleFileUpload);
        console.log('Инициализация extra_page успешна');
    }, 50);
}

function handleFileUpload(e) {
    const file = e.target.files[0];
    if (!file) return;

    window.uploadedModel = processFile(file);
    console.log('Модель загружена:', window.uploadedModel);
}
