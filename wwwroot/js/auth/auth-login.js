//auth-login.js скрипт входа и регистрации пользователя
const signUpButton = document.getElementById('signUp');
const signInButton = document.getElementById('signIn');
const container = document.getElementById('container');

const loaderOverlay = document.createElement('div');
loaderOverlay.className = 'loader-overlay';
loaderOverlay.innerHTML = `<div class="loader"></div>`;
document.body.appendChild(loaderOverlay);
hideLoader();

function showLoader() {
    loaderOverlay.style.display = 'flex';
}

function hideLoader() {
    loaderOverlay.style.display = 'none';
}

signUpButton.addEventListener('click', () => container.classList.add("right-panel-active"));
signInButton.addEventListener('click', () => container.classList.remove("right-panel-active"));

function validateInput(event, isPassword = false) {
    const input = event.target;
    let value = input.value;

    if (value.length > 20) {
        input.value = value.substring(0, 20);
        return;
    }

    const regex = isPassword ? /^[a-zA-Z0-9@]+$/ : /^[a-zA-Z0-9]+$/;
    if (!regex.test(value)) {
        input.value = value.replace(/[^a-zA-Z0-9@]/g, '');
    }
}

document.querySelectorAll('input[name="Username"]').forEach(input => {
    input.addEventListener('input', event => validateInput(event, false));
});
document.querySelectorAll('input[name="Password"]').forEach(input => {
    input.addEventListener('input', event => validateInput(event, true));
});

const hardwareId = navigator.userAgent;
document.getElementById('hardwareId').value = hardwareId;
document.getElementById('hardwareIdRegister').value = hardwareId;

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
        const response = await fetch(url, {
            method: 'POST',
            body: formData
        });

        if (!response.ok) throw new Error('Ошибка сервера');

        const result = await response.json();
        const errorMessage = document.querySelector(errorSelector);

        if (result.success) {
            if (successRedirect) {
                window.location.href = successRedirect;
            } else {
                document.querySelector('.successful-message').style.display = 'block';
                errorMessage.style.display = 'none';
            }
        } else {
            errorMessage.innerHTML = result.errors.map(error => `<p>${error}</p>`).join('');
            errorMessage.style.display = 'block';
        }
    } catch (error) {
        console.error('Ошибка:', error);
        alert('Произошла ошибка. Попробуйте позже.');
    } finally {
        hideLoader();
    }
}

document.querySelector('.sign-up-container form').addEventListener('submit', event =>
    submitForm(event, 'https://vcs-docs.local:7120/Login?handler=Register', '.error-message-registration')
);

document.querySelector('.sign-in-container form').addEventListener('submit', event =>
    submitForm(event, 'https://vcs-docs.local:7120/Login?handler=Login', '.error-message', '/Index')
);
