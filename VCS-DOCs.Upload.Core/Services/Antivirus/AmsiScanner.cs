using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VCS_DOCs.Upload.Core.Services.Antivirus
{
    public sealed class AmsiScanner : IAntivirusScanner, IDisposable
    {
        private readonly string _appName;
        private readonly ILogger<AmsiScanner>? _log;
        private IntPtr _ctx;

        private const uint AMSI_RESULT_DETECTED_THRESHOLD = 0x8000;

        public AmsiScanner(string appName = "VCS-DOCs", ILogger<AmsiScanner>? log = null)
        {
            _appName = appName;
            _log = log;
            TryInit();
        }

        public void Dispose()
        {
            try { if (_ctx != IntPtr.Zero) AmsiUninitialize(_ctx); } catch { }
            _ctx = IntPtr.Zero;
        }

        private void TryInit()
        {
            try
            {
                var hr = AmsiInitialize(_appName, out _ctx);
                if (hr < 0) _ctx = IntPtr.Zero;
            }
            catch { _ctx = IntPtr.Zero; }
        }

        public async Task<ScanVerdict> ScanAsync(Stream content, string? contentName = null, CancellationToken ct = default)
        {
            if (_ctx == IntPtr.Zero) return ScanVerdict.Unavailable;

            IntPtr session = IntPtr.Zero;
            try
            {
                var hrOpen = AmsiOpenSession(_ctx, out session);
                if (hrOpen < 0) return ScanVerdict.Unavailable;

                var name = string.IsNullOrWhiteSpace(contentName) ? "upload-stream" : contentName;
                var buffer = new byte[1024 * 1024];

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int read = await content.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (read <= 0) break;

                    var hrScan = AmsiScanBuffer(_ctx, buffer, (uint)read, name, session, out uint res);
                    if (hrScan < 0) return ScanVerdict.Unavailable;

                    // логируем коды AMSI для диагностики
                    _log?.LogDebug("AMSI chunk scanned: result=0x{Res:X}", res);

                    if (res >= AMSI_RESULT_DETECTED_THRESHOLD)
                        return ScanVerdict.Infected;
                }

                return ScanVerdict.Clean;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "AMSI scan error");
                return ScanVerdict.Error;
            }
            finally
            {
                try { if (session != IntPtr.Zero) AmsiCloseSession(_ctx, session); } catch { }
            }
        }

        [DllImport("amsi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int AmsiInitialize(string appName, out IntPtr amsiContext);

        [DllImport("amsi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern void AmsiUninitialize(IntPtr amsiContext);

        [DllImport("amsi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int AmsiOpenSession(IntPtr amsiContext, out IntPtr session);

        [DllImport("amsi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern void AmsiCloseSession(IntPtr amsiContext, IntPtr session);

        [DllImport("amsi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int AmsiScanBuffer(
            IntPtr amsiContext,
            byte[] buffer,
            uint length,
            string contentName,
            IntPtr session,
            out uint result);
    }
}
