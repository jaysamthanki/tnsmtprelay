# Quick test script for TNSmtpRelay
# Run TNSmtpRelay.exe first (as console app, no args), then run this script.

param(
    [string]$SmtpServer = "localhost",
    [int]$Port = 25,
    [string]$From = "test@technossus.com",
    [string]$To = "jthanki@techienetworks.com",
    [string]$Subject = "TNSmtpRelay Test - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    [string]$Body = "This is a test message sent through TNSmtpRelay."
)

Write-Host "Sending test email via $SmtpServer`:$Port" -ForegroundColor Cyan
Write-Host "  From:    $From"
Write-Host "  To:      $To"
Write-Host "  Subject: $Subject"
Write-Host ""

try {
    Send-MailMessage `
        -SmtpServer $SmtpServer `
        -Port $Port `
        -From $From `
        -To $To `
        -Subject $Subject `
        -Body $Body

    Write-Host "Message sent successfully!" -ForegroundColor Green
}
catch {
    Write-Host "Failed to send: $_" -ForegroundColor Red
}
