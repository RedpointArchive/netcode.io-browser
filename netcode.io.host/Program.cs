using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace netcode.io.host
{
    class Program
    {
        static Dictionary<int, ManagedClient> _clients;
        static Random _random;
        static BinaryWriter _outWriter;
        static object _writerLock;

        const string HelperVersion = "0.1.0";

        class ManagedClient
        {
            public Client client;

            public double time;

            public int tickRate;

            public Thread thread;

            public bool shouldRun;

            public ConcurrentQueue<byte[]> pendingPackets;
        }

        const int TypeCreateClient = 101;
        const int TypeSetClientTickRate = 102;
        const int TypeConnectClient = 103;
        const int TypeSendPacket = 104;
        const int TypeReceivePacket = 105;
        const int TypeGetClientState = 106;
        const int TypeDestroyClient = 107;
        const int TypeClientDestroyed = 108;
        const int TypeCheckPresence = 109;
        const int TypeClientStateChanged = 110;

        const int ResultClientCreated = 201;
        const int ResultSuccess = 202;
        const int ResultError = 203;
        const int ResultErrorInternal = 204;

        static void WriteMessage(object o)
        {
            var json = JsonConvert.SerializeObject(o);
            var utf8Bytes = Encoding.UTF8.GetBytes(json);

            lock (_writerLock)
            {
                _outWriter.Write((Int32)utf8Bytes.Length);
                _outWriter.Write(utf8Bytes);
                _outWriter.Flush();
            }
        }

        static void Main(string[] args)
        {
            _clients = new Dictionary<int, ManagedClient>();
            _random = new Random();
            _outWriter = new BinaryWriter(Console.OpenStandardOutput());
            _writerLock = new object();

            bool quit = false;

            Console.CancelKeyPress += (sender, a) =>
            {
                quit = true;
            };
            
            NetcodeLibrary.SetLogLevel(NetcodeLogLevel.None);

            using (var reader = new BinaryReader(Console.OpenStandardInput()))
            {
                while (!quit)
                {
                    try
                    { 
                        var size = reader.ReadInt32();
                        var bytes = reader.ReadBytes(size);

                        var json = Encoding.UTF8.GetString(bytes);
                        var messageArray = JsonConvert.DeserializeObject<JArray>(json);
                    
                        var messageType = messageArray[0].Value<int>();
                        var messageId = messageArray[1].Value<int>();

                        try
                        { 
                            switch (messageType)
                            {
                                case TypeCheckPresence:
                                    {
                                        WriteMessage(new JArray
                                        {
                                            JValue.FromObject(ResultSuccess),
                                            JValue.FromObject(messageId),
                                            JValue.FromObject(HelperVersion),
                                        });
                                        break;
                                    }
                                case TypeCreateClient:
                                    {
                                        var id = _random.Next();
                                        while (_clients.ContainsKey(id))
                                        {
                                            id = _random.Next();
                                        }
                                        var isIpV6 = false;
                                        if (messageArray.Count >= 3)
                                        {
                                            isIpV6 = messageArray[2].Value<bool>();
                                        }
                                        _clients[id] = new ManagedClient
                                        {
                                            client = new Client(isIpV6 ? "::" : "0.0.0.0", 0),
                                            tickRate = 60,
                                            shouldRun = true,
                                            time = 0,
                                            pendingPackets = new ConcurrentQueue<byte[]>(),
                                        };
                                        _clients[id].thread = new Thread(ClientRun);
                                        _clients[id].thread.IsBackground = true;
                                        _clients[id].thread.Start(id);
                                        WriteMessage(new JArray
                                        {
                                            JValue.FromObject(ResultClientCreated),
                                            JValue.FromObject(messageId),
                                            JValue.FromObject(id),
                                        });
                                        break;
                                    }
                                case TypeSetClientTickRate:
                                    {
                                        var id = messageArray[2].Value<int>();
                                        var tickRate = messageArray[3].Value<int>();
                                        _clients[id].tickRate = tickRate;
                                        WriteMessage(new JArray
                                        {
                                            JValue.FromObject(ResultSuccess),
                                            JValue.FromObject(messageId),
                                        });
                                        break;
                                    }
                                case TypeConnectClient:
                                    {
                                        var id = messageArray[2].Value<int>();
                                        var connectTokenBase64 = messageArray[3].Value<string>();
                                        _clients[id].client.Connect(Convert.FromBase64String(connectTokenBase64));
                                        WriteMessage(new JArray
                                        {
                                            JValue.FromObject(ResultSuccess),
                                            JValue.FromObject(messageId),
                                        });
                                        break;
                                    }
                                case TypeSendPacket:
                                    {
                                        var id = messageArray[2].Value<int>();
                                        var packetData = messageArray[3].Value<string>();
                                        _clients[id].pendingPackets.Enqueue(Convert.FromBase64String(packetData));
                                        WriteMessage(new JArray
                                        {
                                            JValue.FromObject(ResultSuccess),
                                            JValue.FromObject(messageId),
                                        });
                                        break;
                                    }
                                case TypeGetClientState:
                                    {
                                        var id = messageArray[2].Value<int>();
                                        string state;
                                        switch (_clients[id].client.State)
                                        {
                                            case ClientState.Connected:
                                                state = "connected";
                                                break;
                                            case ClientState.ConnectionDenied:
                                                state = "connectionDenied";
                                                break;
                                            case ClientState.ConnectionRequestTimeout:
                                                state = "connectionRequestTimeout";
                                                break;
                                            case ClientState.ConnectionResponseTimeout:
                                                state = "connectionResponseTimeout";
                                                break;
                                            case ClientState.ConnectionTimedOut:
                                                state = "connectionTimedOut";
                                                break;
                                            case ClientState.ConnectTokenExpired:
                                                state = "connectTokenExpired";
                                                break;
                                            case ClientState.Disconnected:
                                                state = "disconnected";
                                                break;
                                            case ClientState.InvalidConnectToken:
                                                state = "invalidConnectToken";
                                                break;
                                            case ClientState.SendingConnectionRequest:
                                                state = "sendingConnectionRequest";
                                                break;
                                            case ClientState.SendingConnectionResponse:
                                                state = "sendingConnectionResponse";
                                                break;
                                            default:
                                                state = "unknown";
                                                break;
                                        }
                                        WriteMessage(new JArray
                                        {
                                            JValue.FromObject(ResultSuccess),
                                            JValue.FromObject(messageId),
                                            JValue.FromObject(state),
                                        });
                                        break;
                                    }
                                case TypeDestroyClient:
                                    {
                                        var id = messageArray[2].Value<int>();
                                        _clients[id].shouldRun = false;
                                        WriteMessage(new JArray
                                        {
                                            JValue.FromObject(ResultSuccess),
                                            JValue.FromObject(messageId),
                                        });
                                        break;
                                    }
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteMessage(new JArray
                            {
                                JValue.FromObject(ResultErrorInternal),
                                JValue.FromObject(messageId),
                                JValue.FromObject(ex.ToString()),
                            });
                        }
                    }
                    catch (EndOfStreamException)
                    {
                        // Requested to close by browser process.
                        return;
                    }
                }
            }
        }

        private static void ClientRun(object o)
        {
            var id = (int)o;
            var managedClient = _clients[id];

            // keep track of client state so we can notify application of state changes
            var prevState = managedClient.client.State;
			
            while (managedClient.shouldRun)
            {
                managedClient.client.Update(managedClient.time);
				
                if (managedClient.client.State != prevState)
                {
                    prevState = managedClient.client.State;
                    PostStateChange(id);
                }

                if (managedClient.client.State == ClientState.Connected)
                {
                    byte[] packetData;
                    while (managedClient.pendingPackets.TryDequeue(out packetData))
                    {
                        managedClient.client.SendPacket(packetData);
                    }
                }

                while (true)
                {
                    var packet = managedClient.client.ReceivePacket();
                    if (packet == null)
                    {
                        break;
                    }

                    WriteMessage(new JArray
                    {
                        JValue.FromObject(TypeReceivePacket),
                        JValue.FromObject(id),
                        JValue.FromObject(Convert.ToBase64String(packet)),
                    });
                }

                var deltaTime = 1.0 / (double)managedClient.tickRate;
                NetcodeLibrary.Sleep(deltaTime);
                managedClient.time += deltaTime;
            }

            WriteMessage(new JArray
            {
                JValue.FromObject(TypeClientDestroyed),
                JValue.FromObject(id),
            });

            _clients.Remove(id);

            managedClient.client.Dispose();
        }

        private static void PostStateChange( int id )
        {
            string state;
            switch (_clients[id].client.State)
            {
                case ClientState.Connected:
                    {
                        state = "connected";
                        break;
                    }
                case ClientState.ConnectionDenied:
                    {
                        state = "connectionDenied";
                        break;
                    }
                case ClientState.ConnectionRequestTimeout:
                    {
                        state = "connectionRequestTimeout";
                        break;
                    }
                case ClientState.ConnectionResponseTimeout:
                    {
                        state = "connectionResponseTimeout";
                        break;
                    }
                case ClientState.ConnectionTimedOut:
                    {
                        state = "connectionTimedOut";
                        break;
                    }
                case ClientState.ConnectTokenExpired:
                    {
                        state = "connectTokenExpired";
                        break;
                    }
                case ClientState.Disconnected:
                    {
                        state = "disconnected";
                        break;
                    }
                case ClientState.InvalidConnectToken:
                    {
                        state = "invalidConnectToken";
                        break;
                    }
                case ClientState.SendingConnectionRequest:
                    {
                        state = "sendingConnectionRequest";
                        break;
                    }
                case ClientState.SendingConnectionResponse:
                    {
                        state = "sendingConnectionResponse";
                        break;
                    }
                default:
                    {
                        state = "unknown";
                        break;
                    }
            }

            WriteMessage(new JArray
            {
                JValue.FromObject(TypeClientStateChanged),
                JValue.FromObject(id),
                JValue.FromObject(state),
            });
        }
    }
}
