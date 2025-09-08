using Drop1.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.DirectoryServices.AccountManagement;
using System.Security.Claims;

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

        // 3) Create session with explicit session loading
        await HttpContext.Session.LoadAsync(); // Ensure session is loaded

        HttpContext.Session.SetString("UserID", user.UserID.ToString());
        HttpContext.Session.SetString("FullName", user.FullName);
        HttpContext.Session.SetString("Department", user.Department ?? "");

        // 🔧 IMPORTANT: Commit the session and wait
        await HttpContext.Session.CommitAsync();

        // ✅ Also add claims so controllers like UploadFolder can use User.FindFirstValue
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
            new Claim(ClaimTypes.Name, user.FullName ?? "")
        };
        var identity = new ClaimsIdentity(claims, "session");
        HttpContext.User = new ClaimsPrincipal(identity);

        // 🔧 DEBUGGING: Log session info
        Console.WriteLine($"Session created for user {user.UserID}");
        Console.WriteLine($"Session ID: {HttpContext.Session.Id}");
        Console.WriteLine($"Session available: {HttpContext.Session.IsAvailable}");

        return Ok(new
        {
            message = "Logged in",
            user = new { user.UserID, user.FullName, user.Department },
            debug = new
            {
                sessionId = HttpContext.Session.Id,
                sessionAvailable = HttpContext.Session.IsAvailable
            }
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.Session.LoadAsync();
        HttpContext.Session.Clear();
        await HttpContext.Session.CommitAsync();
        return Ok(new { message = "Logged out" });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        // 🔧 DEBUGGING: Log session info
        Console.WriteLine($"Auth check - Session ID: {HttpContext.Session.Id}");
        Console.WriteLine($"Auth check - Session available: {HttpContext.Session.IsAvailable}");

        await HttpContext.Session.LoadAsync(); // Ensure session is loaded

        var uidString = HttpContext.Session.GetString("UserID");
        Console.WriteLine($"Auth check - UserID from session: {uidString}");

        if (string.IsNullOrEmpty(uidString))
        {
            Console.WriteLine("Auth check failed - no UserID in session");
            return Unauthorized("Not logged in");
        }

        var uid = long.Parse(uidString);

        var result = new
        {
            UserID = uid,
            FullName = HttpContext.Session.GetString("FullName"),
            Department = HttpContext.Session.GetString("Department"),
            debug = new
            {
                sessionId = HttpContext.Session.Id,
                sessionAvailable = HttpContext.Session.IsAvailable
            }
        };

        Console.WriteLine($"Auth check successful for user {uid}");
        return Ok(result);
    }
}
