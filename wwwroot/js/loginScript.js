// Получаем элементы управления
const signUpButton = document.getElementById('signUp');
const signInButton = document.getElementById('signIn');
const container = document.getElementById('container');

// Создаем элементы для лоадера
const loaderOverlay = document.createElement('div');
loaderOverlay.className = 'loader-overlay';
loaderOverlay.innerHTML = `
    <div class="loader"></div>
`;
document.body.appendChild(loaderOverlay);
hideLoader(); // Скрываем лоадер на старте

// Функции для управления лоадером
function showLoader() {
    loaderOverlay.style.display = 'flex';
}

function hideLoader() {
    loaderOverlay.style.display = 'none';
}

// Событие для переключения на форму регистрации
signUpButton.addEventListener('click', () => {
    container.classList.add("right-panel-active");
});

// Событие для переключения на форму входа
signInButton.addEventListener('click', () => {
    container.classList.remove("right-panel-active");
});

// Получаем HardwareId (например, через userAgent)
const hardwareId = navigator.userAgent; // Или любой другой способ получения уникального идентификатора
document.getElementById('hardwareId').value = hardwareId;
document.getElementById('hardwareIdRegister').value = hardwareId;

document.querySelector('.sign-up-container form').addEventListener('submit', async (event) => {
    event.preventDefault(); // Останавливаем стандартное поведение формы

    const form = event.target;
    const formData = new FormData(form);

    showLoader(); // Показываем лоадер
    try {
        // Отправляем запрос на фиксированный URL
        const response = await fetch('https://vcs-docs.local:7120/Login?handler=Register', {
            method: 'POST',
            body: formData,
        });

        if (!response.ok) {
            throw new Error('Ошибка сервера');
        }

        const result = await response.json();

        // Проверяем, существуют ли элементы для сообщений
        const successMessage = document.querySelector('.successful-message');
        const errorMessage = document.querySelector('.error-message-registration');

        if (result.success) {
            if (successMessage) {
                successMessage.style.display = 'block';
            }
            if (errorMessage) {
                errorMessage.style.display = 'none';
            }
        } else {
            if (successMessage) {
                successMessage.style.display = 'none';
            }
            if (errorMessage) {
                errorMessage.innerHTML = result.errors.map(error => `<p>${error}</p>`).join('');
                errorMessage.style.display = 'block';
            }
        }
    } catch (error) {
        console.error('Ошибка:', error);
        alert('Произошла ошибка при регистрации. Попробуйте позже.');
    } finally {
        hideLoader(); // Скрываем лоадер
    }
});

// Добавляем обработчик для формы входа
document.querySelector('.sign-in-container form').addEventListener('submit', async (event) => {
    event.preventDefault(); // Останавливаем стандартное поведение формы

    const form = event.target;
    const formData = new FormData(form);

    showLoader(); // Показываем лоадер
    try {
        // Отправляем AJAX-запрос для входа
        const response = await fetch('https://vcs-docs.local:7120/Login?handler=Login', {
            method: 'POST',
            body: formData,
        });

        if (!response.ok) {
            throw new Error('Ошибка сервера');
        }

        const result = await response.json();

        // Элементы для отображения сообщений
        const errorMessage = document.querySelector('.error-message');
        const successMessage = document.querySelector('.successful-message');

        if (result.success) {
            // Успешный вход — перенаправляем на главную страницу
            window.location.href = '/Index';
        } else {
            // Показываем ошибки
            if (errorMessage) {
                errorMessage.innerHTML = result.errors.map(error => `<p>${error}</p>`).join('');
                errorMessage.style.display = 'block';
            }
            if (successMessage) {
                successMessage.style.display = 'none';
            }
        }
    } catch (error) {
        console.error('Ошибка:', error);
        alert('Произошла ошибка при входе. Попробуйте позже.');
    } finally {
        hideLoader(); // Скрываем лоадер
    }
});
