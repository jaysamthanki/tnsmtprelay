using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmtpServer;
using SmtpServer.Net;
using SmtpServer.Storage;

namespace TNSmtpRelay;

public class SmtpRelayService : BackgroundService
{
    private readonly RelayConfiguration _config;
    private readonly ILogger<SmtpRelayService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private SmtpServer.SmtpServer? _smtpServer;

    public SmtpRelayService(
        RelayConfiguration config,
        ILogger<SmtpRelayService> logger,
        ILoggerFactory loggerFactory)
    {
        _config = config;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TNSmtpRelay service starting");

        var optionsBuilder = new SmtpServerOptionsBuilder();

        foreach (var endpoint in _config.ListenEndpoints)
        {
            var address = IPAddress.Parse(endpoint.Address);
            optionsBuilder.Endpoint(builder =>
                builder.Endpoint(new IPEndPoint(address, endpoint.Port)));
            _logger.LogInformation("Listening on {Address}:{Port}", endpoint.Address, endpoint.Port);
        }

        var options = optionsBuilder.Build();

        var allowList = new IpAllowList(_config.AllowedIPs);
        var messageStore = new RelayMessageStore(_config, _loggerFactory.CreateLogger<RelayMessageStore>());
        var listenerFactory = new IpFilteringEndpointListenerFactory(allowList, _loggerFactory.CreateLogger<IpFilteringEndpointListenerFactory>());

        var serviceProvider = new RelayServiceProvider();
        serviceProvider.Add(typeof(IMessageStoreFactory), new DelegatingMessageStoreFactory(messageStore));
        serviceProvider.Add(typeof(IEndpointListenerFactory), listenerFactory);

        _smtpServer = new SmtpServer.SmtpServer(options, serviceProvider);

        _logger.LogInformation("SMTP relay server started. Relay target: {Host}:{Port}",
            _config.RelayHost, _config.RelayPort);

        try
        {
            await _smtpServer.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Exception during shutdown (expected)");
        }

        _logger.LogInformation("TNSmtpRelay service stopped");
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TNSmtpRelay service stopping");
        _smtpServer?.Shutdown();
        return base.StopAsync(cancellationToken);
    }
}

internal class DelegatingMessageStoreFactory : IMessageStoreFactory
{
    private readonly IMessageStore _store;

    public DelegatingMessageStoreFactory(IMessageStore store)
    {
        _store = store;
    }

    public IMessageStore CreateInstance(ISessionContext context) => _store;
}
