using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppDomain.CurrentDomain.BaseDirectory : default,
});
builder.Host.UseWindowsService();
builder.Host.ConfigureServices((hostContext, services) =>
{
    services.Configure<HostOptions>(options =>
    {
        options.ShutdownTimeout = TimeSpan.FromSeconds(1);
    });
});
builder.Configuration.AddJsonFile(Path.Join(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json"), false, true);

builder.Services.AddAntiforgery();

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = long.Parse(builder.Configuration["MaxRequestBodySize"]!);
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
app.MapGet("/ping", () =>
{
    return Results.Ok("pong");
});
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
        path = Regex.Replace(path, @"^/+", "/");
        await ExecCommands(preCommand, path);
        await CopyFiles(files, path);
        await ExecCommands(postCommand, path);
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

async static Task ExecCommands(string? command, string path)
{
    if (string.IsNullOrEmpty(command)) return;

    var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    var tempFile = Path.Join(path, "temp.command.");
    tempFile = isWindows ? tempFile + "bat" : tempFile + "sh";
    await File.WriteAllTextAsync(tempFile, command);

    try
    {
        var psi = new ProcessStartInfo()
        {
            FileName = isWindows ? $"\"{tempFile}\"" : "/usr/bin/sh",
            Arguments = isWindows ? "" : $"\"{tempFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = path
        };
        var proc = Process.Start(psi) ?? throw new Exception("Can not exec command.");
        proc.EnableRaisingEvents = true;

        using var stdout = proc.StandardOutput;
        using var stderr = proc.StandardError;

        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await stdout.ReadLineAsync()) != null)
                Console.WriteLine(line);
        });

        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await stderr.ReadLineAsync()) != null)
                Console.Error.WriteLine(line);
        });

        await proc.WaitForExitAsync();
        Console.WriteLine($"Process exited with code {proc.ExitCode}");
    }
    finally
    {
        File.Delete(tempFile);
    }
}
