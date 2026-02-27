namespace TNSmtpRelay;

public class RelayConfiguration
{
    public string RelayHost { get; set; } = "smtp.example.com";
    public int RelayPort { get; set; } = 587;
    public string RelayUsername { get; set; } = "";
    public string RelayPassword { get; set; } = "";
    public bool RelayUseTls { get; set; } = true;
    public List<string> AllowedIPs { get; set; } = new() { "127.0.0.1" };
    public List<ListenEndpoint> ListenEndpoints { get; set; } = new()
    {
        new ListenEndpoint { Address = "0.0.0.0", Port = 25 }
    };
}

public class ListenEndpoint
{
    public string Address { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 25;
}
