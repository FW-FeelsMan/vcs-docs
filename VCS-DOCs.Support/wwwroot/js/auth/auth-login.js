// auth-login.js — вход (Support). Пароль НЕ фильтруем по символам.
const container = document.getElementById('container');

const loaderOverlay = document.createElement('div');
loaderOverlay.className = 'loader-overlay';
loaderOverlay.innerHTML = `<div class="loader"></div>`;
document.body.appendChild(loaderOverlay);
hideLoader();

function showLoader() { loaderOverlay.style.display = 'flex'; }
function hideLoader() { loaderOverlay.style.display = 'none'; }

// ===== Вспомогательные =====
function isPrintableKey(e) {
    if (e.ctrlKey || e.metaKey || e.altKey) return false;
    const k = e.key;
    if (!k || typeof k !== 'string') return false;
    if (k === 'Unidentified') return false;
    if (k.length > 1 && k !== ' ') return false;
    return true;
}

function blinkHint(hintEl, inputEl) {
    if (!hintEl) return;
    hintEl.style.display = 'inline';
    inputEl?.classList.add('over-limit');
    clearTimeout(hintEl.__hideTimer);
    hintEl.__hideTimer = setTimeout(() => {
        hintEl.style.display = 'none';
        inputEl?.classList.remove('over-limit');
    }, 1500);
}

// ===== Лимиты длины и подсказки "не более N символов" =====
function attachMaxLenHints(root = document) {
    const inputs = root.querySelectorAll('input[maxlength], input[data-maxlen], textarea[maxlength], textarea[data-maxlen]');
    inputs.forEach(inp => {
        const form = inp.closest('form') || root;
        const name = inp.getAttribute('name') || inp.getAttribute('id') || '';
        const aspFor = inp.getAttribute('asp-for') || '';
        const max = parseInt(inp.getAttribute('data-maxlen') || inp.getAttribute('maxlength') || '0', 10);

        let hint =
            form.querySelector(`.field-hint[data-hint-for="${name}"]`) ||
            (aspFor ? form.querySelector(`.field-hint[data-hint-for="${aspFor}"]`) : null) ||
            (inp.id ? form.querySelector(`.field-hint[data-hint-for="${inp.id}"]`) : null);

        if (!hint) {
            hint =
                root.querySelector(`.field-hint[data-hint-for="${name}"]`) ||
                (aspFor ? root.querySelector(`.field-hint[data-hint-for="${aspFor}"]`) : null) ||
                (inp.id ? root.querySelector(`.field-hint[data-hint-for="${inp.id}"]`) : null);
        }

        function update() {
            if (!max) return;
            if (inp.value.length > max) {
                inp.value = inp.value.substring(0, max);
            }
            if (hint) {
                hint.style.display = inp.value.length >= max ? 'inline' : 'none';
            }
        }

        function onKeyDown(e) {
            if (!max) return;
            if (!isPrintableKey(e)) return;
            const hasSelection = inp.selectionStart !== inp.selectionEnd;
            if (inp.value.length >= max && !hasSelection) {
                e.preventDefault();
                blinkHint(hint, inp);
            }
        }

        inp.addEventListener('keydown', onKeyDown);
        inp.addEventListener('input', update);
        inp.addEventListener('paste', () => setTimeout(update, 0));
        update();
    });
}

// Валидация полей (логин — только латиница/цифры; пароль — любые символы)
function validateInput(event, isPassword = false) {
    const input = event.target;
    const attrMax = parseInt(input.getAttribute('data-maxlen') || input.getAttribute('maxlength') || '0', 10);
    const maxLen = attrMax || (isPassword ? 100 : 20);

    if (input.value.length > maxLen) {
        input.value = input.value.substring(0, maxLen);
        return;
    }

    if (!isPassword) {
        if (!/^[a-zA-Z0-9]*$/.test(input.value)) {
            input.value = input.value.replace(/[^a-zA-Z0-9]/g, '');
        }
    }
}

document.querySelectorAll('input[name="Username"]').forEach(input => {
    input.addEventListener('input', e => validateInput(e, false));
});
document.querySelectorAll('input[name="Password"]').forEach(input => {
    input.addEventListener('input', e => validateInput(e, true));
});

document.addEventListener('DOMContentLoaded', () => {
    attachMaxLenHints(document);
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

document.querySelector('.sign-in-container form')?.addEventListener('submit', e =>
    submitForm(e, window.location.pathname + '?handler=Login', '.error-message', '/')
);