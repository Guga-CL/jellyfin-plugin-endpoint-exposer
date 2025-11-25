using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.EndpointExposer.Controllers
{
    [ApiController]
    [Route("endpoint-exposer/watchplanner")]
    public class WatchplannerController : ControllerBase
    {
        private readonly IHttpClientFactory _httpFactory;

        public WatchplannerController(IHttpClientFactory httpFactory)
        {
            _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        }

        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new { status = "ok" });
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var resp = await HttpHelpers.SendWithRetryAsync(
                    client => client.GetAsync("https://example.com/status"),
                    _httpFactory).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    return StatusCode((int)resp.StatusCode, "Upstream returned non-success");
                }

                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return Ok(body);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Request failed: {ex.Message}");
            }
        }
    }
}
