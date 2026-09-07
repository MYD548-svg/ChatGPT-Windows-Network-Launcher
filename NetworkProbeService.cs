using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace ChatGPTAntiBanLauncher
{
    public enum ProbeErrorKind
    {
        None,
        CertificateError,
        ProxyUnreachable,
        Timeout,
        HttpError,
        JsonParseError,
        GeneralNetworkError
    }

    public sealed class ProbeResult
    {
        public bool Success { get; set; }
        public string Ip { get; set; }
        public string Country { get; set; }
        public string IanaTimezone { get; set; }
        public TimeSpan? UtcOffset { get; set; }
        public ProbeErrorKind ErrorKind { get; set; }
        public string ErrorMessage { get; set; }
        public int GenerationToken { get; set; }

        public static ProbeResult Fail(ProbeErrorKind kind, string message, int generation)
        {
            return new ProbeResult
            {
                Success = false,
                ErrorKind = kind,
                ErrorMessage = message,
                GenerationToken = generation
            };
        }
    }

    [DataContract]
    internal class IpWhoisResponse
    {
        [DataMember(Name = "ip")] public string Ip { get; set; }
        [DataMember(Name = "country")] public string Country { get; set; }
        [DataMember(Name = "timezone")] public string Timezone { get; set; }
        [DataMember(Name = "timezone_gmt")] public string TimezoneGmt { get; set; }
        [DataMember(Name = "timezone_offset")] public string TimezoneOffset { get; set; }
        [DataMember(Name = "success")] public bool? Success { get; set; }
    }

    [DataContract]
    internal class IpInfoResponse
    {
        [DataMember(Name = "ip")] public string Ip { get; set; }
        [DataMember(Name = "country")] public string Country { get; set; }
        [DataMember(Name = "timezone")] public string Timezone { get; set; }
    }

    [DataContract]
    internal class IpSbResponse
    {
        [DataMember(Name = "ip")] public string Ip { get; set; }
        [DataMember(Name = "country")] public string Country { get; set; }
        [DataMember(Name = "timezone")] public string Timezone { get; set; }
        [DataMember(Name = "offset")] public int? Offset { get; set; }
    }

    public static class NetworkProbeService
    {
        private static int currentGeneration = 0;

        public static int NextGeneration()
        {
            return Interlocked.Increment(ref currentGeneration);
        }

        public static int CurrentGeneration
        {
            get { return currentGeneration; }
        }

        static NetworkProbeService()
        {
            ConfigureSecurityProtocols();
        }

        public static void ConfigureSecurityProtocols()
        {
            try
            {
                // Enable TLS 1.2 (3072) and TLS 1.3 (12288)
                // Note: Standard certificate validation is strictly maintained without any bypass callback
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
            }
            catch
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                }
                catch { }
            }
        }

        public static ProbeResult ProbeNode(ProxyConfig proxy, bool useProxy, int generation, int timeoutMs = 5000)
        {
            if (useProxy && (proxy == null || !proxy.IsValid()))
            {
                return ProbeResult.Fail(ProbeErrorKind.ProxyUnreachable, "代理配置无效或端口超出范围", generation);
            }

            // Strict HTTPS-only endpoints
            string[] endpoints = new string[]
            {
                "https://ipwhois.app/json/",
                "https://ipinfo.io/json",
                "https://api.ip.sb/geoip"
            };

            string lastError = null;
            ProbeErrorKind lastKind = ProbeErrorKind.GeneralNetworkError;

            for (int i = 0; i < endpoints.Length; i++)
            {
                // Early return if a newer generation request was triggered
                if (generation != currentGeneration)
                {
                    return ProbeResult.Fail(ProbeErrorKind.None, "请求已过期", generation);
                }

                string url = endpoints[i];
                try
                {
                    string json = FetchHttps(url, proxy, useProxy, timeoutMs);
                    if (string.IsNullOrEmpty(json)) continue;

                    ProbeResult parsed = ParseEndpointJson(i, json, generation);
                    if (parsed != null && parsed.Success)
                    {
                        return parsed;
                    }
                }
                catch (WebException wex)
                {
                    DiagnoseWebException(wex, out lastKind, out lastError);
                }
                catch (SocketException sex)
                {
                    lastKind = ProbeErrorKind.ProxyUnreachable;
                    lastError = string.Format("套接字连接异常 ({0}): {1}", sex.SocketErrorCode, sex.Message);
                }
                catch (Exception ex)
                {
                    lastKind = ProbeErrorKind.GeneralNetworkError;
                    lastError = ex.Message;
                }
            }

            string errSummary = string.IsNullOrEmpty(lastError) ? "无法连接任何已配置的 HTTPS GeoIP 接口" : lastError;
            return ProbeResult.Fail(lastKind, errSummary, generation);
        }

        private static string FetchHttps(string url, ProxyConfig proxy, bool useProxy, int timeoutMs)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ChatGPTLauncher/0.5";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            if (useProxy && proxy != null && proxy.IsValid())
            {
                request.Proxy = new WebProxy(proxy.Host, proxy.Port);
            }
            else
            {
                request.Proxy = null;
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static ProbeResult ParseEndpointJson(int endpointIndex, string json, int generation)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                if (endpointIndex == 0) // ipwhois.app
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(IpWhoisResponse));
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        IpWhoisResponse r = (IpWhoisResponse)ser.ReadObject(ms);
                        if (r != null && !string.IsNullOrEmpty(r.Ip) && !string.IsNullOrEmpty(r.Timezone))
                        {
                            TimeSpan parsedOffset;
                            TimeSpan? offset = null;
                            string dummyErr;
                            if (TimezoneHelper.ParseSecondsOffset(r.TimezoneOffset, out parsedOffset))
                            {
                                offset = parsedOffset;
                            }
                            else if (!string.IsNullOrEmpty(r.TimezoneGmt) && TimezoneHelper.ParseUserUtcOffset(r.TimezoneGmt, out parsedOffset, out dummyErr))
                            {
                                offset = parsedOffset;
                            }

                            return new ProbeResult
                            {
                                Success = true,
                                Ip = r.Ip,
                                Country = r.Country ?? "未知",
                                IanaTimezone = r.Timezone,
                                UtcOffset = offset,
                                GenerationToken = generation
                            };
                        }
                    }
                }
                else if (endpointIndex == 1) // ipinfo.io
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(IpInfoResponse));
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        IpInfoResponse r = (IpInfoResponse)ser.ReadObject(ms);
                        if (r != null && !string.IsNullOrEmpty(r.Ip) && !string.IsNullOrEmpty(r.Timezone))
                        {
                            return new ProbeResult
                            {
                                Success = true,
                                Ip = r.Ip,
                                Country = r.Country ?? "未知",
                                IanaTimezone = r.Timezone,
                                UtcOffset = null,
                                GenerationToken = generation
                            };
                        }
                    }
                }
                else if (endpointIndex == 2) // api.ip.sb
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(IpSbResponse));
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        IpSbResponse r = (IpSbResponse)ser.ReadObject(ms);
                        if (r != null && !string.IsNullOrEmpty(r.Ip) && !string.IsNullOrEmpty(r.Timezone))
                        {
                            TimeSpan parsedOffset;
                            TimeSpan? offset = null;
                            if (r.Offset.HasValue && TimezoneHelper.ParseSecondsOffset(r.Offset.Value, out parsedOffset))
                            {
                                offset = parsedOffset;
                            }
                            return new ProbeResult
                            {
                                Success = true,
                                Ip = r.Ip,
                                Country = r.Country ?? "未知",
                                IanaTimezone = r.Timezone,
                                UtcOffset = offset,
                                GenerationToken = generation
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return ProbeResult.Fail(ProbeErrorKind.JsonParseError, "响应内容解析错误: " + ex.Message, generation);
            }

            return null;
        }

        private static void DiagnoseWebException(WebException wex, out ProbeErrorKind kind, out string message)
        {
            if (wex.Status == WebExceptionStatus.TrustFailure ||
                wex.Status == WebExceptionStatus.SecureChannelFailure)
            {
                kind = ProbeErrorKind.CertificateError;
                message = "TLS/SSL 证书校验失败 (目标接口证书不受信任或被中间人拦截)";
                return;
            }

            if (wex.Status == WebExceptionStatus.ConnectFailure ||
                wex.Status == WebExceptionStatus.ProxyNameResolutionFailure)
            {
                kind = ProbeErrorKind.ProxyUnreachable;
                message = "代理服务不可达或连接被拒绝 (请检查本地代理客户端与端口)";
                return;
            }

            if (wex.Status == WebExceptionStatus.Timeout)
            {
                kind = ProbeErrorKind.Timeout;
                message = "连接超时 (节点网络拥堵或代理无响应)";
                return;
            }

            if (wex.Response is HttpWebResponse)
            {
                HttpWebResponse resp = (HttpWebResponse)wex.Response;
                kind = ProbeErrorKind.HttpError;
                message = string.Format("HTTP 状态码异常: {0} ({1})", (int)resp.StatusCode, resp.StatusDescription);
                return;
            }

            kind = ProbeErrorKind.GeneralNetworkError;
            message = wex.Message;
        }
    }
}
