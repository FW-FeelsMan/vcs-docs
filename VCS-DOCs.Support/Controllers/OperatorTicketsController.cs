using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VCS_DOCs.Support.Controllers
{
    [ApiController]
    [Route("api/support/tickets")]
    [Authorize(Policy = "SupportOnly")]
    [Produces("application/json")]
    public sealed class OperatorTicketsController : ControllerBase
    {
        private static readonly string[] Orgs = new[]
        {
            "ООО «Орг 1»","ООО «Орг 2»","ООО «Орг 3»","АО «Корпорация»"
        };

        // GET: /api/support/tickets/orgs
        [HttpGet("orgs")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public ActionResult<IEnumerable<string>> GetOrganizations()
            => Ok(Orgs.OrderBy(o => o, StringComparer.Create(new System.Globalization.CultureInfo("ru-RU"), true)));

        // GET: /api/support/tickets/open?scope=all|mine|unassigned&org=&q=
        [HttpGet("open")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public ActionResult<IEnumerable<OpenTicketRow>> GetOpen(
            [FromQuery] string scope = "all",
            [FromQuery(Name = "org")] string? organization = null,
            [FromQuery(Name = "q")] string? query = null)
        {
            var rows = MockOpenTickets();

            rows = ApplyFilters(rows, scope, organization, query);
            return Ok(rows);
        }

        // GET: /api/support/tickets/closed?scope=all|mine|unassigned&org=&q=
        [HttpGet("closed")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public ActionResult<IEnumerable<ClosedTicketRow>> GetClosed(
            [FromQuery] string scope = "all",
            [FromQuery(Name = "org")] string? organization = null,
            [FromQuery(Name = "q")] string? query = null)
        {
            var rows = MockClosedTickets();

            rows = ApplyFilters(rows, scope, organization, query);
            return Ok(rows);
        }

        // --------- Моки и фильтры ---------

        private static List<OpenTicketRow> MockOpenTickets()
        {
            var list = new List<OpenTicketRow>(24);
            for (int i = 0; i < 24; i++)
            {
                var id = $"12{(1000 + i).ToString().PadLeft(4, '0')}ab";
                var wait = (i % 2 == 0) ? "user" : "operator";      // 'user' | 'operator'
                var assigned = (i % 3 == 0);
                list.Add(new OpenTicketRow
                {
                    Id = id,
                    Subject = $"Демо: проблема с файлом №{i + 1}",
                    UserLogin = $"user{(300 + i).ToString().PadLeft(3, '0')}",
                    Organization = Orgs[i % Orgs.Length],
                    Wait = wait,
                    OperatorLogin = assigned ? "2825" : ""
                });
            }
            return list;
        }

        private static List<ClosedTicketRow> MockClosedTickets()
        {
            var list = new List<ClosedTicketRow>(24);
            for (int i = 0; i < 24; i++)
            {
                var id = $"22{(2000 + i).ToString().PadLeft(4, '0')}zx";
                var assigned = (i % 3 == 0);
                var day = 10 + (i % 9);
                var created = new DateTime(2025, 8, day, 9, (10 + i) % 60, 0, DateTimeKind.Local);
                var closed = new DateTime(2025, 8, day, 12, (25 + i) % 60, 0, DateTimeKind.Local);

                list.Add(new ClosedTicketRow
                {
                    Id = id,
                    Subject = $"Демо: закрытая заявка №{i + 1}",
                    UserLogin = $"user{(200 + i).ToString().PadLeft(3, '0')}",
                    Organization = Orgs[i % Orgs.Length],
                    OperatorLogin = assigned ? "2825" : "",
                    CreatedAt = created,
                    ClosedAt = closed
                });
            }
            return list;
        }

        private static List<OpenTicketRow> ApplyFilters(
            List<OpenTicketRow> rows, string scope, string? org, string? q)
        {
            IEnumerable<OpenTicketRow> e = rows;

            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim().ToLowerInvariant();
                e = e.Where(r => $"{r.Id} {r.Subject} {r.UserLogin} {r.Organization}".ToLowerInvariant().Contains(s));
            }
            if (!string.IsNullOrWhiteSpace(org))
                e = e.Where(r => r.Organization == org);

            scope = (scope ?? "all").ToLowerInvariant();
            e = scope switch
            {
                "mine" => e.Where(r => !string.IsNullOrWhiteSpace(r.OperatorLogin)),
                "unassigned" => e.Where(r => string.IsNullOrWhiteSpace(r.OperatorLogin)),
                _ => e
            };

            return e.ToList();
        }

        private static List<ClosedTicketRow> ApplyFilters(
            List<ClosedTicketRow> rows, string scope, string? org, string? q)
        {
            IEnumerable<ClosedTicketRow> e = rows;

            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim().ToLowerInvariant();
                e = e.Where(r => $"{r.Id} {r.Subject} {r.UserLogin} {r.Organization}".ToLowerInvariant().Contains(s));
            }
            if (!string.IsNullOrWhiteSpace(org))
                e = e.Where(r => r.Organization == org);

            scope = (scope ?? "all").ToLowerInvariant();
            e = scope switch
            {
                "mine" => e.Where(r => !string.IsNullOrWhiteSpace(r.OperatorLogin)),
                "unassigned" => e.Where(r => string.IsNullOrWhiteSpace(r.OperatorLogin)),
                _ => e
            };

            return e.ToList();
        }

        // --------- DTOs (PascalCase → сериализуется как camelCase) ---------

        public sealed class OpenTicketRow
        {
            public string Id { get; set; } = default!;
            public string Subject { get; set; } = default!;
            public string UserLogin { get; set; } = default!;
            public string Organization { get; set; } = default!;
            /// <summary> "user" | "operator" </summary>
            public string Wait { get; set; } = "user";
            public string? OperatorLogin
            {
                get; set;
            }
        }

        public sealed class ClosedTicketRow
        {
            public string Id { get; set; } = default!;
            public string Subject { get; set; } = default!;
            public string UserLogin { get; set; } = default!;
            public string Organization { get; set; } = default!;
            public string? OperatorLogin
            {
                get; set;
            }
            public DateTime CreatedAt
            {
                get; set;
            }
            public DateTime ClosedAt
            {
                get; set;
            }
        }
    }
}
