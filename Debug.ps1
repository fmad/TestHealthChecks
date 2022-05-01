Import-Module ".\TestHealthChecks.dll" -verbose
$VerbosePreference = 'SilentlyContinue'
   
$sites = @{}
$hosts = @{}
foreach( $code in '200','201','301','302','400','403','404','500','521' ) {
    $sites["T:$code"] = "httpstat.us/$code"
}
$sites["T:IP"] = (Resolve-DnsName -Name www.google.com -Type A).IP4Address
$hosts["T:IP"] = "www.google.com"
Write-Host "Querying:"
$sites.GetEnumerator() | Sort-Object Name
$now = (Get-Date).ToString('yyyy.MM.dd-HH.mm.ss')
$logFolder = 'C:\Log'
if ( -not (Test-Path -Path $logFolder) ) {
    New-Item -Path $logFolder -ItemType Directory | Out-Null
}
$errFile = "$logFolder\Err-debug-$now.txt"
Write-Host "Errors will be saved to $errFile"
$parms = @{
    NoReturn        = $true
    Urls            = $sites
    Hostnames       = $hosts
    HideCodes       = @(200)
    ErrorLogFile    = $errFile
    HideDuplicates  = $false
    TimeoutMiliSecs = 1000
    WaitMiliSecs    = 1000
}
Test-HealthChecks @parms | Format-Table *
