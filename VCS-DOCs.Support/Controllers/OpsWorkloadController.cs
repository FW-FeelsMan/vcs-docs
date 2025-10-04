using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCS_DOCs.Support.Monitoring;
using System.Linq;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/ops/workload")]
[Authorize(Policy = "SupportOnly")]
public sealed class OpsWorkloadController : ControllerBase
{
    private readonly WorkloadStore _store;
    public OpsWorkloadController(WorkloadStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<WorkloadStore.WorkloadSnapshot>> Get([FromQuery] string range = "1h")
    {
        range = range is "15m" or "24h" ? range : "1h";
        var snapshot = await _store.BuildSnapshot(range);
        return Ok(snapshot);
    }
}