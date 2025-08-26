// support-sidebar.js

// --- состояние ---
const contentCache = new Map();
let currentContentId = null;
let clickLock = false;

// --- init ---
document.addEventListener('DOMContentLoaded', () => {
    // обработчики клика по пунктам
    document.querySelectorAll('.sidebar-button').forEach(button => {
        button.addEventListener('click', () => window.selectSupportButton(button));
    });

    // выбрать первый пункт по умолчанию
    const firstButton = document.querySelector('.sidebar-button');
    if (firstButton) window.selectSupportButton(firstButton);
});

// --- выбор пункта сайдбара ---
window.selectSupportButton = function (button) {
    if (clickLock) return;
    clickLock = true;
    setTimeout(() => (clickLock = false), 300);

    const contentId = button.getAttribute('data-content');
    const styleId = button.getAttribute('data-style');

    if (!contentId || currentContentId === contentId) return;

    // можно кешировать отдельные страницы, если нужно
    // if (currentContentId === 'faq') { /* пример сохранения состояния */ }

    loadStyles(styleId);
    showLoader();

    if (contentCache.has(contentId)) {
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

// --- загрузка html-фрагмента в центральную область ---
async function loadContent(contentId) {
    const contentContainer = document.getElementById('content');
    if (!contentContainer) return;

    try {
        // очищаем поле сразу
        contentContainer.innerHTML = '';

        // Pages/Content/{contentId}.cshtml -> маршрут /Content/{contentId}
        const url = `/Content/${contentId}`;

        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok) throw new Error(`HTTP error! ${response.status}`);
        const html = await response.text();

        // создаём панель в пред-анимационном состоянии
        const panel = document.createElement('div');
        panel.className = 'view-panel view-pre';
        panel.innerHTML = html;
        contentContainer.replaceChildren(panel);

        // плавная анимация появления
        panel.getBoundingClientRect(); // reflow
        panel.classList.add('view-enter');
        panel.addEventListener('animationend', () => {
            panel.classList.remove('view-enter', 'view-pre');
            hideLoader();
        }, { once: true });

        // при желании — закешировать
        contentCache.set(contentId, { html });

    } catch (error) {
        console.error('Ошибка загрузки:', error);
        contentContainer.innerHTML = `<div class="error-message">Ошибка загрузки</div>`;
        hideLoader();
    }
}

// --- показ кеша с анимацией ---
function showCachedContent(contentId) {
    const data = contentCache.get(contentId);
    const contentContainer = document.getElementById('content');
    if (!data || !contentContainer) {
        hideLoader();
        return;
    }

    const panel = document.createElement('div');
    panel.className = 'view-panel view-pre';
    panel.innerHTML = data.html;
    contentContainer.replaceChildren(panel);

    panel.getBoundingClientRect();
    panel.classList.add('view-enter');
    panel.addEventListener('animationend', () => {
        panel.classList.remove('view-enter', 'view-pre');
        hideLoader();
    }, { once: true });
}

// --- подмена таблиц стилей (как в вебе) ---
function loadStyles(styleId) {
    if (!styleId) return;
    document
        .querySelectorAll('link[rel=stylesheet][id]')
        .forEach(link => {
            if (link.dataset.persistent === 'true') return; // не трогаем постоянные css
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
