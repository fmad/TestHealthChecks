Import-Module ".\TestHealthChecks.dll" -verbose
$VerbosePreference = 'SilentlyContinue'
$TestICMPOnly      = $true

$checks = @()
if ($TestICMPOnly) {
    foreach ($range in 124..127) {
        $checks += @{
            Name         = "TTL $range"
            Proto        = 'icmp'
            Address      = "185.236.$range.251"
            ShowHopCount = $true
        }
#        $checks += @{
#            Name         = "ICMP $range"
#            Proto        = 'icmp'
#            Address      = "185.236.$range.251"
#            ShowHopCount = $false
#        }
    }
    $checks += @{
        Name         = "8.8.8.8"
        Proto        = 'icmp'
        Address      = "8.8.8.8"
        ShowHopCount = $true
    }
} else {
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
        Address = "httpstat.us/404"
        Expected= 200
    }
    $checks += @{
        Name    = 'T:IP'
        Proto   = 'https'
        Address = '142.250.179.228' # (Resolve-DnsName -Name www.google.com -Type A).IP4Address
        Hostname= 'www.google.com'
    }
    $checks += @{
        Name    = 'TCP80'
        Proto   = 'tcp'
        Address = '142.250.179.228' # (Resolve-DnsName -Name www.google.com -Type A).IP4Address
        Port    = 80
        Expected= 0
    }
    $checks += @{
        Name    = 'TCP81'
        Proto   = 'tcp'
        Address = '142.250.179.228' # (Resolve-DnsName -Name www.google.com -Type A).IP4Address
        Port    = 81
        Expected= -1
    }
    $checks += @{
        Name    = 'ICMP1'
        Proto   = 'icmp'
        Address = '142.250.179.228' # (Resolve-DnsName -Name www.google.com -Type A).IP4Address
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
    SuccessErrorOnly= -not $TestICMPOnly # $true = Only show success (0) or error (-1) if the result code is in the Expected list, otherwise display the code
    HideDuplicates  = $false # $true
    NoReturn        = $true
    TimeoutMiliSecs = if ($TestICMPOnly) {  900 } else {  5000 }
    WaitMiliSecs    = if ($TestICMPOnly) { 2000 } else { 10000 }
}
Test-HealthChecks @parms | Format-Table *

<# ICMP Status codes

    //     The ICMP echo request failed for an unknown reason.
    Unknown = -1,

    //     The ICMP echo request succeeded; an ICMP echo reply was received. When you get
    //     this status code, the other System.Net.NetworkInformation.PingReply properties
    //     contain valid data.
    Success = 0,

    //     The ICMP echo request failed because the network that contains the destination
    //     computer is not reachable.
    DestinationNetworkUnreachable = 11002,

    //     The ICMP echo request failed because the destination computer is not reachable.
    DestinationHostUnreachable = 11003,

    //     The ICMPv6 echo request failed because contact with the destination computer
    //     is administratively prohibited. This value applies only to IPv6.
    DestinationProhibited = 11004,

    //     The ICMP echo request failed because the destination computer that is specified
    //     in an ICMP echo message is not reachable, because it does not support the packet's
    //     protocol. This value applies only to IPv4. This value is described in IETF RFC
    //     1812 as Communication Administratively Prohibited.
    DestinationProtocolUnreachable = 11004,

    //     The ICMP echo request failed because the port on the destination computer is
    //     not available.
    DestinationPortUnreachable = 11005,

    //     The ICMP echo request failed because of insufficient network resources.
    NoResources = 11006,

    //     The ICMP echo request failed because it contains an invalid option.
    BadOption = 11007,

    //     The ICMP echo request failed because of a hardware error.
    HardwareError = 11008,

    //     The ICMP echo request failed because the packet containing the request is larger
    //     than the maximum transmission unit (MTU) of a node (router or gateway) located
    //     between the source and destination. The MTU defines the maximum size of a transmittable
    //     packet.
    PacketTooBig = 11009,

    //     The ICMP echo Reply was not received within the allotted time. The default time
    //     allowed for replies is 5 seconds. You can change this value using the Overload:System.Net.NetworkInformation.Ping.Send
    //     or Overload:System.Net.NetworkInformation.Ping.SendAsync methods that take a
    //     timeout parameter.
    TimedOut = 11010,

    //     The ICMP echo request failed because there is no valid route between the source
    //     and destination computers.
    BadRoute = 11012,

    //     The ICMP echo request failed because its Time to Live (TTL) value reached zero,
    //     causing the forwarding node (router or gateway) to discard the packet.
    TtlExpired = 11013,

    //     The ICMP echo request failed because the packet was divided into fragments for
    //     transmission and all of the fragments were not received within the time allotted
    //     for reassembly. RFC 2460 (available at www.ietf.org) specifies 60 seconds as
    //     the time limit within which all packet fragments must be received.
    TtlReassemblyTimeExceeded = 11014,

    //     The ICMP echo request failed because a node (router or gateway) encountered problems
    //     while processing the packet header. This is the status if, for example, the header
    //     contains invalid field data or an unrecognized option.
    ParameterProblem = 11015,

    //     The ICMP echo request failed because the packet was discarded. This occurs when
    //     the source computer's output queue has insufficient storage space, or when packets
    //     arrive at the destination too quickly to be processed.
    SourceQuench = 11016,

    //     The ICMP echo request failed because the destination IP address cannot receive
    //     ICMP echo requests or should never appear in the destination address field of
    //     any IP datagram. For example, calling Overload:System.Net.NetworkInformation.Ping.Send
    //     and specifying IP address "000.0.0.0" returns this status.
    BadDestination = 11018,

    //     The ICMP echo request failed because the destination computer that is specified
    //     in an ICMP echo message is not reachable; the exact cause of problem is unknown.
    DestinationUnreachable = 11040,

    //     The ICMP echo request failed because its Time to Live (TTL) value reached zero,
    //     causing the forwarding node (router or gateway) to discard the packet.
    TimeExceeded = 11041,

    //     The ICMP echo request failed because the header is invalid.
    BadHeader = 11042,

    //     The ICMP echo request failed because the Next Header field does not contain a
    //     recognized value. The Next Header field indicates the extension header type (if
    //     present) or the protocol above the IP layer, for example, TCP or UDP.
    UnrecognizedNextHeader = 11043,

    //     The ICMP echo request failed because of an ICMP protocol error.
    IcmpError = 11044,

    //     The ICMP echo request failed because the source address and destination address
    //     that are specified in an ICMP echo message are not in the same scope. This is
    //     typically caused by a router forwarding a packet using an interface that is outside
    //     the scope of the source address. Address scopes (link-local, site-local, and
    //     global scope) determine where on the network an address is valid.
    DestinationScopeMismatch = 11045
#>