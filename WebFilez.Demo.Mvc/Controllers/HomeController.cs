using Microsoft.AspNetCore.Mvc;

namespace WebFilez.Demo.Mvc.Controllers;

public sealed class HomeController : Controller
{
    // In a real app, add [Authorize] here and protect the API too.
    [HttpGet("/")]
    public IActionResult Index() => View();

    [HttpGet("/picker")]
    public IActionResult Picker() => View();

    [HttpGet("/error")]
    public IActionResult Error() => Problem();
}
