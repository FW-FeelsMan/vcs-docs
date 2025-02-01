document.addEventListener("DOMContentLoaded", function () {
    const firstButton = document.querySelector('.sidebar-button');
    if (firstButton) {
        selectButton(firstButton);
    }
});

function selectButton(button) {
    const buttons = document.querySelectorAll('.sidebar-button');
    buttons.forEach(btn => btn.classList.remove('selected'));
    button.classList.add('selected');

    const contentId = button.getAttribute('data-content');
    const styleId = button.getAttribute('data-style');

    showLoader(); 
    loadContent(contentId, styleId);
}

function loadContent(contentId, styleId) {
    fetch(`/html/${contentId}.html`)
        .then(response => response.text())
        .then(html => {
            const contentDiv = document.getElementById('content');
            contentDiv.innerHTML = html;
            loadStyles(styleId);
        })
        .catch(error => console.error('Ошибка загрузки контента:', error))
       .finally(() => hideLoader()); 
}

function loadStyles(styleId) {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = `/css/${styleId}.css`;
    document.head.appendChild(link);
}

function showLoader() {
    const loader = document.getElementById('loader');
    if (loader) {
        loader.classList.remove('hidden');
    }
}

function hideLoader() {
    const loader = document.getElementById('loader');
    if (loader) {
        loader.classList.add('hidden');
    }
}
