Import-Module ".\TestHealthChecks.dll" -verbose
$VerbosePreference = 'SilentlyContinue'

$checks = @()
foreach( $code in '200','201','301','302','400','403','404','500','521' ) {
    $checks += @{
        Name    = "T:$code";
        Proto   = 'https';
        Address = "httpstat.us/$code"
    }
}
$checks += @{
    Name    = 'T:IP';
    Proto   = 'https';
    Address = (Resolve-DnsName -Name www.google.com -Type A).IP4Address
    Hostname= 'www.google.com'
}
Write-Host "Querying:"
$checks.Name
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
    TimeoutMiliSecs = 1000
    WaitMiliSecs    = 1000
}
Test-HealthChecks @parms | Format-Table *
