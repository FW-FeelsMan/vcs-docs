(function () {
    const form = document.querySelector('.sign-in-container form');
    const errBox = document.querySelector('.error-message');
    const hw = document.getElementById('hardwareId');
    if (hw) hw.value = navigator.userAgent;

    async function submitForm(event) {
        event.preventDefault();
        errBox.style.display = 'none';
        errBox.innerHTML = '';

        const formData = new FormData(form);

        try {
            const response = await fetch(window.location.pathname + '?handler=Login', {
                method: 'POST',
                body: formData,
            });

            if (!response.ok) throw new Error('Server error');

            const result = await response.json();
            if (result.success) {
                window.location.href = '/';
            } else {
                errBox.innerHTML = (result.errors || ['Ошибка входа']).map(e => `<p>${e}</p>`).join('');
                errBox.style.display = 'block';
            }
        } catch (e) {
            console.error(e);
            errBox.innerHTML = '<p>Произошла ошибка. Попробуйте позже.</p>';
            errBox.style.display = 'block';
        }
    }

    if (form) form.addEventListener('submit', submitForm);
})();
