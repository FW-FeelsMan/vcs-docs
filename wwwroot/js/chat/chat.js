// chat.js
document.addEventListener("DOMContentLoaded", function () {
    const tryInitChat = () => {
        const chatBox = document.getElementById("chatbox");
        const toggleBtn = document.getElementById("chat-toggle-button");
        const closeBtn = document.getElementById("chatbox-close") || document.getElementById("close");
        const minimizeBtn = document.getElementById("minimize");

        if (!chatBox || !toggleBtn || !closeBtn) {
            console.warn("Чат: элементы не найдены, повторим позже...");
            return false;
        }

        if (window.chatInitialized) return true;
        window.chatInitialized = true;

        const openChat = () => {
            chatBox.classList.remove("collapsed");
            chatBox.classList.remove("fadeOut");
            chatBox.classList.add("fadeIn");
            chatBox.style.display = "block";
            toggleBtn.classList.add("lifted");
        };

        const closeChat = () => {
            chatBox.classList.remove("fadeIn");
            chatBox.classList.add("fadeOut");
            toggleBtn.classList.remove("lifted");
            setTimeout(() => {
                chatBox.style.display = "none";
                chatBox.classList.remove("fadeOut");
            }, 300);
        };

        const toggleChat = () => {
            const isCollapsed = chatBox.classList.contains("collapsed");

            if (isCollapsed) {
                chatBox.classList.remove("collapsed");
                chatBox.classList.remove("fadeOut");
                chatBox.classList.add("fadeIn");
                chatBox.style.display = "block";
                toggleBtn.classList.add("lifted");
            } else {
                chatBox.classList.remove("fadeIn");
                chatBox.classList.add("fadeOut");
                chatBox.classList.add("collapsed");
                toggleBtn.classList.remove("lifted");
                setTimeout(() => {
                    chatBox.style.display = "none";
                    chatBox.classList.remove("fadeOut");
                }, 300);
            }
        };

        toggleBtn.addEventListener("click", toggleChat);
        closeBtn.addEventListener("click", closeChat);
        minimizeBtn?.addEventListener("click", closeChat);

        // Вкладки
        const tabButtons = document.querySelectorAll("#topmenu span");
        const tabContents = {
            "Контакты": document.getElementById("chat-tab-contacts"),
            "Чаты": document.getElementById("chat-tab-chats"),
            "Задачи": document.getElementById("chat-tab-tasks"),
        };

        tabButtons.forEach(btn => {
            btn.addEventListener("click", () => {
                tabButtons.forEach(b => b.classList.remove("active"));
                btn.classList.add("active");

                Object.keys(tabContents).forEach(key => {
                    tabContents[key].style.display = key === btn.textContent ? "block" : "none";
                });

                sessionStorage.setItem("chatActiveTab", btn.textContent);
            });
        });

        // Восстановление активной вкладки
        const restoreChatTab = () => {
            const savedTab = sessionStorage.getItem("chatActiveTab") || "Контакты";
            const btn = [...tabButtons].find(b => b.textContent === savedTab);
            if (btn) btn.click();
        };
        restoreChatTab();

       // window.addEventListener("taskUpdate", async (e) => {
       //     const { renderChatMiniTaskCard } = await import('/js/common/tasks-shared.js');
        //    console.log("[Chat] Получена задача:", e.detail);
       //     renderChatMiniTaskCard(e.detail);
      //  });
        return true;
    };

    const maxTries = 50;
    let attempt = 0;
    const interval = setInterval(() => {
        if (tryInitChat() || ++attempt > maxTries) {
            clearInterval(interval);
            if (attempt > maxTries) {
                console.error("Чат: не удалось инициализировать после множества попыток");
            }
        }
    }, 200);
});