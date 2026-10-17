using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// The Kids plugin API.
/// </summary>
[ApiController]
[Route("Kids")]
[Produces(MediaTypeNames.Application.Json)]
public class KidsController : ControllerBase
{
    /// <summary>
    /// Lets clients detect the plugin, even before signing in.
    /// </summary>
    /// <response code="200">The plugin is installed.</response>
    /// <returns>The plugin and API versions.</returns>
    [HttpGet("Info")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<KidsInfo> GetInfo() => new KidsInfo(Plugin.Instance?.Version.ToString() ?? string.Empty, ApiVersion: 1);
}
