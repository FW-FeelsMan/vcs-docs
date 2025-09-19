// auth-login.js — вход (Support). Без регистрации. Пароль НЕ фильтруем по символам.
const container = document.getElementById('container');

const loaderOverlay = document.createElement('div');
loaderOverlay.className = 'loader-overlay';
loaderOverlay.innerHTML = `<div class="loader"></div>`;
document.body.appendChild(loaderOverlay);
hideLoader();

function showLoader() { loaderOverlay.style.display = 'flex'; }
function hideLoader() { loaderOverlay.style.display = 'none'; }

// Валидация полей
function validateInput(event, isPassword = false) {
    const input = event.target;
    let value = input.value;

    const maxLen = isPassword ? 100 : 20;
    if (value.length > maxLen) {
        input.value = value.substring(0, maxLen);
        return;
    }

    if (!isPassword) {
        // логин: латиница + цифры
        if (!/^[a-zA-Z0-9]*$/.test(value)) {
            input.value = value.replace(/[^a-zA-Z0-9]/g, '');
        }
    } else {
        // пароль: не фильтруем (разрешаем спецсимволы)
    }
}

document.querySelectorAll('input[name="Username"]').forEach(input => {
    input.addEventListener('input', e => validateInput(e, false));
});
document.querySelectorAll('input[name="Password"]').forEach(input => {
    input.addEventListener('input', e => validateInput(e, true));
});

const hardwareId = navigator.userAgent;
document.getElementById('hardwareId')?.setAttribute('value', hardwareId);

const params = new URLSearchParams(window.location.search);
if (params.get('message') === 'session_terminated') {
    alert('Вы были разлогинены, так как выполнен вход с другого устройства');
}

async function submitForm(event, url, errorSelector, successRedirect = null) {
    event.preventDefault();
    const form = event.target;
    const formData = new FormData(form);
    showLoader();

    try {
        const response = await fetch(url, { method: 'POST', body: formData });
        if (!response.ok) throw new Error('Ошибка сервера');

        const result = await response.json();
        const errorMessage = document.querySelector(errorSelector);

        if (result.success) {
            if (successRedirect) {
                window.location.href = successRedirect;
            } else {
                // в саппорте регистрации нет — просто уходим на главную/панель
                window.location.reload();
            }
        } else {
            if (errorMessage) {
                errorMessage.innerHTML = (result.errors || []).map(e => `<p>${e}</p>`).join('');
                errorMessage.style.display = 'block';
            } else {
                alert((result.errors || ['Ошибка']).join('\n'));
            }
        }
    } catch (error) {
        console.error('Ошибка:', error);
        alert('Произошла ошибка. Попробуйте позже.');
    } finally {
        hideLoader();
    }
}

// только логин-форма
document.querySelector('.sign-in-container form')?.addEventListener('submit', e =>
    submitForm(e, window.location.pathname + '?handler=Login', '.error-message', '/')
);