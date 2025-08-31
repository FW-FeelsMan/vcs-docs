(function () {
    console.log("[presence] init on page:", document.title);
    if (window.__supportPresenceStarted) return;
    window.__supportPresenceStarted = true;

    if (!window.signalR || !window.signalR.HubConnectionBuilder) {
        console.warn("[presence] SignalR client not found (CDN failed?)");
        return;
    }

    const statusEl = document.querySelector('.sidebar .main-user-status');
    const setStatus = (cls, text) => {
        if (!statusEl) return;
        statusEl.classList.remove('online', 'offline', 'connecting', 'error');
        statusEl.classList.add(cls);
        statusEl.textContent = text;
    };

    const conn = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/userStatus")
        .withAutomaticReconnect()
        .build();

    conn.on("ForceLogout", () => {
        try { conn.stop(); } catch { }
        alert("Вы были отключены: выполнен принудительный вход с другого устройства.");
        window.location.href = "/Account/LoginSupport?forced=1";
    });

    conn.onreconnecting(() => setStatus('connecting', 'Переподключение…'));
    conn.onreconnected(() => setStatus('online', 'В сети'));
    conn.onclose(() => setStatus('offline', 'Отключено'));

    conn.start()
        .then(() => {
            console.log("[presence] connected. connectionId =", conn.connectionId);
            setStatus('online', 'В сети');
        })
        .catch(err => {
            console.error("SignalR start failed:", err);
            setStatus('error', 'Ошибка соединения');
        });

    window.addEventListener("beforeunload", () => { try { conn.stop(); } catch { } });
})();
    