using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using System.Text.Json;
using System.Text;

namespace VCS_DOCs.Support.Controllers
{
    [ApiController]
    [Route("api/Support/captcha")]
    public class CaptchaProxyController : ControllerBase
    {
        private readonly HttpClient _vdocs;
        private readonly ILogger<CaptchaProxyController> _log;

        public CaptchaProxyController(IHttpClientFactory http, ILogger<CaptchaProxyController> log)
        {
            _vdocs = http.CreateClient("VDocsBridge"); // baseUrl = https://vcs-docs.support.local:7120/
            _log = log;
        }

        // /api/Support/captcha/new  -> прокси в WEB
        [HttpGet("new")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> New(CancellationToken ct)
        {
            var res = await _vdocs.GetAsync("/api/Support/captcha/new",
                                            HttpCompletionOption.ResponseHeadersRead, ct);

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _log.LogWarning("Captcha NEW proxy failed: {Status} {Body}", (int)res.StatusCode, body);
                return StatusCode((int)res.StatusCode, body);
            }

            var contentType = res.Content.Headers.ContentType?.ToString() ?? "application/json";
            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Content(json, contentType);
        }

        // /api/Support/captcha/image/{id}  -> прокси в WEB (картинка/stream)
        [HttpGet("image/{id}")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Image([FromRoute] string id, CancellationToken ct)
        {
            var url = $"/api/Support/captcha/image/{Uri.EscapeDataString(id)}";
            var res = await _vdocs.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _log.LogWarning("Captcha IMAGE proxy failed: {Status} {Body}", (int)res.StatusCode, body);
                return StatusCode((int)res.StatusCode, body);
            }

            var contentType = res.Content.Headers.ContentType?.ToString() ?? "image/png";
            var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return File(stream, contentType);
        }
        //[HttpPost("/api/Support/ticket")]
        //[AllowAnonymous]
        //public async Task<IActionResult> Ticket([FromBody] JsonElement payload, CancellationToken ct)
        //{
        //    var content = new StringContent(payload.GetRawText(), Encoding.UTF8, "application/json");
        //    var res = await _vdocs.PostAsync("/api/Support/ticket", content, ct);

        //    var body = await res.Content.ReadAsStringAsync(ct);
        //    if (!res.IsSuccessStatusCode)
        //    {
        //        _log.LogWarning("Ticket proxy failed: {Status} {Body}", (int)res.StatusCode, body);
        //        return StatusCode((int)res.StatusCode, body);
        //    }
        //    return Content(body, res.Content.Headers.ContentType?.ToString() ?? "application/json");
        //}
    }
}
