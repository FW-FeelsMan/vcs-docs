const contentCache = new Map(); // Кеш только для extra_page
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

    // Сохраняем состояние только для extra_page
    if (currentContentId === 'extra_page') {
        const contentElement = document.querySelector(`[data-cached-content="extra_page"]`);
        if (contentElement) {
            contentCache.set('extra_page', {
                html: contentElement.innerHTML,
                state: getPageState('extra_page')
            });
        }
    }

    // Активируем стиль
    loadStyles(styleId);
    showLoader();

    // Для extra_page используем кеш, остальные грузим заново
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
    try {
        const contentContainer = document.getElementById('content');

        // Всегда очищаем контейнер, кроме случая кеширования
        if (contentId !== 'extra_page' || !contentCache.has('extra_page')) {
            contentContainer.innerHTML = '';
        }

        const response = await fetch(`/Content/${contentId}`);
        if (!response.ok) throw new Error(`HTTP error! ${response.status}`);
        const html = await response.text();

        // Для всех страниц
        contentContainer.innerHTML = html;

        // Только для extra_page
        if (contentId === 'extra_page') {
            contentCache.set('extra_page', {
                html: html,
                state: getPageState('extra_page')
            });
            initExtraPage(); // Инициализация после вставки HTML
        }
    } catch (error) {
        console.error('Ошибка загрузки:', error);
        contentContainer.innerHTML = `<div class="error-message">Ошибка загрузки</div>`;
    } finally {
        hideLoader();
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

    // Для extra_page
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
        // Ваша логика восстановления модели
        viewer.innerHTML = `<iframe src="/ifcjs/index.html?model=${encodeURIComponent(modelData)}"></iframe>`;
    } catch (error) {
        console.error('Ошибка восстановления модели:', error);
    }
}
function getPageState(contentId) {
    // Сохраняем состояние только для extra_page
    if (contentId === 'extra_page') {
        return {
            model: window.uploadedModel // Пример: сохраняем загруженную модель
        };
    }
    return null;
}

function restorePageState(contentId, container) {
    const state = contentCache.get(contentId).state;
    if (!state) return;

    // Восстанавливаем состояние только для extra_page
    if (contentId === 'extra_page' && state.model) {
        window.uploadedModel = state.model;
        restoreModel(container); // Функция восстановления модели
    }
}

// Функция для загрузки стилей
function loadStyles(styleId) {
    document.querySelectorAll("link[rel=stylesheet][id]").forEach(link => {
        link.disabled = link.id !== styleId;
    });
}

// Функция для показа лоадера
function showLoader() {
    const loader = document.getElementById('loader');
    if (loader) loader.classList.remove('hidden');
}

// Функция для скрытия лоадера
function hideLoader() {
    const loader = document.getElementById('loader');
    if (loader) loader.classList.add('hidden');
}

// Инициализация для extra_page
function initExtraPage() {
    // Ждем пока браузер обработает новый HTML
    setTimeout(() => {
        const uploader = document.querySelector('#extra_page input[type="file"]');
        if (!uploader) {
            console.error('Input не найден. Проверьте:');
            console.log('Доступный HTML:', document.getElementById('extra_page')?.innerHTML);
            return;
        }

        uploader.addEventListener('change', handleFileUpload);
        console.log('Инициализация extra_page успешна');
    }, 50); // Небольшая задержка для обработки DOM
}

function handleFileUpload(e) {
    const file = e.target.files[0];
    if (!file) return;

    // Логика загрузки и отображения модели
    window.uploadedModel = processFile(file); // Пример обработки файла
    console.log('Модель загружена:', window.uploadedModel);
}