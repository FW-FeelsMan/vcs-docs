// wwwroot/js/support-presence.js
(function () {
    console.log("[presence] init on page:", document.title || location.pathname);

    if (window.__supportPresenceStarted) {
        console.log("[presence] already started; skip");
        return;
    }
    window.__supportPresenceStarted = true;

    if (!window.signalR || !window.signalR.HubConnectionBuilder) {
        console.error("[presence] SignalR client not found (CDN failed?)");
        return;
    }

    const conn = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/userStatus")
        .withAutomaticReconnect()
        .build();

    // сохраним на window — удобно дебажить из консоли
    window.__supportPresenceConnection = conn;

    conn.on("ForceLogout", () => {
        console.warn("[presence] ForceLogout received");
        try { conn.stop(); } catch { }
        alert("Вы были вылогинены: выполнен принудительный вход с другого устройства.");
        window.location.href = "/Account/LoginSupport?forced=1";
    });

    conn.onreconnecting(err => console.warn("[presence] reconnecting...", err && err.message));
    conn.onreconnected(id => console.log("[presence] reconnected. connectionId =", id));
    conn.onclose(err => console.warn("[presence] closed", err && err.message));

    conn.start()
        .then(() => console.log("[presence] connected. connectionId =", conn.connectionId))
        .catch(err => console.error("[presence] start failed:", err));

    window.addEventListener("beforeunload", () => { try { conn.stop(); } catch { } });
})();
