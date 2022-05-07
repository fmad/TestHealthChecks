using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using DnsClient;

namespace TestHealthChecks
{
    [Cmdlet(VerbsDiagnostic.Test, "HealthChecks")]
    public class TestHealthChecksCmdlets: Cmdlet
    {
        #region Parameters
        [Parameter(Mandatory=true )] public Checks[]        Checks           { get; set; }
        [Parameter(Mandatory=false)] public String          ErrorLogFile     { get; set; }
        [Parameter(Mandatory=false)] public SwitchParameter HideDuplicates   { get; set; }
        [Parameter(Mandatory=false)] public SwitchParameter NoReturn         { get; set; }
        [Parameter(Mandatory=false)] public String          OutputLogFile    { get; set; }
        [Parameter(Mandatory=false)] public int             RepeatHeader     { get; set; } = 20;
        [Parameter(Mandatory=false)] public bool            SuccessErrorOnly { get; set; } = false;
        [Parameter(Mandatory=false)] public int             TimeoutMiliSecs  { get; set; } = 2000;
        [Parameter(Mandatory=false)] public int             WaitMiliSecs     { get; set; } = 100;
        #endregion

        private bool _exitRequested = false;
        private const int TimeOutCode = -100;

        protected override void StopProcessing()
        {
            base.StopProcessing();
            _exitRequested = true;
        }
        protected override void EndProcessing()
        {
            base.EndProcessing();
            var    dnsClient    = new LookupClient();
            var    stopWatch    = new Stopwatch();
            var    results      = new ConcurrentDictionary<String, Int32>();
            bool   repeat       = NoReturn.IsPresent;
            int    repeatHeader = RepeatHeader;
            string previousLine = null; // Used with "HideDuplicates" to avoid repeating like lines and forcing good results out of screen
            string currentLine  = null; // To hold the "current" line that would be output
            var columns = Checks.Select(x => x.Name).ToList();
            columns.Sort();
            WriteVerbose($"Test-HealthChecks w/ {TimeoutMiliSecs}ms Timeout, {WaitMiliSecs}ms Wait, {(NoReturn.IsPresent ? "-NoReturn" : "")} and {columns.Count} checks to test...");
            try {
                do {
                    stopWatch.Reset();
                    stopWatch.Start();
                    var timestamp = new DateTimeOffset(DateTime.UtcNow).ToString("yyyy/MM/dd HH:mm:ss.fff");
                    var obj       = new PSObject();
                    obj.Properties.Add(new PSNoteProperty("Timestamp", timestamp));
                    using (var finished = new CountdownEvent(1)) {
                        foreach (var test in columns) {
                            var check    = Checks.Where(x => x.Name == test).FirstOrDefault();  // Used to capture the loop variable in the lambda expression.
                            var address  = check.Address;
                            var proto    = check.Proto;
                            var port     = check.Port;
                            var hostname = check.Hostname;
                            var expected = check.Expected;
                            finished.AddCount(); // Indicate that there is another work item.
                            ThreadPool.QueueUserWorkItem(
                                (state) => {
                                    try {
                                        switch (proto) {
                                            case ProtoEnum.http:
                                            case ProtoEnum.https:
                                                results[check.Name] = InvokeRequest($"{proto}://{address}", hostname, expected);
                                                break;
                                            case ProtoEnum.icmp:
                                                results[check.Name] = TestIcmpConnect(address, expected);
                                                break;
                                            case ProtoEnum.tcp:
                                                results[check.Name] = TestTcpConnect(address, port, expected);
                                                break;
                                            case ProtoEnum.dns:
                                                results[check.Name] = 0; // Do some DNS checks here...
                                                break;
                                            default:
                                                throw new Exception("Unhandled Protocol");
                                        }
                                    } finally {
                                        finished.Signal(); // Signal that the work item is complete.
                                    }
                                }, null
                            );
                        }
                        finished.Signal(); // Signal that queueing is complete.
                        finished.Wait();   // Wait for all work items to complete.
                    }
                    currentLine = "";
                    foreach (var k in columns) {
                        int code = Int32.Parse(results[k].ToString());
                        string codeTxt = code != TimeOutCode ? code.ToString() : "---";
                        currentLine += (currentLine.Length > 0 ? "," : "") + codeTxt;
                        obj.Properties.Add(new PSNoteProperty(k.ToString(), codeTxt));
                        if (ErrorLogFile != null && codeTxt.Trim() != "" && codeTxt != "200") {
                            using (StreamWriter w = File.AppendText(ErrorLogFile)) {
                                w.WriteLine($"{timestamp} {k} = {codeTxt}");
                            }
                        }
                    }
                    // Return an object except if HideDuplicates && currentLine == previousLine
                    if (!(HideDuplicates && String.Equals(currentLine, previousLine))) {
                        WriteObject(sendToPipeline: obj, enumerateCollection: false);
                        if (repeat) {
                            if (--repeatHeader <= 0) {
                                PSObject objH = new PSObject();
                                objH.Properties.Add(new PSNoteProperty("Timestamp", "====/==/== ==:==:==.=="));
                                foreach (var k in columns) {
                                    objH.Properties.Add(new PSNoteProperty(k.ToString(), k.ToString()));
                                }
                                WriteObject(sendToPipeline: objH, enumerateCollection: false);
                                repeatHeader = RepeatHeader;
                            }
                        }
                    }
                    previousLine = currentLine;
                    stopWatch.Stop();
                    if (this.WaitMiliSecs > 0) {
                        int sleep = this.WaitMiliSecs - (int)stopWatch.ElapsedMilliseconds;
                        if (sleep > 0) {
                            Thread.Sleep(sleep);
                        }
                    };
                } while (repeat && !_exitRequested);
            }
            catch (PipelineStoppedException) {
                _exitRequested = true;
            }
        }

