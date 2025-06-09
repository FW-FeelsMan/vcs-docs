/**
 * Скрипт для управления чатом
 * Включает функции открытия, закрытия и сворачивания чата
 */
console.log("скрипт чата запущен");
// Состояние чата
const chatState = {
    isOpen: false,
    isMinimized: false,
    activeContact: null
};

// Инициализация чата при загрузке DOM
document.addEventListener("DOMContentLoaded", function () {
    initChat();
});

/**
 * Инициализация чата и всех его компонентов
 */
function initChat() {
    const chatbox = document.getElementById("chatbox");
    const toggleBtn = document.getElementById("chat-toggle-button");
    const closeBtn = document.getElementById("close");
    const minimizeBtn = document.getElementById("minimize");
    const friends = document.querySelectorAll(".friend");
    const searchField = document.getElementById("searchfield");
    const sendMessageInput = document.querySelector("#sendmessage input");
    const sendButton = document.getElementById("send");

    // Если элементы не найдены, выходим
    if (!chatbox || !toggleBtn) {
        console.warn("Элементы чата не найдены");
        return;
    }

    // Обработчик кнопки открытия/закрытия чата
    toggleBtn.addEventListener("click", function () {
        toggleChat();
    });

    // Обработчик кнопки закрытия чата
    if (closeBtn) {
        closeBtn.addEventListener("click", function () {
            closeChat();
        });
    }

    // Обработчик кнопки сворачивания чата
    if (minimizeBtn) {
        minimizeBtn.addEventListener("click", function () {
            minimizeChat();
        });
    }

    // Обработчики для контактов
    friends.forEach(friend => {
        friend.addEventListener("click", function () {
            openChatWithContact(friend);
        });
    });

    // Обработчик поля поиска
    if (searchField) {
        searchField.addEventListener("focus", function () {
            if (this.placeholder === "Поиск контактов...") {
                this.placeholder = "";
            }
        });

        searchField.addEventListener("blur", function () {
            if (this.placeholder === "") {
                this.placeholder = "Поиск контактов...";
            }
        });
    }

    // Обработчик поля ввода сообщения
    if (sendMessageInput) {
        sendMessageInput.addEventListener("focus", function () {
            if (this.placeholder === "Введите сообщение...") {
                this.placeholder = "";
            }
        });

        sendMessageInput.addEventListener("blur", function () {
            if (this.placeholder === "") {
                this.placeholder = "Введите сообщение...";
            }
        });

        // Отправка сообщения по нажатию Enter
        sendMessageInput.addEventListener("keypress", function (e) {
            if (e.key === "Enter") {
                sendMessage();
            }
        });
    }

    // Обработчик кнопки отправки сообщения
    if (sendButton) {
        sendButton.addEventListener("click", function () {
            sendMessage();
        });
    }
}

/**
 * Переключение состояния чата (открыт/закрыт)
 */
function toggleChat() {
    const chatbox = document.getElementById("chatbox");
    const toggleBtn = document.getElementById("chat-toggle-button");

    if (!chatState.isOpen) {
        // Открываем чат
        chatbox.style.display = "block";
        chatbox.classList.add("fadeIn");
        chatbox.classList.remove("fadeOut");

        toggleBtn.innerHTML = "❌";
        toggleBtn.title = "Закрыть чат";

        chatState.isOpen = true;
        chatState.isMinimized = false;
    } else if (chatState.isMinimized) {
        // Разворачиваем свернутый чат
        chatbox.style.transform = "translateY(0)";
        chatbox.style.height = "484px";

        toggleBtn.innerHTML = "❌";
        toggleBtn.title = "Закрыть чат";

        chatState.isMinimized = false;
    } else {
        // Закрываем чат
        closeChat();
    }
}

/**
 * Закрытие чата
 */
function closeChat() {
    const chatbox = document.getElementById("chatbox");
    const toggleBtn = document.getElementById("chat-toggle-button");
    const chatview = document.getElementById("chatview");
    const friendslist = document.getElementById("friendslist");

    chatbox.classList.add("fadeOut");

    // Скрываем чат после завершения анимации
    setTimeout(() => {
        chatbox.style.display = "none";
        chatbox.classList.remove("fadeIn", "fadeOut");

        // Возвращаем к списку контактов
        if (chatview && friendslist) {
            chatview.style.display = "none";
            friendslist.style.display = "block";
        }
    }, 300);

    toggleBtn.innerHTML = "💬";
    toggleBtn.title = "Открыть чат";

    chatState.isOpen = false;
    chatState.isMinimized = false;
}

/**
 * Сворачивание чата
 */
