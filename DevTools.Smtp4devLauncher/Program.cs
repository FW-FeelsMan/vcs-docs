// DevTools.Smtp4devLauncher/Program.cs
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

static class Launcher
{
    static string LogPath =>
        Path.Combine(Path.GetTempPath(), "smtp4dev-launcher.log");

    static void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Console.WriteLine(line);
        try { File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8); } catch { }
    }

    static string? FindInPath(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            try
            {
                var cand = Path.Combine(p.Trim(), fileName);
                if (File.Exists(cand)) return cand;
            }
            catch { }
        }
        return null;
    }

    static string? ResolveSmtp4devExe()
    {
        // 1) глобальный dotnet tool
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var toolExe = Path.Combine(home, ".dotnet", "tools", "smtp4dev.exe");
        if (File.Exists(toolExe)) return toolExe;

        // 2) PATH
        var fromPath = FindInPath("smtp4dev.exe");
        if (!string.IsNullOrEmpty(fromPath)) return fromPath;

        // 3) *nix
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var unix = FindInPath("smtp4dev");
            if (!string.IsNullOrEmpty(unix)) return unix;
        }
        return null;
    }

    static bool IsProcessRunning() =>
        Process.GetProcessesByName("smtp4dev").Any();

    static bool IsTcpListening(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            return task.Wait(TimeSpan.FromMilliseconds(300)) && client.Connected;
        }
        catch { return false; }
    }

    static async Task<bool> WaitForHttpAsync(string url, TimeSpan timeout)
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            var stop = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < stop)
            {
                try
                {
                    var resp = await http.GetAsync(url);
                    if ((int)resp.StatusCode < 500) return true; // 200..499 — считаем жив
                }
                catch { }
                await Task.Delay(250);
            }
        }
        catch { }
        return false;
    }

    static async Task<int> Main()
    {
        // очистим лог
        try { File.WriteAllText(LogPath, "smtp4dev launcher log\n", Encoding.UTF8); } catch { }

        Log("Ищу smtp4dev…");
        var exe = ResolveSmtp4devExe();
        if (exe == null)
        {
            Log("Не найден smtp4dev. Установите: dotnet tool install --global Rnwood.Smtp4dev");
            return 2;
        }
        Log($"Найден: {exe}");

        // Уже запущен?
        if (IsProcessRunning() || IsTcpListening("127.0.0.1", 2525))
        {
            Log("Похоже, smtp4dev уже запущен (процесс или порт 2525 занят). Ничего не делаю.");
            return 0;
        }

        var args = "--smtpport 2525 --urls http://localhost:5000";
        Log($"Стартую: \"{exe}\" {args}");

        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };

        try
        {
            var proc = Process.Start(psi);
            if (proc == null)
            {
                Log("Process.Start вернул null — не удалось запустить.");
                return 3;
            }

            // Пишем вывод в лог асинхронно
            _ = Task.Run(async () =>
            {
                try { string? line; while ((line = await proc.StandardOutput.ReadLineAsync()) != null) Log("[out] " + line); }
                catch { }
            });
            _ = Task.Run(async () =>
            {
                try { string? line; while ((line = await proc.StandardError.ReadLineAsync()) != null) Log("[err] " + line); }
                catch { }
            });

            // Ждём, пока UI поднимется (без авто-открытия браузера)
            Log("Жду UI http://localhost:5000 …");
            var ok = await WaitForHttpAsync("http://localhost:5000/", TimeSpan.FromSeconds(8));
            Log(ok ? "UI доступен." : "Не дождался UI. Проверьте лог: " + LogPath);
            return ok ? 0 : 4;
        }
        catch (Exception ex)
        {
            Log("Ошибка запуска: " + ex.Message);
            return 5;
        }
    }
}