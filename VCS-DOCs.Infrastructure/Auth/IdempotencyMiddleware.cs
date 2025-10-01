using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;

namespace VCS_DOCs.Infrastructure
{
    public sealed class IdempotencyMiddleware
    {
        private readonly RequestDelegate _next;

        public IdempotencyMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext ctx, IDistributedCache cache)
        {
            if (HttpMethods.IsPost(ctx.Request.Method) ||
                HttpMethods.IsPut(ctx.Request.Method) ||
                HttpMethods.IsPatch(ctx.Request.Method) ||
                HttpMethods.IsDelete(ctx.Request.Method))
            {
                var key = ctx.Request.Headers["X-Idempotency-Key"].ToString();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    var user = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anon";
                    var cacheKey = $"idem:{user}:{key}";

                    var existed = await cache.GetStringAsync(cacheKey);
                    if (existed != null)
                    {
                        ctx.Response.StatusCode = StatusCodes.Status409Conflict;
                        await ctx.Response.WriteAsync("Duplicate request");
                        return;
                    }

                    await cache.SetStringAsync(
                        cacheKey,
                        "1",
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                        });
                }
            }

            await _next(ctx);
        }
    }
}
