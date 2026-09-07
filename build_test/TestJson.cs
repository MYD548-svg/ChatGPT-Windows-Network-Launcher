using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

[DataContract]
public class TestConfig
{
    [DataMember(Name = "mode")]
    public string Mode { get; set; }
    [DataMember(Name = "proxy_port")]
    public int ProxyPort { get; set; }
}

public class TestMain
{
    public static void Main()
    {
        TestConfig cfg = new TestConfig { Mode = "iana", ProxyPort = 7897 };
        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(TestConfig));
        using (MemoryStream ms = new MemoryStream())
        {
            ser.WriteObject(ms, cfg);
            ms.Position = 0;
            using (StreamReader sr = new StreamReader(ms))
            {
                Console.WriteLine(sr.ReadToEnd());
            }
        }
    }
}
