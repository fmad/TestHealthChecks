Import-Module ".\TestHealthChecks.dll" -verbose
$VerbosePreference = 'SilentlyContinue'

$checks = @()
foreach( $code in '200','201','301','302','400','403','404','500','521' ) {
    $checks += @{
        Name    = "T:$code"
        Proto   = 'https'
        Address = "httpstat.us/$code"
        Expected= @($code)
    }
}
$checks += @{
    Name    = "T:404b"
    Proto   = 'https'
    Address = "httpstat.us/$code"
    Expected= 200
}
$checks += @{
    Name    = 'T:IP'
    Proto   = 'https'
    Address = (Resolve-DnsName -Name www.google.com -Type A).IP4Address
    Hostname= 'www.google.com'
}
$checks += @{
    Name    = 'TCP80'
    Proto   = 'tcp'
    Address = (Resolve-DnsName -Name www.google.com -Type A).IP4Address
    Port    = 80
    Expected= 0
}
$checks += @{
    Name    = 'TCP81'
    Proto   = 'tcp'
    Address = (Resolve-DnsName -Name www.google.com -Type A).IP4Address
    Port    = 81
    Expected= -1
}
$checks += @{
    Name    = 'ICMP1'
    Proto   = 'icmp'
    Address = (Resolve-DnsName -Name www.google.com -Type A).IP4Address
    Expected= 0
}
$checks += @{
    Name    = 'ICMP2'
    Proto   = 'icmp'
    Address = '255.255.255.255'
    Expected= 11010
}
$checks += @{
    Name    = 'DNS1'
    Proto   = 'dns'
    Address = '8.8.8.8'
    Query   = 'MX:madruga.eu'
    Expected= @('alt1.aspmx.l.google.com','alt2.aspmx.l.google.com','aspmx.l.google.com','aspmx2.googlemail.com','aspmx3.googlemail.com')
}
$now = (Get-Date).ToString('yyyy.MM.dd-HH.mm.ss')
$logFolder = 'C:\Log'
if ( -not (Test-Path -Path $logFolder) ) {
    New-Item -Path $logFolder -ItemType Directory | Out-Null
}
$errFile = "$logFolder\Err-debug-$now.txt"
Write-Host "Errors will be saved to $errFile"
$parms = @{
    Checks          = $checks
    ErrorLogFile    = $errFile
    SuccessErrorOnly= $true     # Only show success (0) or error (-1) if the result code is in the Expected list, otherwise display the code
    HideDuplicates  = $false
    NoReturn        = $true
    TimeoutMiliSecs =  5000
    WaitMiliSecs    = 10000
}
Test-HealthChecks @parms | Format-Table *
