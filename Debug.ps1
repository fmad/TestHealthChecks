Import-Module ".\TestHealthChecks.dll" -verbose
$VerbosePreference = 'SilentlyContinue'

$checks = @()
foreach( $code in '200','201','301','302','400','403','404','500','521' ) {
    $checks += @{
        Name    = "T:$code"
        Proto   = 'https'
        Address = "httpstat.us/$code"
    }
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
}
$checks += @{
    Name    = 'TCP81'
    Proto   = 'tcp'
    Address = (Resolve-DnsName -Name www.google.com -Type A).IP4Address
    Port    = 81
}
$checks += @{
    Name    = 'ICMP1'
    Proto   = 'icmp'
    Address = (Resolve-DnsName -Name www.google.com -Type A).IP4Address
}
$checks += @{
    Name    = 'ICMP2'
    Proto   = 'icmp'
    Address = '255.255.255.255'
}
$now = (Get-Date).ToString('yyyy.MM.dd-HH.mm.ss')
$logFolder = 'C:\Log'
if ( -not (Test-Path -Path $logFolder) ) {
    New-Item -Path $logFolder -ItemType Directory | Out-Null
}
$errFile = "$logFolder\Err-debug-$now.txt"
Write-Host "Errors will be saved to $errFile"
$parms = @{
    NoReturn        = $true
    Checks          = $checks
    HideCodes       = $null # @(200)
    ErrorLogFile    = $errFile
    HideDuplicates  = $false
    TimeoutMiliSecs =  5000
    WaitMiliSecs    = 10000
}
Test-HealthChecks @parms | Format-Table *
