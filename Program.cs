using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TNSmtpRelay;

const string ServiceName = "TNSmtpRelay";
const string ServiceDisplayName = "TN SMTP Relay";
const string ServiceDescription = "SMTP relay service that forwards mail through a configured upstream SMTP server";

// Handle install/uninstall before building the host
if (args.Length > 0)
{
    var arg = args[0].ToLowerInvariant().TrimStart('-', '/');

    if (arg == "install")
    {
        var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
        Console.WriteLine($"Installing service '{ServiceName}'...");

        var createResult = RunSc($"create {ServiceName} binPath= \"{exePath}\" start= auto DisplayName= \"{ServiceDisplayName}\"");
        if (createResult != 0)
        {
            Console.WriteLine("Failed to install service.");
            return createResult;
        }

        RunSc($"description {ServiceName} \"{ServiceDescription}\"");
        Console.WriteLine($"Service '{ServiceName}' installed successfully.");
        return 0;
    }

    if (arg == "uninstall")
    {
        Console.WriteLine($"Stopping service '{ServiceName}'...");
        RunSc($"stop {ServiceName}");

        Console.WriteLine($"Removing service '{ServiceName}'...");
        var deleteResult = RunSc($"delete {ServiceName}");
        if (deleteResult != 0)
        {
            Console.WriteLine("Failed to uninstall service.");
            return deleteResult;
        }

        Console.WriteLine($"Service '{ServiceName}' uninstalled successfully.");
        return 0;
    }

    Console.WriteLine("Usage: TNSmtpRelay.exe [-install | -uninstall]");
    Console.WriteLine("  No arguments: Run the service (or as console app for debugging)");
    Console.WriteLine("  -install:     Install as a Windows service");
    Console.WriteLine("  -uninstall:   Uninstall the Windows service");
    return 1;
}

// Build and run the host
var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Logging.AddLog4Net(Path.Combine(AppContext.BaseDirectory, "log4net.config"));

var config = new RelayConfiguration();
builder.Configuration.Bind(config);
builder.Services.AddSingleton(config);

builder.Services.AddWindowsService();
builder.Services.AddHostedService<SmtpRelayService>();

var host = builder.Build();
await host.RunAsync();
return 0;

static int RunSc(string arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = "sc.exe",
        Arguments = arguments,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    using var process = Process.Start(psi);
    if (process == null)
    {
        Console.WriteLine("Failed to start sc.exe");
        return -1;
    }

    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (!string.IsNullOrWhiteSpace(output))
        Console.WriteLine(output.Trim());
    if (!string.IsNullOrWhiteSpace(error))
        Console.Error.WriteLine(error.Trim());

    return process.ExitCode;
}