function minimizeChat() {
    const chatbox = document.getElementById("chatbox");
    const toggleBtn = document.getElementById("chat-toggle-button");

    if (!chatState.isMinimized) {
        // Сворачиваем чат (оставляем только заголовок)
        chatbox.style.transform = "translateY(404px)";
        chatbox.style.height = "484px";

        toggleBtn.innerHTML = "🔼";
        toggleBtn.title = "Развернуть чат";

        chatState.isMinimized = true;
    } else {
        // Разворачиваем чат
        chatbox.style.transform = "translateY(0)";

        toggleBtn.innerHTML = "❌";
        toggleBtn.title = "Закрыть чат";

        chatState.isMinimized = false;
    }
}

/**
 * Открытие чата с выбранным контактом
 * @param {HTMLElement} contactElement - Элемент контакта
 */
function openChatWithContact(contactElement) {
    const chatview = document.getElementById("chatview");
    const friendslist = document.getElementById("friendslist");
    const profileImg = document.querySelector("#profile img");
    const profileName = document.querySelector("#profile p");
    const profileEmail = document.querySelector("#profile span");
    const messageImages = document.querySelectorAll(".message:not(.right) img");

    if (!chatview || !friendslist) return;

    // Получаем данные контакта
    const contactImg = contactElement.querySelector("img").src;
    const contactName = contactElement.querySelector("p strong").textContent;
    const contactEmail = contactElement.querySelector("p span").textContent;

    // Обновляем профиль в чате
    if (profileImg) profileImg.src = contactImg;
    if (profileName) profileName.textContent = contactName;
    if (profileEmail) profileEmail.textContent = contactEmail;

    // Обновляем аватары в сообщениях
    messageImages.forEach(img => {
        img.src = contactImg;
    });

    // Показываем чат и скрываем список контактов
    friendslist.style.display = "none";
    chatview.style.display = "block";

    // Анимация появления чата
    setTimeout(() => {
        document.querySelector("#profile p").classList.add("animate");
        document.querySelector("#profile").classList.add("animate");
    }, 100);

    setTimeout(() => {
        document.getElementById("chat-messages").classList.add("animate");
    }, 150);

    // Сохраняем активный контакт
    chatState.activeContact = contactName;
}

/**
 * Отправка сообщения
 */
function sendMessage() {
    const input = document.querySelector("#sendmessage input");
    const chatMessages = document.getElementById("chat-messages");

    if (!input || !chatMessages || input.value.trim() === "") return;

    const messageText = input.value.trim();

    // Создаем элемент сообщения
    const messageDiv = document.createElement("div");
    messageDiv.className = "message right";

    // Добавляем аватар
    const img = document.createElement("img");
    img.src = document.querySelector(".message.right img").src;
    messageDiv.appendChild(img);

    // Создаем пузырь сообщения
    const bubble = document.createElement("div");
    bubble.className = "bubble";
    bubble.textContent = messageText;

    // Добавляем время
    const timeSpan = document.createElement("span");
    const now = new Date();
    timeSpan.textContent = `${now.getHours()}:${now.getMinutes().toString().padStart(2, '0')}`;
    bubble.appendChild(timeSpan);

    messageDiv.appendChild(bubble);

    // Добавляем сообщение в чат
    chatMessages.appendChild(messageDiv);

    // Прокручиваем чат вниз
    chatMessages.scrollTop = chatMessages.scrollHeight;

    // Очищаем поле ввода
    input.value = "";

    // Имитация ответа (для демонстрации)
    setTimeout(() => {
        simulateResponse();
    }, 1000);
}

/**
 * Имитация ответа от собеседника (для демонстрации)
 */
function simulateResponse() {
    const chatMessages = document.getElementById("chat-messages");
    if (!chatMessages) return;

    // Создаем элемент сообщения
    const messageDiv = document.createElement("div");
    messageDiv.className = "message";

    // Добавляем аватар
    const img = document.createElement("img");
    img.src = document.querySelector("#profile img").src;
    messageDiv.appendChild(img);

    // Создаем пузырь сообщения
    const bubble = document.createElement("div");
    bubble.className = "bubble";

    // Выбираем случайный ответ
    const responses = [
        "Хорошо, понял!",
        "Интересно, расскажи подробнее.",
        "Согласен с тобой.",
        "Давай обсудим это позже.",
        "Спасибо за информацию!"
    ];
    const randomResponse = responses[Math.floor(Math.random() * responses.length)];
    bubble.textContent = randomResponse;

    // Добавляем время
    const timeSpan = document.createElement("span");
    const now = new Date();
    timeSpan.textContent = `${now.getHours()}:${now.getMinutes().toString().padStart(2, '0')}`;
    bubble.appendChild(timeSpan);

    messageDiv.appendChild(bubble);

    // Добавляем сообщение в чат
    chatMessages.appendChild(messageDiv);

    // Прокручиваем чат вниз
    chatMessages.scrollTop = chatMessages.scrollHeight;
}


