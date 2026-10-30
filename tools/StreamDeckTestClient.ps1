# Test client for the Stream Deck server of OpenTuner (#88): prints the state messages and sends the commands given as arguments.
#
#   .\StreamDeckTestClient.ps1                          only listens (Ctrl+C to end)
#   .\StreamDeckTestClient.ps1 -Send '{"cmd":"select","tuner":2}'
#   .\StreamDeckTestClient.ps1 -Send '{"cmd":"mute","tuner":1}','{"cmd":"view","view":"grid"}' -Seconds 3
#
# Commands: select, mute, tab ("tab": Properties|Expert|TS Info|Special|Chip), view ("view": large|only|grid), volume ("delta": +-n).
# "tuner" is 1 based, left out = the selected tuner.
param(
    [int]$Port = 8765,
    [string[]]$Send = @(),
    [int]$Seconds = 0
)

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ws.ConnectAsync([Uri]"ws://127.0.0.1:$Port/", [Threading.CancellationToken]::None).Wait()
Write-Host "connected to 127.0.0.1:$Port"

function Send-Text([string]$text) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($text)
    $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(,$bytes)
    $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait()
    Write-Host ">> $text"
}

foreach ($message in $Send) { Send-Text $message }

$buffer = New-Object byte[] 8192
$end = if ($Seconds -gt 0) { (Get-Date).AddSeconds($Seconds) } else { [DateTime]::MaxValue }
while ($ws.State -eq 'Open' -and (Get-Date) -lt $end) {
    $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(,$buffer)
    $task = $ws.ReceiveAsync($segment, [Threading.CancellationToken]::None)
    while (-not $task.IsCompleted -and (Get-Date) -lt $end) { Start-Sleep -Milliseconds 50 }
    if (-not $task.IsCompleted) { break }
    $result = $task.Result
    if ($result.MessageType -eq 'Close') { break }
    Write-Host ("<< " + [Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count))
}

if ($ws.State -eq 'Open') { try { $ws.CloseAsync('NormalClosure', '', [Threading.CancellationToken]::None).Wait(1000) | Out-Null } catch {} }
