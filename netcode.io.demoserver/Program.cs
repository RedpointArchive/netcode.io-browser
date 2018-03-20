using NetcodeIO.NET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace netcode.io.demoserver
{
    class Program
    {
        static readonly byte[] _privateKey = new byte[]
        {
            0x60, 0x6a, 0xbe, 0x6e, 0xc9, 0x19, 0x10, 0xea,
            0x9a, 0x65, 0x62, 0xf6, 0x6f, 0x2b, 0x30, 0xe4,
            0x43, 0x71, 0xd6, 0x2c, 0xd1, 0x99, 0x27, 0x26,
            0x6b, 0x3c, 0x60, 0xf4, 0xb7, 0x15, 0xab, 0xa1,
        };

        static bool running = true;
        
        static string serverAddress = "127.0.0.1";

        static string serverPort = "40000";

        static string httpAddress = "127.0.0.1";

        static string httpPort = "8080";

        static bool isServerIpv4 = true;

        static byte[][] lastPacketMessage;

        static int maxClients = 128;

        static Random random = new Random();

        static void Main(string[] args)
        {
            var nonInteractive = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--non-interactive")
                {
                    nonInteractive = true;
                }
                else if (args[i] == "--server-address")
                {
                    serverAddress = args[i + 1];

                    if (serverAddress == "FROM_ENV")
                    {
                        serverAddress = Environment.GetEnvironmentVariable("SERVER_ADDRESS");
                    }

                    i++;
                }
                else if (args[i] == "--server-port")
                {
                    serverPort = args[i + 1];
                    i++;
                }
                else if (args[i] == "--http-address")
                {
                    httpAddress = args[i + 1];
                    i++;
                }
                else if (args[i] == "--http-port")
                {
                    httpPort = args[i + 1];
                    i++;
                }
            }

            try
            {
                var ip = IPAddress.Parse(serverAddress);
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    isServerIpv4 = false;
                }
            }
            catch { }
            
            // Start web server.
            WebServer ws = new WebServer(SendResponse, "http://" + httpAddress + ":" + httpPort + "/");
            ws.Run();

            // Run netcode.io server in another thread.
            var netcodeThread = new Thread(NetcodeServer);
            netcodeThread.IsBackground = !nonInteractive;
            netcodeThread.Start();

            Console.WriteLine("netcode.io demo server started, open up http://" + httpAddress + ":" + httpPort + "/ to try it!");
            if (!nonInteractive)
            {
                Console.ReadKey();
                running = false;
                ws.Stop();
            }
        }

        private static void NetcodeServer()
        {
            var server = new Server(
                32,
                "0.0.0.0",
                int.Parse(serverPort),
                0x1122334455667788L, 
                _privateKey);

            var clients = new Dictionary<RemoteClient, byte[]>();

            server.OnClientConnected += (client) =>
            {
                clients.Add(client, null);
            };
            server.OnClientDisconnected += (client) =>
            {
                clients.Remove(client);
            };
            server.OnClientMessageReceived += (client, payload, payloadSize) =>
            {
                if (!clients.ContainsKey(client))
                {
                    clients.Add(client, null);
                }

                var b = new byte[payloadSize];
                Array.Copy(payload, b, payloadSize);
                clients[client] = b;
            };
            
            server.Start();

            lastPacketMessage = new byte[maxClients][];

            while (running)
            {
                foreach (var kv in clients.ToArray())
                {
                    if (kv.Value != null)
                    {
                        server.SendPayload(kv.Key, kv.Value, kv.Value.Length);
                        clients[kv.Key] = null;
                    }
                }

                Thread.Sleep(1000 / 60);
            }
            
            server.Stop();
        }

        public static Tuple<int, byte[]> SendResponse(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.Url.AbsolutePath == "/")
            {
                response.ContentType = "text/html";

                var asmPath = Assembly.GetExecutingAssembly().Location;
                var indexPath = Path.Combine(new FileInfo(asmPath).DirectoryName, "index.htm");
                using (var reader = new StreamReader(indexPath))
                {
                    return new Tuple<int, byte[]>(200, Encoding.UTF8.GetBytes(reader.ReadToEnd().Replace("__PROTOCOL__", isServerIpv4 ? "ipv4" : "ipv6")));
                }
            }

            if (request.Url.AbsolutePath == "/basic")
            {
                response.ContentType = "text/html";

                var asmPath = Assembly.GetExecutingAssembly().Location;
                var indexPath = Path.Combine(new FileInfo(asmPath).DirectoryName, "basic.htm");
                using (var reader = new StreamReader(indexPath))
                {
                    return new Tuple<int, byte[]>(200, Encoding.UTF8.GetBytes(reader.ReadToEnd().Replace("__PROTOCOL__", isServerIpv4 ? "ipv4" : "ipv6")));
                }
            }

            if (request.Url.AbsolutePath == "/token")
            {
                response.ContentType = "text/plain";

                var buffer = new byte[sizeof(UInt64)];
                random.NextBytes(buffer);
                var clientId = BitConverter.ToUInt64(buffer, 0);

                Console.WriteLine($"Generating connect token for {serverAddress}:{serverPort}");
                var tokenFactory = new TokenFactory(
                    0x1122334455667788L,
                    _privateKey);
                var token = tokenFactory.GenerateConnectToken(
                    new[] { new IPEndPoint(IPAddress.Parse(serverAddress), int.Parse(serverPort)) },
                    30,
                    15,
                    0,
                    clientId,
                    new byte[0]);
                return new Tuple<int, byte[]>(200, Encoding.UTF8.GetBytes(Convert.ToBase64String(token)));
            }

            if (request.Url.AbsolutePath == "/netcode-support.xpi")
            {
                response.ContentType = "application/x-xpinstall";

                var asmPath = Assembly.GetExecutingAssembly().Location;
                var xpiPath = Path.Combine(new FileInfo(asmPath).DirectoryName, "netcodeio_support_self_dist-0.1.5-fx.xpi");
                using (var reader = new FileStream(xpiPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var b = new byte[reader.Length];
                    reader.Read(b, 0, b.Length);
                    return new Tuple<int, byte[]>(200, b);
                }
            }

            return new Tuple<int, byte[]>(404, Encoding.UTF8.GetBytes("404 not found"));
        }
    }
}