        protected int TestIcmpConnect(string Address, List<string>Expected)
        {
            int result;
            try {
                var pingSender = new Ping();
                var reply = pingSender.Send(Address, TimeoutMiliSecs);
                result = (int)reply.Status;
            } catch (Exception) {
                result = (int)IPStatus.Unknown;
            }
            if (SuccessErrorOnly && Expected?.Count > 0) {
                if (Expected.Contains(result.ToString())) {
                    result = 0;
                } else {
                    result = -1;
                }
            }
            return result;
        }

        protected int TestTcpConnect(string Address, int Port, List<string> Expected) {
            var tcpClient  = new TcpClient();  // future optimization: resolve address at startup
            var ipAddress  = Dns.GetHostEntry(Address).AddressList[0];
            var ipEndPoint = new IPEndPoint(ipAddress, Port);
            var conResult  = tcpClient.BeginConnect(Address, Port, null, null);
            var success    = conResult.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(TimeoutMiliSecs));
            int result;
            if (success) {
                tcpClient.EndConnect(conResult);
                result = 0;
            } else {
                result = -1;
            }
            if (SuccessErrorOnly && Expected?.Count > 0) {
                if (Expected.Contains(result.ToString())) {
                    result = 0;
                } else {
                    result = -1;
                }
            }
            return result;
        }

        protected int InvokeRequest(string URL, string Hostname, List<string> Expected)
        {
            var request = (HttpWebRequest)WebRequest.Create(URL);
            request.Timeout = TimeoutMiliSecs;
            if(null != Hostname) {
                request.Headers.Add("Host", Hostname);
            }
            request.AllowAutoRedirect = false;
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            int statusCode;
            int result;
            try {
                var response = request.GetResponse();
                statusCode   = (int)((HttpWebResponse)response)?.StatusCode;
                response.Close();
                result = statusCode;
            } catch (WebException e) {
                if (e.Status == WebExceptionStatus.ProtocolError) {
                    if (e.Response is HttpWebResponse response) {
                        result = (int)response.StatusCode;
                    } else {
                        result = TimeOutCode - 1; // no http status code available
                    }
                } else if (e.Status == WebExceptionStatus.Timeout) {
                    result = TimeOutCode; // Timeout
                } else {
                    result = -(int)e.Status; // Other exception
                }
            } catch (Exception e2) {
                using (StreamWriter w = File.AppendText("C:\\Debug\\err.txt")) {
                    w.WriteLine(e2.Message);
                }
                result = TimeOutCode - 2; // Other exception
            }
            if (SuccessErrorOnly && Expected?.Count > 0) {
                if (Expected.Contains(result.ToString())) {
                    result = 0;
                } else {
                    result = -1;
                }
            }
            return result;
        }
    }

    public enum ProtoEnum
    {
        https = 0,
        http,
        icmp,
        tcp,
        dns
    }
    public class Checks
    {
        public string        Name;
        public string        Address;
        public string        Hostname;
        public string        Query;
        public int           Port;
        public ProtoEnum     Proto;
        public List<string>  Expected;
    }
}
