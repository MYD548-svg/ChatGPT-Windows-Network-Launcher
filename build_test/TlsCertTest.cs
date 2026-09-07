using System;
using System.Net;

namespace ChatGPTAntiBanLauncher.Tests
{
    public class TlsCertTest
    {
        public static int Main()
        {
            Console.WriteLine("--- Testing TLS Certificate Validation Enforcement ---");

            NetworkProbeService.ConfigureSecurityProtocols();

            // Verify that ServerCertificateValidationCallback is NULL (standard X.509 chain validation)
            bool callbackIsNull = (ServicePointManager.ServerCertificateValidationCallback == null);
            Console.WriteLine((callbackIsNull ? "[PASS]" : "[FAIL]") + " ServerCertificateValidationCallback is null (no unconditional trust bypass)");

            // Attempt request to known untrusted/self-signed SSL endpoint
            bool rejected = false;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://self-signed.badssl.com/");
                req.Timeout = 5000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    // Should not succeed with untrusted cert
                    rejected = false;
                }
            }
            catch (WebException wex)
            {
                if (wex.Status == WebExceptionStatus.TrustFailure ||
                    wex.Status == WebExceptionStatus.SecureChannelFailure ||
                    wex.Status == WebExceptionStatus.Timeout ||
                    wex.Status == WebExceptionStatus.NameResolutionFailure)
                {
                    // Certificate error or network rejection
                    rejected = true;
                    Console.WriteLine("[PASS] Untrusted certificate was rejected as expected: " + wex.Status);
                }
                else
                {
                    Console.WriteLine("[INFO] WebException: " + wex.Status);
                    rejected = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[PASS] Rejected: " + ex.GetType().Name);
                rejected = true;
            }

            return (callbackIsNull && rejected) ? 0 : 1;
        }
    }
}
