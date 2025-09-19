// auth-login.js — вход/регистрация (Web). Пароль НЕ фильтруем по символам!
const signUpButton = document.getElementById('signUp');
const signInButton = document.getElementById('signIn');
const container = document.getElementById('container');

const loaderOverlay = document.createElement('div');
loaderOverlay.className = 'loader-overlay';
loaderOverlay.innerHTML = `<div class="loader"></div>`;
document.body.appendChild(loaderOverlay);
hideLoader();

function showLoader() { loaderOverlay.style.display = 'flex'; }
function hideLoader() { loaderOverlay.style.display = 'none'; }

signUpButton?.addEventListener('click', () => container?.classList.add('right-panel-active'));
signInButton?.addEventListener('click', () => container?.classList.remove('right-panel-active'));

function validateInput(event, isPassword = false) {
    const input = event.target;
    let value = input.value;

    // лимиты длины
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
        // пароль: не фильтруем спецсимволы
        // (опционально можно подрезать пробелы по краям:
        // input.value = value.replace(/^\s+|\s+$/g, '');
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
document.getElementById('hardwareIdRegister')?.setAttribute('value', hardwareId);

const params = new URLSearchParams(window.location.search);
if (params.get('message') === 'session_terminated') {
    alert('Вы были разлогинены, так как выполнен вход с другого устройства');
}

async function submitForm(event, url, errorSelector, successRedirect = null) {
    event.preventDefault();
    const form = event.target;
    const formData = new FormData(form);

    const u = (formData.get('Username') || '').toString().trim();
    const p = (formData.get('Password') || '').toString();
    formData.set('Username', u);
    formData.set('Password', p);

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
                document.querySelector('.successful-message')?.setAttribute('style', 'display:block');
                if (errorMessage) errorMessage.style.display = 'none';
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

document.querySelector('.sign-up-container form')?.addEventListener('submit', e =>
    submitForm(e, window.location.pathname + '?handler=Register', '.error-message-registration')
);
document.querySelector('.sign-in-container form')?.addEventListener('submit', e =>
    submitForm(e, window.location.pathname + '?handler=Login', '.error-message', '/')
);