# Techie Networks SMTP Relay
Very Quick SMTP Relay for small networks to relay through a proper authenticated provider.

## Usage
Edit the appsettings.json to configure your allowed relays (ip or cidr), the port to listen to (along with interfaces, use 0.0.0.0 for all addresses). Edit log4net.config for logging setup.

run

TNSMTPRelay.exe -install
to install as a service

TNSMTPRelay.exe -uninstall
to uninstall the service

TNSMTPRelay.exe 
to run in console mode.

Done
