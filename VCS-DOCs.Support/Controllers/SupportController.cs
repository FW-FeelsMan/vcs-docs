using System.Net;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace VCS_DOCs.Support.Controllers
{
    [ApiController]
    [Route("support/api/[controller]")]
    public class SupportController : ControllerBase
    {
        //private const string CaptchaSessionPrefix = "captcha:";
        private const int CaptchaTtlMinutes = 5;
        private const string CaptchaPrefix = "captcha:";


        private readonly IConfiguration _cfg;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<SupportController> _log;
        private readonly IMemoryCache _cache;

        public SupportController(IConfiguration cfg, IHttpClientFactory http, ILogger<SupportController> log, IMemoryCache cache)
        {
            _cfg = cfg;
            _http = http;
            _log = log;
            _cache = cache;
        }

        // ===== DTO =====
        public class SupportTicketRequest
        {
            public string? FullName
            {
                get; set;
            }
            public string? Login
            {
                get; set;
            }
            public string ReplyTo { get; set; } = "";
            public string Subject { get; set; } = "";
            public string Message { get; set; } = "";
            public int? Code
            {
                get; set;
            }
            public string? OriginalPath
            {
                get; set;
            }
            public string? TraceId
            {
                get; set;
            }
            public string? UserAgent
            {
                get; set;
            }

            // reCAPTCHA v2
            public string? CaptchaToken
            {
                get; set;
            }

            // LocalCaptcha
            public string? CaptchaId
            {
                get; set;
            }
            public string? CaptchaAnswer
            {
                get; set;
            }
        }

        private sealed class RecaptchaVerifyResponse
        {
            public bool success
            {
                get; set;
            }
            public string[]? error_codes
            {
                get; set;
            }
        }

        private sealed class LocalCaptchaData
        {
            public string Id { get; set; } = default!;
            public string Expr { get; set; } = default!;   // например: "12 + 7"
            public string Answer { get; set; } = default!; // "19"
            public DateTime UtcCreated
            {
                get; set;
            }
        }

        // ===== API =====

        [AllowAnonymous]
        [HttpPost("ticket")]
        public async Task<IActionResult> Ticket([FromBody] SupportTicketRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.ReplyTo) ||
                string.IsNullOrWhiteSpace(req.Subject) ||
                string.IsNullOrWhiteSpace(req.Message))
                return BadRequest("missing fields");

            var capEnabled = _cfg.GetValue<bool>("Captcha:Enabled");
            var provider = _cfg["Captcha:Provider"] ?? "ReCaptchaV2";

            if (capEnabled)
            {
                if (provider.Equals("LocalCaptcha", StringComparison.OrdinalIgnoreCase))
                {
                    if (!await ValidateLocalCaptchaAsync(req.CaptchaId, req.CaptchaAnswer))
                        return BadRequest("captcha_failed");
                }
                else if (provider.Equals("ReCaptchaV2", StringComparison.OrdinalIgnoreCase))
                {
                    var secret = _cfg["Captcha:Secret"];
                    if (string.IsNullOrWhiteSpace(secret))
                        return StatusCode((int)HttpStatusCode.PreconditionFailed, "captcha not configured");

                    if (!await VerifyRecaptchaV2Async(secret, req.CaptchaToken, HttpContext.Connection.RemoteIpAddress?.ToString(), ct))
                        return BadRequest("captcha_failed");
                }
            }

            var ticketId = Guid.NewGuid().ToString("N")[..8];
            _log.LogInformation("Support ticket {TicketId} from {Email}: {Subject}; Code={Code}; Path={Path}; Trace={Trace}",
                ticketId, req.ReplyTo, req.Subject, req.Code, req.OriginalPath, req.TraceId);

            return Ok(new { ticketId });
        }

        [AllowAnonymous]
        [HttpGet("captcha/new")]
        public IActionResult NewCaptcha()
        {
            try
            {
                var (expr, answer) = MakeSimpleMath();
                var id = Guid.NewGuid().ToString("N");
                var key = CaptchaPrefix + id;

                var data = new LocalCaptchaData
                {
                    Id = id,
                    Expr = expr,
                    Answer = answer,
                    UtcCreated = DateTime.UtcNow
                };

                _cache.Set(key, data, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CaptchaTtlMinutes)
                });

                return Ok(new { id });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "captcha/new failed");
                return StatusCode(500, "captcha_internal_error");
            }
        }

        [AllowAnonymous]
        [HttpGet("captcha/image/{id}")]
        public IActionResult CaptchaImage([FromRoute] string id)
        {
            if (TryGetCaptcha(id, out var data))
            {
                var svg = RenderCaptchaSvg(data.Expr);
                return Content(svg, "image/svg+xml; charset=utf-8");
            }
            return NotFound();
        }
        private bool TryGetCaptcha(string id, out LocalCaptchaData data)
        {
            var key = CaptchaPrefix + id;
            if (_cache.TryGetValue(key, out LocalCaptchaData? cached) && cached != null)
            {
                if ((DateTime.UtcNow - cached.UtcCreated) <= TimeSpan.FromMinutes(CaptchaTtlMinutes))
                {
                    data = cached;
                    return true;
                }
                _cache.Remove(key);
            }
            data = default!;
            return false;
        }

        private async Task<bool> VerifyRecaptchaV2Async(string secret, string? token, string? remoteIp, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            var form = new Dictionary<string, string>
            {
                ["secret"] = secret,
                ["response"] = token
            };
            if (!string.IsNullOrWhiteSpace(remoteIp))
                form["remoteip"] = remoteIp;

            var client = _http.CreateClient();
            using var resp = await client.PostAsync("https://www.google.com/recaptcha/api/siteverify",
                                                    new FormUrlEncodedContent(form), ct);
            if (!resp.IsSuccessStatusCode) return false;

            var vr = await resp.Content.ReadFromJsonAsync<RecaptchaVerifyResponse>(cancellationToken: ct)
                     ?? new RecaptchaVerifyResponse { success = false };

            return vr.success;
        }

        private async Task<bool> ValidateLocalCaptchaAsync(string? id, string? answer)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(answer))
                return false;

            var key = CaptchaPrefix + id;
            if (!_cache.TryGetValue(key, out LocalCaptchaData? saved) || saved == null)
                return false;

            // одноразовость
            _cache.Remove(key);

            if ((DateTime.UtcNow - saved.UtcCreated) > TimeSpan.FromMinutes(CaptchaTtlMinutes))
                return false;

            return string.Equals(saved.Answer?.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        private static (string expr, string answer) MakeSimpleMath()
        {
            var rnd = Random.Shared;
            int a = rnd.Next(6, 20);
            int b = rnd.Next(6, 20);
            if (rnd.Next(0, 3) == 0 && a > b)
                return ($"{a} - {b}", (a - b).ToString());
            return ($"{a} + {b}", (a + b).ToString());
        }

        // IMPORTANT: format numbers with InvariantCulture so SVG transforms use dots
        private static string RenderCaptchaSvg(string text)
        {
            const int W = 220, H = 64;
            var rnd = Random.Shared;
            var ci = CultureInfo.InvariantCulture;

            string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{W}' height='{H}' viewBox='0 0 {W} {H}'>");

            // Фон + лёгкий градиент
            sb.Append(@"
            <defs>
              <linearGradient id='g' x1='0' y1='0' x2='1' y2='1'>
                <stop offset='0%'  stop-color='#fafafa'/>
                <stop offset='100%' stop-color='#ececec'/>
              </linearGradient>
            </defs>");
            sb.Append("<rect x='0' y='0' width='100%' height='100%' fill='url(#g)'/>");

            // Шумовые линии
            for (int i = 0; i < 5; i++)
            {
                var x1 = rnd.Next(0, W); var y1 = rnd.Next(0, H);
                var x2 = rnd.Next(0, W); var y2 = rnd.Next(0, H);
                var col = $"#{rnd.Next(150, 210):X2}{rnd.Next(150, 210):X2}{rnd.Next(150, 210):X2}";
                sb.Append($"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='{col}' stroke-width='1' opacity='0.8'/>");
            }

            // Точки шума
            for (int i = 0; i < 120; i++)
            {
                var cx = rnd.Next(0, W); var cy = rnd.Next(0, H);
                var r = rnd.Next(1, 3);
                var col = $"#{rnd.Next(170, 235):X2}{rnd.Next(170, 235):X2}{rnd.Next(170, 235):X2}";
                sb.Append($"<circle cx='{cx}' cy='{cy}' r='{r}' fill='{col}' opacity='0.9'/>");
            }

            // Текст
            double x = 28; // старт немного правее
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch)) { x += 14; continue; }

                int y = 34 + rnd.Next(-2, 3);     
                int rot = rnd.Next(-12, 13);      
                var fill = "#1f2937";            

                sb.Append($"<g transform='translate({x.ToString(ci)},{y.ToString(ci)}) rotate({rot.ToString(ci)})'>");
                sb.Append(
                    $"<text x='0' y='0' " +
                    $"font-family='Verdana,Arial,sans-serif' font-size='30' font-weight='700' " +
                    $"fill='{fill}' stroke='#ffffff' stroke-width='1.2' paint-order='stroke fill' " +
                    $"dominant-baseline='middle' text-anchor='middle'>{Esc(ch.ToString())}</text>");
                sb.Append("</g>");

                x += 26 + rnd.Next(0, 5);
            }

            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
