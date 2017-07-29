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
            NetcodeLibrary.SetLogLevel(NetcodeLogLevel.Info);

            double time = 0f;
            double deltaTime = 1.0 / 60.0;

            var server = new Server(
                serverAddress + ":" + serverPort,
                0x1122334455667788L, 
                _privateKey,
                0);
            
            server.Start(NetcodeLibrary.GetMaxClients());

            lastPacketMessage = new byte[NetcodeLibrary.GetMaxClients()][];

            while (running)
            {
                server.Update(time);
                
                for (var clientIndex = 0; clientIndex < NetcodeLibrary.GetMaxClients(); clientIndex++)
                {
                    if (server.ClientConnected(clientIndex) && lastPacketMessage[clientIndex] != null)
                    {
                        server.SendPacket(clientIndex, lastPacketMessage[clientIndex]);
                        lastPacketMessage[clientIndex] = null;
                    }
                }

                for (var clientIndex = 0; clientIndex < NetcodeLibrary.GetMaxClients(); clientIndex++)
                {
                    while (true)
                    {
                        var packet = server.ReceivePacket(clientIndex);
                        if (packet == null)
                        {
                            break;
                        }

                        lastPacketMessage[clientIndex] = packet;
                    }
                }

                NetcodeLibrary.Sleep(deltaTime);

                time += deltaTime;
            }
            
            server.Dispose();
        }

        public static string SendResponse(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.Url.AbsolutePath == "/")
            {
                response.ContentType = "text/html";

                var asmPath = Assembly.GetExecutingAssembly().Location;
                var indexPath = Path.Combine(new FileInfo(asmPath).DirectoryName, "index.htm");
                using (var reader = new StreamReader(indexPath))
                {
                    return reader.ReadToEnd().Replace("__PROTOCOL__", isServerIpv4 ? "ipv4" : "ipv6");
                }
            }

            if (request.Url.AbsolutePath == "/basic")
            {
                response.ContentType = "text/html";

                var asmPath = Assembly.GetExecutingAssembly().Location;
                var indexPath = Path.Combine(new FileInfo(asmPath).DirectoryName, "basic.htm");
                using (var reader = new StreamReader(indexPath))
                {
                    return reader.ReadToEnd().Replace("__PROTOCOL__", isServerIpv4 ? "ipv4" : "ipv6");
                }
            }

            if (request.Url.AbsolutePath == "/token")
            {
                response.ContentType = "text/plain";
                
                var clientId = NetcodeLibrary.GetRandomUInt64();
                var token = NetcodeLibrary.GenerateConnectTokenFromPrivateKey(
                    new[] { serverAddress + ":" + serverPort },
                    30,
                    clientId,
                    0x1122334455667788L,
                    0,
                    _privateKey);
                return Convert.ToBase64String(token);
            }

            return "404 not found";
        }
    }
}
