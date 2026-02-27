using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using SmtpServer;
using SmtpServer.IO;
using SmtpServer.Mail;
using SmtpServer.Net;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace TNSmtpRelay;

public class RelayMessageStore : MessageStore
{
    private readonly RelayConfiguration _config;
    private readonly ILogger<RelayMessageStore> _logger;

    public RelayMessageStore(RelayConfiguration config, ILogger<RelayMessageStore> logger)
    {
        _config = config;
        _logger = logger;
    }

    public override async Task<SmtpResponse> SaveAsync(
        ISessionContext context,
        IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer,
        CancellationToken cancellationToken)
    {
        var from = transaction.From?.AsAddress() ?? "(unknown)";
        var to = string.Join(", ", transaction.To.Select(t => t.AsAddress()));

        _logger.LogInformation("Relaying message from {From} to {To}", from, to);

        try
        {
            using var stream = new MemoryStream(buffer.ToArray());
            var message = await MimeMessage.LoadAsync(stream, cancellationToken);

            using var client = new SmtpClient();

            var tlsOption = _config.RelayUseTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(_config.RelayHost, _config.RelayPort, tlsOption, cancellationToken);

            if (!string.IsNullOrEmpty(_config.RelayUsername))
            {
                await client.AuthenticateAsync(_config.RelayUsername, _config.RelayPassword, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Message relayed successfully from {From} to {To}", from, to);
            return SmtpResponse.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to relay message from {From} to {To}", from, to);
            return new SmtpResponse(SmtpReplyCode.TransactionFailed, "Relay failed: " + ex.Message);
        }
    }
}

public class IpFilteringEndpointListenerFactory : IEndpointListenerFactory
{
    private readonly IpAllowList _allowList;
    private readonly ILogger _logger;

    public IpFilteringEndpointListenerFactory(IpAllowList allowList, ILogger logger)
    {
        _allowList = allowList;
        _logger = logger;
    }

    public IEndpointListener CreateListener(IEndpointDefinition endpointDefinition)
    {
        var endpoint = endpointDefinition.Endpoint;
        var listener = new IpFilteringEndpointListener(endpoint, _allowList, _logger);
        _listeners.Add(listener);
        return listener;
    }

    private readonly List<IpFilteringEndpointListener> _listeners = new();
}

public class IpFilteringEndpointListener : IEndpointListener
{
    private readonly TcpListener _tcpListener;
    private readonly IpAllowList _allowList;
    private readonly ILogger _logger;

    public IpFilteringEndpointListener(IPEndPoint endpoint, IpAllowList allowList, ILogger logger)
    {
        _allowList = allowList;
        _logger = logger;
        _tcpListener = new TcpListener(endpoint);
        _tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _tcpListener.Start();
    }

    public async Task<ISecurableDuplexPipe> GetPipeAsync(ISessionContext context, CancellationToken cancellationToken)
    {
        while (true)
        {
            var tcpClient = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
            var remoteEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;

            if (remoteEndPoint != null && _allowList.IsAllowed(remoteEndPoint.Address))
            {
                _logger.LogInformation("Accepted connection from {RemoteEndPoint}", remoteEndPoint);
                var stream = tcpClient.GetStream();
                return new SecurableDuplexPipe(stream, () =>
                {
                    tcpClient.Close();
                    tcpClient.Dispose();
                });
            }

            _logger.LogWarning("Rejected connection from {RemoteEndPoint} - not in allowed IP list", remoteEndPoint);
            tcpClient.Close();
            tcpClient.Dispose();
        }
    }

    public void Dispose()
    {
        _tcpListener.Stop();
    }
}

public class SecurableDuplexPipe : ISecurableDuplexPipe
{
    private readonly Stream _stream;
    private readonly Action _disposeAction;
    private System.IO.Pipelines.PipeReader _input;
    private System.IO.Pipelines.PipeWriter _output;
    private bool _isSecure;

    public SecurableDuplexPipe(Stream stream, Action disposeAction)
    {
        _stream = stream;
        _disposeAction = disposeAction;
        _input = System.IO.Pipelines.PipeReader.Create(stream);
        _output = System.IO.Pipelines.PipeWriter.Create(stream);
    }

    public System.IO.Pipelines.PipeReader Input => _input;
    public System.IO.Pipelines.PipeWriter Output => _output;
    public bool IsSecure => _isSecure;

    public async Task UpgradeAsync(X509Certificate certificate, SslProtocols protocols, CancellationToken cancellationToken)
    {
        var sslStream = new SslStream(_stream, leaveInnerStreamOpen: false);
        await sslStream.AuthenticateAsServerAsync(certificate, false, protocols, false);
        _input = System.IO.Pipelines.PipeReader.Create(sslStream);
        _output = System.IO.Pipelines.PipeWriter.Create(sslStream);
        _isSecure = true;
    }

    public void Dispose()
    {
        _input?.Complete();
        _output?.Complete();
        _disposeAction();
    }
}

public class IpAllowList
{
    private readonly List<(IPAddress Network, int PrefixLength)> _allowedNetworks;

    public IpAllowList(List<string> entries)
    {
        _allowedNetworks = ParseEntries(entries);
    }

    public bool IsAllowed(IPAddress clientIp)
    {
        if (clientIp.IsIPv4MappedToIPv6)
        {
            clientIp = clientIp.MapToIPv4();
        }

        foreach (var (network, prefixLength) in _allowedNetworks)
        {
            if (IsInNetwork(clientIp, network, prefixLength))
                return true;
        }

        return false;
    }

    private static List<(IPAddress, int)> ParseEntries(List<string> entries)
    {
        var result = new List<(IPAddress, int)>();

        foreach (var entry in entries)
        {
            if (entry.Contains('/'))
            {
                var parts = entry.Split('/');
                if (IPAddress.TryParse(parts[0], out var address) && int.TryParse(parts[1], out var prefix))
                {
                    result.Add((address, prefix));
                }
            }
            else
            {
                if (IPAddress.TryParse(entry, out var address))
                {
                    var prefixLength = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
                    result.Add((address, prefixLength));
                }
            }
        }

        return result;
    }

    private static bool IsInNetwork(IPAddress address, IPAddress network, int prefixLength)
    {
        if (address.AddressFamily != network.AddressFamily)
            return false;

        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            if (addressBytes[i] != networkBytes[i])
                return false;
        }

        if (remainingBits > 0)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((addressBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                return false;
        }

        return true;
    }
}

public class RelayServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public void Add(Type type, object instance) => _services[type] = instance;

    public object? GetService(Type serviceType)
    {
        _services.TryGetValue(serviceType, out var service);
        return service;
    }
}
