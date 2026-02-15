// Password visibility toggle for Login/Register forms.
// Usage:
//   <button class="password-toggle" data-target="#passwordInputId">...</button>

(function () {
    'use strict';

    function setVisible(btn, input, visible) {
        input.type = visible ? 'text' : 'password';
        btn.setAttribute('aria-pressed', visible ? 'true' : 'false');
        btn.setAttribute('aria-label', visible ? 'Скрыть пароль' : 'Показать пароль');
        btn.classList.toggle('is-visible', visible);

        const eye = btn.querySelector('.icon-eye');
        const eyeOff = btn.querySelector('.icon-eye-off');
        if (eye && eyeOff) {
            eye.style.display = visible ? 'none' : '';
            eyeOff.style.display = visible ? '' : 'none';
        }
    }

    function init() {
        const buttons = document.querySelectorAll('.password-toggle[data-target]');
        if (!buttons.length) return;

        for (const btn of buttons) {
            const selector = btn.getAttribute('data-target');
            if (!selector) continue;

            const input = document.querySelector(selector);
            if (!input || input.tagName !== 'INPUT') continue;

            setVisible(btn, input, false);

            btn.addEventListener('click', function () {
                const visibleNow = input.type === 'text';
                setVisible(btn, input, !visibleNow);
                try { input.focus({ preventScroll: true }); } catch { input.focus(); }
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
