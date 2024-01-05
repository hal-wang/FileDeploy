using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAntiforgery();

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = long.Parse(builder.Configuration["MaxRequestBodySize"]!); // 200MB
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = long.Parse(builder.Configuration["MaxRequestBodySize"]!);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
});
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = long.Parse(builder.Configuration["MaxRequestBodySize"]!);
});

var app = builder.Build();
app.UseAntiforgery();

app.MapPut("/", async (
    HttpContext ctx,
    IFormFileCollection files,
    [FromHeader(Name = "ApiKey")] string apiKey,
    [FromForm] string path,
    [FromForm] string? preCommand,
    [FromForm] string? postCommand) =>
{
    if (apiKey != ctx.RequestServices.GetService<IConfiguration>()!["ApiKey"])
    {
        return Results.Unauthorized();
    }

    try
    {
        await ExecCommands(preCommand);
        await CopyFiles(files, path);
        await ExecCommands(postCommand);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }

    return Results.NoContent();
}).DisableAntiforgery();

app.Run();

async static Task CopyFiles(IFormFileCollection files, string path)
{
    foreach (var file in files)
    {
        var target = Path.Join(path, file.Name);
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var targetStream = File.OpenWrite(target);
        await file.CopyToAsync(targetStream);
        Console.WriteLine("put file: " + target);
    }
}

async static Task ExecCommands(string? command)
{
    if (string.IsNullOrEmpty(command)) return;

    var tempFile = Path.Join(Path.GetDirectoryName(Environment.ProcessPath), "temp.command.");
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        tempFile += "bat";
    }
    else
    {
        tempFile += "sh";
    }
    await File.WriteAllTextAsync(tempFile, command);

    var psi = new ProcessStartInfo(tempFile) { RedirectStandardOutput = true };
    var proc = Process.Start(psi) ?? throw new Exception("Can not exec command.");
    using var sr = proc.StandardOutput;
    while (!sr.EndOfStream)
    {
        Console.WriteLine(sr.ReadLine());
    }

    if (!proc.HasExited)
    {
        proc.Kill();
    }
}