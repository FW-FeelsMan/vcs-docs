(function () {
    if (window.__vdocsPresenceStarted) return;
    window.__vdocsPresenceStarted = true;

    if (!window.signalR || !window.signalR.HubConnectionBuilder) {
        console.warn("[vdocs-presence] SignalR client not found");
        return;
    }

    const conn = new signalR.HubConnectionBuilder()
        .withUrl("/Data/userStatusHub")
        .withAutomaticReconnect()
        .build();

    conn.on("ForceLogout", () => {
        try { conn.stop(); } catch { }
        // JwtId уже инвалидируем на сервере; редирект принудительно:
        window.location.href = "/Login?forced=1";
    });

    conn.onreconnecting(() => console.log("[vdocs-presence] reconnecting..."));
    conn.onreconnected(() => console.log("[vdocs-presence] reconnected"));
    conn.onclose(() => console.log("[vdocs-presence] closed"));

    conn.start()
        .then(() => console.log("[vdocs-presence] connected:", conn.connectionId))
        .catch(err => console.error("[vdocs-presence] start failed:", err));

    window.addEventListener("beforeunload", () => { try { conn.stop(); } catch { } });
})();
