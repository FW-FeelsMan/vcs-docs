// sidebar.js
// --- состояние ---
const contentCache = new Map();
let currentContentId = null;
let clickLock = false;

// --- инициализация ---
document.addEventListener('DOMContentLoaded', () => {
    const firstButton = document.querySelector('.sidebar-button');

    document.querySelectorAll('.sidebar-button').forEach(button => {
        button.addEventListener('click', () => window.selectButton(button));
    });

    if (firstButton) window.selectButton(firstButton);
});

// --- выбор пункта сайдбара ---
window.selectButton = function (button) {
    // анти-дребезг кликов
    if (clickLock) return;
    clickLock = true;
    setTimeout(() => (clickLock = false), 300);

    const contentId = button.getAttribute('data-content');
    const styleId = button.getAttribute('data-style');

    if (currentContentId === contentId) return;

    // сохранить состояние extra_page перед уходом
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

// --- визуальный выбор активной кнопки ---
function updateButtonSelection(button) {
    document.querySelectorAll('.sidebar-button').forEach(btn => btn.classList.remove('selected'));
    button.classList.add('selected');
}

// --- загрузка контента в центральную область ---
async function loadContent(contentId) {
    const contentContainer = document.getElementById('content');
    if (!contentContainer) return;

    try {
        // очищаем поле
        contentContainer.innerHTML = '';

        // формируем URL (для профиля отключаем кэш)
        const url = contentId === 'profile_page'
            ? `/Content/${contentId}?ts=${Date.now()}`
            : `/Content/${contentId}`;

        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok) throw new Error(`HTTP error! ${response.status}`);
        const html = await response.text();

        // создаём панель СРАЗУ в пред-анимационном состоянии (никакого FOUC)
        const panel = document.createElement('div');
        panel.className = 'view-panel view-pre';
        panel.innerHTML = html;
        contentContainer.replaceChildren(panel);

        // запуск анимации — гарантированно один раз
        const startAnim = (() => {
            let started = false;
            return () => {
                if (started) return;
                started = true;

                // reflow, чтобы браузер «увидел» предсостояние перед переключением
                panel.getBoundingClientRect();

                panel.classList.add('view-enter');
                panel.addEventListener('animationend', () => {
                    panel.classList.remove('view-enter', 'view-pre');
                    hideLoader();
                }, { once: true });
            };
        })();

        // если внутри есть iframe (Обратная связь) — ждём его загрузку
        const iframe = panel.querySelector('iframe');
        let fallbackId = null;

        if (iframe) {
            iframe.addEventListener('load', () => {
                startAnim();
                if (fallbackId) { clearTimeout(fallbackId); fallbackId = null; }
            }, { once: true });

            // страховка на случай, если load долго не прилетает
            fallbackId = setTimeout(startAnim, 3000);
        } else {
            // обычные секции (Проекты/Профиль/и т.п.) — анимируем сразу
            startAnim();
        }

        // подгрузка профильных скриптов
        if (contentId === 'profile_page') {
            await loadProfileScripts();
            if (typeof window.initUploadFile === 'function') {
                window.initUploadFile();
            }
        }
    } catch (error) {
        console.error('Ошибка загрузки:', error);
        contentContainer.innerHTML = `<div class="error-message">Ошибка загрузки</div>`;
        hideLoader();
    }
}

// --- подгрузка js для профиля ---
async function loadProfileScripts() {
    const scripts = [
        '/js/profile/profile.js',
        '/js/profile/profile-edit-info.js',
        '/js/profile/storage/storage-sortable.js',
        '/js/profile/storage/upload-file.js?v=20250926a',
        '/js/profile/storage/upload-conflict-modal.js',
        '/js/profile/storage/storage-table.js',
        '/js/profile/storage/share-link-modal.js',
        // '/js/profile/storage/sorttable.js',
        // '/js/profile/taskManager/taskManager.js',
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

    if (typeof window.initAvatarUpload === 'function') {
        initAvatarUpload();
    }
    if (typeof window.initUserStorage === 'function') {
        window.initUserStorage();
    }

    if (window.taskManager) {
        try {
            const response = await fetch('/api/tasks/active', { credentials: 'same-origin', cache: 'no-store' });
            if (!response.ok) throw new Error(`Ошибка ответа: ${response.status}`);
            const tasks = await response.json();
            if (Array.isArray(tasks)) {
                tasks.forEach(task => window.taskManager.addTask(task));

                const observer = new MutationObserver((mutations, obs) => {
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

// --- показ кеша (extra_page) c аккуратной анимацией ---
function showCachedContent(contentId) {
    const contentContainer = document.getElementById('content');
    const cachedData = contentCache.get(contentId);
    if (!cachedData) {
        console.error('Нет данных в кеше для:', contentId);
        hideLoader();
        return;
    }

    const panel = document.createElement('div');
    panel.className = 'view-panel view-pre';
    panel.innerHTML = cachedData.html;
    contentContainer.replaceChildren(panel);

    // короткое «въезжание» из предсостояния
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

// --- вспомогательные для extra_page ---
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
    const state = contentCache.get(contentId)?.state;
    if (!state) return;
    if (contentId === 'extra_page' && state.model) {
        window.uploadedModel = state.model;
        restoreModel(container);
    }
}

// --- подмена таблиц стилей ---
function loadStyles(styleId) {
    document.querySelectorAll('link[rel=stylesheet][id]').forEach(link => {
        if (link.dataset.persistent === 'true') return; // не трогаем «постоянные» css
        link.disabled = link.id !== styleId;
    });
}

// --- лоадер ---
function showLoader() {
    document.getElementById('loader')?.classList.remove('hidden');
}
function hideLoader() {
    document.getElementById('loader')?.classList.add('hidden');
}

// --- extra_page и загрузка файла ---
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
