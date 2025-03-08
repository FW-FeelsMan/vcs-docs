(function () {
    const initializeSignalR = () => {
        if (window.location.pathname.toLowerCase() === '/login') return;

        if (typeof signalR === 'undefined') {
            console.error('SignalR не загружен');
            return;
        }

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/Data/userStatusHub", {
                skipNegotiation: true,
                transport: signalR.HttpTransportType.WebSockets
            })
            .configureLogging(signalR.LogLevel.Warning)
            .withAutomaticReconnect()
            .build();

        connection.on("UserStatusUpdated", (userId, isOnline) => {
            console.log(`Статус пользователя ${userId}: ${isOnline ? 'онлайн' : 'оффлайн'}`);
        });

        connection.on("ForceLogout", () => {
            console.log("Received ForceLogout command");
            localStorage.removeItem('token');
            window.location.href = '/Login?message=session_terminated';
        });

        connection.onclose(error => {
            if (error?.statusCode === 401) {
                window.location.href = '/Login';
            }
            console.error('Соединение закрыто:', error);
        });
        connection.on("ForceLogout", function () {
            console.log("Received ForceLogout command");
            alert("Связь с сервером разорвана. Причина: принудительный вход с другого устройства");
            window.location.href = "/Login";
        });
        connection.on("DebugResponse", function (message) {
            console.log("Получено сообщение от сервера:", message);
        });

        // Вызови метод с клиента после подключения
        connection.start().then(() => {
            connection.invoke("DebugMessage");
        });
    };

    initializeSignalR();
})();