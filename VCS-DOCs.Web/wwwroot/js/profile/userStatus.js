// userStatus.js — отслеживание подключения и статуса пользователя 
(function () {
    const initializeSignalR = () => {
        if (window.location.pathname.toLowerCase() === '/login') return;

        if (typeof signalR === 'undefined') {
            console.error('[SignalR] Не загружен');
            return;
        }

        const getUserStatusEl = () => document.querySelector(".user-status");

        const setUserStatus = (text, className) => {
            const el = getUserStatusEl();
            if (!el) return;
            el.textContent = text;
            el.classList.remove("online", "offline", "connecting", "error");
            if (className) el.classList.add(className);
        };

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/Data/userStatusHub", {
                skipNegotiation: true,
                transport: signalR.HttpTransportType.WebSockets
            })
            .configureLogging(signalR.LogLevel.Warning)
            .withAutomaticReconnect()
            .build();

        connection.on("UserStatusUpdated", (userId, isOnline) => {
          //  console.log(`Статус пользователя ${userId}: ${isOnline ? 'онлайн' : 'оффлайн'}`);
        });

        connection.on("ForceLogout", () => {
            console.warn("[SignalR] Получена команда ForceLogout");
            alert("Связь с сервером разорвана. Причина: вход с другого устройства");
            localStorage.removeItem('token');
            window.location.href = '/Login?message=session_terminated';
        });

        connection.onclose(error => {
            console.error('[SignalR] Соединение закрыто:', error);
            setUserStatus("Оффлайн", "offline");
        });

        connection.onreconnecting(() => {
            console.warn("[SignalR] Переподключение...");
            setUserStatus("Переподключение...", "connecting");
        });

        connection.onreconnected(() => {
           // console.log("[SignalR] Переподключение завершено");
            setUserStatus("В сети", "online");
        });

        connection.start()
            .then(() => {
              //  console.log("[SignalR] Подключено");
                setUserStatus("В сети", "online");
            })
            .catch(err => {
                console.error("[SignalR] Ошибка подключения:", err);
                setUserStatus("Ошибка", "error");
            });

        // Резервная проверка, если соединение молча отвалилось
        setInterval(() => {
            switch (connection.state) {
                case "Connected":
                    setUserStatus("В сети", "online");
                    break;
                case "Disconnected":
                    setUserStatus("Оффлайн", "offline");
                    break;
                case "Reconnecting":
                    setUserStatus("Переподключение...", "connecting");
                    break;
                default:
                    setUserStatus("Ошибка", "error");
                    break;
            }
        }, 5000);
    };

    document.addEventListener("DOMContentLoaded", initializeSignalR);

    // экспортируем для ручного вызова при SPA-переходах
    window.reconnectUserStatus = initializeSignalR;
})();
