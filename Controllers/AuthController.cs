using Drop1.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.DirectoryServices.AccountManagement;

namespace Drop1.Api.Controllers;

public record LoginRequest(int UserID, string Password);

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        // 1) Is this a real app user?
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserID == req.UserID);
        if (user is null)
            return Unauthorized("User not registered in the app.");

        // 2) Validate credentials depending on mode
        var mode = _config["Auth:Mode"]?.ToUpperInvariant();
        bool valid = false;

        if (mode == "DEV_PASS")
        {
            var devPass = _config["Auth:DevPassword"];
            valid = !string.IsNullOrEmpty(devPass) && req.Password == devPass;
        }
        else if (mode == "AD")
        {
            var domain = _config["Auth:Domain"];
            if (string.IsNullOrWhiteSpace(domain))
                return StatusCode(500, "AD domain not configured.");

            try
            {
                using var ctx = new PrincipalContext(ContextType.Domain, domain);
                valid = ctx.ValidateCredentials(req.UserID.ToString(), req.Password);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"AD error: {ex.Message}");
            }
        }
        else
        {
            return StatusCode(500, "Unknown Auth mode. Use DEV_PASS or AD.");
        }

        if (!valid) return Unauthorized("Invalid credentials.");

        // 3) Create session
        HttpContext.Session.SetInt32("UserID", user.UserID);
        HttpContext.Session.SetString("FullName", user.FullName);
        HttpContext.Session.SetString("Department", user.Department ?? "");

        return Ok(new
        {
            message = "Logged in",
            user = new { user.UserID, user.FullName, user.Department }
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return Ok(new { message = "Logged out" });
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        var uid = HttpContext.Session.GetInt32("UserID");
        if (uid is null) return Unauthorized("Not logged in");
        return Ok(new
        {
            UserID = uid,
            FullName = HttpContext.Session.GetString("FullName"),
            Department = HttpContext.Session.GetString("Department")
        });
    }
}
