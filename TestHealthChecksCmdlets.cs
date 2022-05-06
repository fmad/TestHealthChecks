using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TestHealthChecks
{
    [Cmdlet(VerbsDiagnostic.Test, "HealthChecks")]
    public class TestHealthChecksCmdlets: Cmdlet
    {
        private bool _exitRequested = false;

        [Parameter]
        public int TimeoutMiliSecs { get; set; } = 2000;

        [Parameter]
        public int WaitMiliSecs { get; set; } = 100;

        [Parameter]
        public int RepeatHeader { get; set; } = 20;

        [Parameter]
        public SwitchParameter NoReturn { get; set; }

        [Parameter]
        public Checks[] Checks { get; set; }

        [Parameter]
        public int?[] HideCodes { get; set; }
        
        [Parameter]
        public SwitchParameter HideDuplicates { get; set; }

        [Parameter]
        public String ErrorLogFile { get; set; }

        [Parameter]
        public String OutputLogFile { get; set; }

        private const int TimeOutCode = -100;

        protected override void StopProcessing()
        {
            base.StopProcessing();
            _exitRequested = true;
        }
        protected override void EndProcessing()
        {
            base.EndProcessing();
            Stopwatch stopWatch = new Stopwatch();
            var columns = Checks.Select(x => x.Name).ToList();
            columns.Sort();
            WriteVerbose($"Test-HealthChecks w/ {TimeoutMiliSecs}ms Timeout, {WaitMiliSecs}ms Wait, {(NoReturn.IsPresent?"-NoReturn":"")} and {columns.Count} checks to test...");
            var    results      = new ConcurrentDictionary<String, Int32>();
            bool   repeat       = NoReturn.IsPresent;
            int    repeatHeader = RepeatHeader;
            string previousLine = null; // Used with "HideDuplicates" to avoid repeating like lines and forcing good results out of screen
            string currentLine  = null; // To hold the "current" line that would be output
            try {
                do {
                    stopWatch.Reset();
                    stopWatch.Start();
                    var timestamp = new DateTimeOffset(DateTime.UtcNow).ToString("yyyy/MM/dd HH:mm:ss.fff");
                    PSObject obj = new PSObject();
                    obj.Properties.Add(new PSNoteProperty("Timestamp", timestamp));

                    using (var finished = new CountdownEvent(1)) {
                        foreach (var test in columns) {
                            var check    = Checks.Where(x => x.Name == test).FirstOrDefault();  // Used to capture the loop variable in the lambda expression.
                            var address  = check.Address;
                            var proto    = check.Proto;
                            var port     = check.Port;
                            var hostname = check.Hostname;
                            switch(proto) {
                                case ProtoEnum.http:
                                case ProtoEnum.https:
                                    finished.AddCount(); // Indicate that there is another work item.
                                    ThreadPool.QueueUserWorkItem(
                                        (state) => {
                                            try {
                                                results[check.Name] = InvokeRequest($"{proto}://{address}", hostname);
                                            } finally {
                                                finished.Signal(); // Signal that the work item is complete.
                                            }
                                        }, null
                                    );
                                    break;
                                case ProtoEnum.icmp:
                                    // Need to start a thread with an ICMP check
                                    break;
                                case ProtoEnum.tcp:
                                    // Need to start a thread with an ICMP check
                                    finished.AddCount();
                                    ThreadPool.QueueUserWorkItem(
                                        (state) => {
                                            try
                                            {
                                                results[check.Name] = TestTcpConnect(address,port);
                                            } finally {
                                                finished.Signal();
                                            }
                                        }, null
                                    );
                                    break;
                                default:
                                    throw new Exception("Unhandled Protocol");
                            }
                        }
                        finished.Signal(); // Signal that queueing is complete.
                        finished.Wait();   // Wait for all work items to complete.
                    }
                    currentLine = "";
                    foreach (var k in columns) {
                        int code = Int32.Parse(results[k].ToString());
                        string codeTxt = code != TimeOutCode ? code.ToString() : "---";
                        if (null != HideCodes && HideCodes.Contains(code)) {
                            codeTxt = "";
                        }
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

        protected int TestTcpConnect(string Address, int Port) {
            TcpClient tcpClient   = new TcpClient();
            // future optimization: resolve address at startup
            IPAddress ipAddress = Dns.GetHostEntry(Address).AddressList[0];
            IPEndPoint ipEndPoint = new IPEndPoint(ipAddress, Port);
            var result  = tcpClient.BeginConnect(Address, Port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(TimeoutMiliSecs));
            if (success) {
                tcpClient.EndConnect(result);
                return 200;
            } else
            {
                return -1;
            }
        }

        protected int InvokeRequest(string URL, string Hostname)
        {
            var request = (HttpWebRequest)WebRequest.Create(URL);
            request.Timeout = TimeoutMiliSecs;
            if(null != Hostname) {
                request.Headers.Add("Host", Hostname);
            }
            request.AllowAutoRedirect = false;
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            int statusCode;
            try {
                var response = request.GetResponse();
                statusCode   = (int)((HttpWebResponse)response)?.StatusCode;
                response.Close();
                return statusCode;
            } catch (WebException e) {
                if (e.Status == WebExceptionStatus.ProtocolError) {
                    if (e.Response is HttpWebResponse response) {
                        return (int)response.StatusCode;
                    } else {
                        return TimeOutCode - 1; // no http status code available
                    }
                } else if (e.Status == WebExceptionStatus.Timeout) {
                    return TimeOutCode; // Timeout
                } else {
                    return -(int)e.Status; // Other exception
                }
            } catch (Exception e2) {
                using (StreamWriter w = File.AppendText("C:\\Debug\\err.txt")) {
                    w.WriteLine(e2.Message);
                }
                return TimeOutCode - 2; // Other exception
            }
        }
    }

    public enum ProtoEnum
    {
        https = 0,
        http,
        icmp,
        tcp
    }
    public class Checks
    {
        public string    Name;
        public string    Address;
        public string    Hostname;
        public int       Port;
        public ProtoEnum Proto;
    }
}
