using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Org.BouncyCastle.Crypto.TlsExt;

using NetcodeIO.NET.Utils;
using NetcodeIO.NET.Utils.IO;
using NetcodeIO.NET.Internal;

namespace NetcodeIO.NET
{
	/// <summary>
	/// Represents a remote client connected to a server
	/// </summary>
	public class RemoteClient
	{
		/// <summary>
		/// The unique ID of the client as assigned by the token server
		/// </summary>
		public ulong ClientID;

		/// <summary>
		/// The index as assigned by the server
		/// </summary>
		public uint ClientIndex;

		/// <summary>
		/// The remote endpoint of the client
		/// </summary>
		public EndPoint RemoteEndpoint;

		/// <summary>
		/// 256 bytes of arbitrary user data
		/// </summary>
		public byte[] UserData;

		internal bool Connected;
		internal bool Confirmed;

		internal NetcodeReplayProtection replayProtection;
		internal Server server;

		internal double lastResponseTime;
		internal int timeoutSeconds;

		public RemoteClient(Server server)
		{
			this.server = server;
		}

		/// <summary>
		/// Send a payload to this client
		/// </summary>
		public void SendPayload(byte[] payload, int payloadSize)
		{
			server.SendPayload(this, payload, payloadSize);
		}

		internal void Touch(double time)
		{
			lastResponseTime = time;
		}
	}

	/// <summary>
	/// Event handler for when a client connects to the server
	/// </summary>
	public delegate void RemoteClientConnectedEventHandler(RemoteClient client);

	/// <summary>
	/// Event handler for when a client disconnects from the server
	/// </summary>
	public delegate void RemoteClientDisconnectedEventHandler(RemoteClient client);

	/// <summary>
	/// Event handler for when payload packets are received from a connected client
	/// </summary>
	public delegate void RemoteClientMessageReceivedEventHandler(RemoteClient sender, byte[] payload, int payloadSize);

	/// <summary>
	/// Class for starting a Netcode.IO server and accepting connections from remote clients
	/// </summary>
	public sealed class Server
	{
		#region embedded types

		private struct usedConnectToken
		{
			public byte[] mac;
			public EndPoint endpoint;
			public double time;
		}

		#endregion

		#region Public fields/properties

		/// <summary>
		/// Event triggered when a remote client connects
		/// </summary>
		public event RemoteClientConnectedEventHandler OnClientConnected;

		/// <summary>
		/// Event triggered when a remote client disconnects
		/// </summary>
		public event RemoteClientDisconnectedEventHandler OnClientDisconnected;

		/// <summary>
		/// Event triggered when a payload is received from a remote client
		/// </summary>
		public event RemoteClientMessageReceivedEventHandler OnClientMessageReceived;

		/// <summary>
		/// Log level for messages
		/// </summary>
		public NetcodeLogLevel LogLevel = NetcodeLogLevel.Error;

		/// <summary>
		/// Gets the port this server is listening on (or -1 if not listening)
		/// </summary>
		public int Port
		{
			get
			{
				if (listenSocket == null)
					return -1;

				return listenSocket.BoundPort;
			}
		}

		/// <summary>
		/// Gets or sets the internal tickrate of the server in ticks per second. Value must be between 1 and 1000.
		/// </summary>
		public int Tickrate
		{
			get { return tickrate; }
			set
			{
				if (value < 1 || value > 1000) throw new ArgumentOutOfRangeException();
				tickrate = value;
			}
		}

		/// <summary>
		/// Gets the current number of connected clients
		/// </summary>
		public int NumConnectedClients
		{
			get
			{
				int connectedClients = 0;
				for (int i = 0; i < clientSlots.Length; i++)
					if (clientSlots[i] != null && clientSlots[i].Confirmed) connectedClients++;

				return connectedClients;
			}
		}

		#endregion

		#region Private fields

		internal bool debugIgnoreConnectionRequest = false;
		internal bool debugIgnoreChallengeResponse = false;

		private ISocketContext listenSocket;
		private IPEndPoint listenEndpoint;

		private bool isRunning = false;

		private ulong protocolID;

		private RemoteClient[] clientSlots;
		private int maxSlots;

		private usedConnectToken[] connectTokenHistory;
		private int maxConnectTokenEntries;

		private ulong nextSequenceNumber = 0;
		private ulong nextChallengeSequenceNumber = 0;

		private byte[] privateKey;
		private byte[] challengeKey;

		private EncryptionManager encryptionManager;

		private int tickrate;
		internal double time;

		private bool disposed = false;

		#endregion

		public Server(int maxSlots, int port, ulong protocolID, byte[] privateKey)
		{
			this.tickrate = 60;

			this.maxSlots = maxSlots;
			this.maxConnectTokenEntries = this.maxSlots * 8;
			this.connectTokenHistory = new usedConnectToken[this.maxConnectTokenEntries];
			initConnectTokenHistory();

			this.clientSlots = new RemoteClient[maxSlots];
			this.encryptionManager = new EncryptionManager(maxSlots);

			this.listenEndpoint = new IPEndPoint(IPAddress.Any, port);

			if (this.listenEndpoint.AddressFamily == AddressFamily.InterNetwork)
				this.listenSocket = new UDPSocketContext(AddressFamily.InterNetwork);
			else
				this.listenSocket = new UDPSocketContext(AddressFamily.InterNetworkV6);

			this.protocolID = protocolID;

			this.privateKey = privateKey;

			// generate a random challenge key
			this.challengeKey = new byte[32];
			KeyUtils.GenerateKey(this.challengeKey);
		}

		internal Server(ISocketContext socketContext, int maxSlots, int port, ulong protocolID, byte[] privateKey)
		{
			this.tickrate = 60;

			this.maxSlots = maxSlots;
			this.maxConnectTokenEntries = this.maxSlots * 8;
			this.connectTokenHistory = new usedConnectToken[this.maxConnectTokenEntries];
			initConnectTokenHistory();

			this.clientSlots = new RemoteClient[maxSlots];
			this.encryptionManager = new EncryptionManager(maxSlots);

			this.listenEndpoint = new IPEndPoint(IPAddress.Any, port);

			this.listenSocket = socketContext;

			this.protocolID = protocolID;

			this.privateKey = privateKey;

			// generate a random challenge key
			this.challengeKey = new byte[32];
			KeyUtils.GenerateKey(this.challengeKey);
		}

		#region Public Methods

		/// <summary>
		/// Start the server and listen for incoming connections
		/// </summary>
		public void Start()
		{
			Start(true);
		}

		internal void Start(bool autoTick)
		{
			if (disposed) throw new InvalidOperationException("Can't restart disposed server, please create a new server");

			resetConnectTokenHistory();

			this.listenSocket.Bind(this.listenEndpoint);
			isRunning = true;

			if (autoTick)
			{
				this.time = DateTime.Now.GetTotalSeconds();
				ThreadPool.QueueUserWorkItem(serverTick);
			}
		}

		/// <summary>
		/// Stop the server and disconnect any clients
		/// </summary>
		public void Stop()
		{
			disposed = true;

			disconnectAll();
			isRunning = false;
			this.listenSocket.Close();

			if (OnClientConnected != null)
			{
				foreach (var receiver in OnClientConnected.GetInvocationList())
					OnClientConnected -= (RemoteClientConnectedEventHandler)receiver;
			}

			if (OnClientDisconnected != null)
			{
				foreach (var receiver in OnClientDisconnected.GetInvocationList())
					OnClientDisconnected -= (RemoteClientDisconnectedEventHandler)receiver;
			}

			if (OnClientMessageReceived != null)
			{
				foreach (var receiver in OnClientMessageReceived.GetInvocationList())
					OnClientMessageReceived -= (RemoteClientMessageReceivedEventHandler)receiver;
			}
		}

		/// <summary>
		/// Send a payload to the remote client
		/// </summary>
		public void SendPayload(RemoteClient client, byte[] payload, int payloadSize)
		{
			sendPayloadToClient(client, payload, payloadSize);
		}

		/// <summary>
		/// Disconnect the remote client
		/// </summary>
		public void Disconnect(RemoteClient client)
		{
			disconnectClient(client);
		}

		#endregion

		#region Core

		double keepAlive = 0.0;
		internal void Tick(double time)
		{
			this.listenSocket.Pump();

			double dt = time - this.time;
			this.time = time;

			// send keep alive to clients 10 times per second
			keepAlive += dt;
			while (keepAlive >= 0.1)
			{
				keepAlive -= 0.1;
				for (int i = 0; i < clientSlots.Length; i++)
				{
					if (clientSlots[i] != null)
					{
						sendKeepAlive(clientSlots[i]);
					}
				}
			}

			// disconnect any clients which have not responded for timeout seconds
			for (int i = 0; i < clientSlots.Length; i++)
			{
				if (clientSlots[i] == null) continue;

				double timeRemaining = time - clientSlots[i].lastResponseTime;

				// timeout < 0 disables timeouts
				if (clientSlots[i].timeoutSeconds >= 0 &&
					(time - clientSlots[i].lastResponseTime) >= clientSlots[i].timeoutSeconds)
				{
					if (OnClientDisconnected != null)
						OnClientDisconnected(clientSlots[i]);

					log("Client {0} timed out", NetcodeLogLevel.Debug, clientSlots[i].RemoteEndpoint.ToString());
					disconnectClient(clientSlots[i]);
				}
			}

			// process datagram queue
			Datagram packet;
			while (listenSocket != null && listenSocket.Read(out packet))
			{
				processDatagram(packet.payload, packet.payloadSize, packet.sender);
				packet.Release();
			}
		}

		private void serverTick(Object stateInfo)
		{
			while (isRunning)
			{
				Tick(DateTime.Now.GetTotalSeconds());

				// sleep until next tick
				double tickLength = 1.0 / tickrate;
				Thread.Sleep((int)(tickLength * 1000));
			}
		}

		// process a received datagram
		private void processDatagram(byte[] payload, int size, EndPoint sender)
		{
			using (var reader = ByteArrayReaderWriter.Get(payload))
			{
				NetcodePacketHeader packetHeader = new NetcodePacketHeader();
				packetHeader.Read(reader);

				if (packetHeader.PacketType == NetcodePacketType.ConnectionRequest)
				{
					if (!debugIgnoreConnectionRequest)
						processConnectionRequest(reader, size, sender);
				}
				else
				{
					switch (packetHeader.PacketType)
					{
						case NetcodePacketType.ChallengeResponse:
							if (!debugIgnoreChallengeResponse)
								processConnectionResponse(reader, packetHeader, size, sender);
							break;
						case NetcodePacketType.ConnectionKeepAlive:
							processConnectionKeepAlive(reader, packetHeader, size, sender);
							break;
						case NetcodePacketType.ConnectionPayload:
							processConnectionPayload(reader, packetHeader, size, sender);
							break;
						case NetcodePacketType.ConnectionDisconnect:
							processConnectionDisconnect(reader, packetHeader, size, sender);
							break;
					}
				}
			}
		}

		#endregion

		#region Receive Packet Methods

		// check the packet against the client's replay protection, returning true if packet was replayed, false otherwise
		private bool checkReplay(NetcodePacketHeader header, EndPoint sender)
		{
			var cryptIdx = encryptionManager.FindEncryptionMapping(sender, time);
            if (cryptIdx == -1) {
                log("Replay protection failed to find encryption mapping", NetcodeLogLevel.Debug);
                return true;
            }

			var clientIndex = encryptionManager.GetClientID(cryptIdx);
			var client = clientSlots[clientIndex];

            if (client == null) {
                log("Replay protection failed to find client", NetcodeLogLevel.Debug);
                return true;
            }

			return client.replayProtection.AlreadyReceived(header.SequenceNumber);
		}

		// process an incoming disconnect message
		private void processConnectionDisconnect(ByteArrayReaderWriter reader, NetcodePacketHeader header, int size, EndPoint sender)
		{
			if (checkReplay(header, sender))
			{
				return;
			}

			// encryption mapping was not registered, so don't bother
			int cryptIdx = encryptionManager.FindEncryptionMapping(sender, time);
			if (cryptIdx == -1)
			{
				log("No crytpo key for sender", NetcodeLogLevel.Debug);
				return;
			}

			var decryptKey = encryptionManager.GetReceiveKey(cryptIdx);

			var disconnectPacket = new NetcodeDisconnectPacket() { Header = header };
			if (!disconnectPacket.Read(reader, size - (int)reader.ReadPosition, decryptKey, protocolID))
				return;

			// locate the client by endpoint and free their slot
			var clientIndex = encryptionManager.GetClientID(cryptIdx);

			var client = clientSlots[clientIndex];
            if (client == null) return;

			clientSlots[clientIndex] = null;

			// remove encryption mapping
			encryptionManager.RemoveEncryptionMapping(sender, time);

            // make sure all other clients still have their encryption mappings
            foreach (RemoteClient otherClient in clientSlots) {
                if (otherClient == null) continue;
                if (encryptionManager.FindEncryptionMapping(otherClient.RemoteEndpoint, time) == -1)
                    log("Encryption mapping removed wrong mapping!", NetcodeLogLevel.Debug);
            }

			// trigger client disconnect callback
			if (OnClientDisconnected != null)
				OnClientDisconnected(client);

			log("Client {0} disconnected", NetcodeLogLevel.Info, client.RemoteEndpoint);
		}

		// process an incoming payload
		private void processConnectionPayload(ByteArrayReaderWriter reader, NetcodePacketHeader header, int size, EndPoint sender)
		{
			if (checkReplay(header, sender))
			{
				return;
			}

			// encryption mapping was not registered, so don't bother
			int cryptIdx = encryptionManager.FindEncryptionMapping(sender, time);
			if (cryptIdx == -1)
			{
				log("No crytpo key for sender", NetcodeLogLevel.Debug);
				return;
			}

			// grab the decryption key and decrypt the packet
			var decryptKey = encryptionManager.GetReceiveKey(cryptIdx);

			var payloadPacket = new NetcodePayloadPacket() { Header = header };
			if (!payloadPacket.Read(reader, size - (int)reader.ReadPosition, decryptKey, protocolID))
				return;

			var clientIndex = encryptionManager.GetClientID(cryptIdx);
			var client = clientSlots[clientIndex];

			// trigger callback
			if (OnClientMessageReceived != null)
				OnClientMessageReceived(client, payloadPacket.Payload, payloadPacket.Length);

			payloadPacket.Release();
		}

		// process an incoming connection keep alive packet
		private void processConnectionKeepAlive(ByteArrayReaderWriter reader, NetcodePacketHeader header, int size, EndPoint sender)
		{
			if (checkReplay(header, sender))
			{
                log("Detected replay in keep-alive", NetcodeLogLevel.Debug);
				return;
			}

			// encryption mapping was not registered, so don't bother
			int cryptIdx = encryptionManager.FindEncryptionMapping(sender, time);
			if (cryptIdx == -1)
			{
				log("No crytpo key for sender", NetcodeLogLevel.Debug);
				return;
			}

			// grab the decryption key and decrypt the packet
			var decryptKey = encryptionManager.GetReceiveKey(cryptIdx);

			var keepAlivePacket = new NetcodeKeepAlivePacket() { Header = header };
			if (!keepAlivePacket.Read(reader, size - (int)reader.ReadPosition, decryptKey, protocolID))
			{
				log("Failed to decrypt", NetcodeLogLevel.Debug);
				return;
			}

			if (keepAlivePacket.ClientIndex >= maxSlots)
			{
				log("Invalid client index", NetcodeLogLevel.Debug);
				return;
			}

			var client = this.clientSlots[(int)keepAlivePacket.ClientIndex];
            if (client == null) {
                log("Failed to find client for endpoint", NetcodeLogLevel.Debug);
                return;
            }

			if (!client.RemoteEndpoint.Equals(sender))
			{
				log("Client does not match sender", NetcodeLogLevel.Debug);
				return;
			}

			if (!client.Confirmed)
			{
				// trigger callback
				if (OnClientConnected != null)
					OnClientConnected(client);

				log("Client {0} connected", NetcodeLogLevel.Info, client.RemoteEndpoint);
			}

			client.Confirmed = true;

			client.Touch(time);

			int idx = encryptionManager.FindEncryptionMapping(client.RemoteEndpoint, time);
			encryptionManager.Touch(idx, client.RemoteEndpoint, time);
		}

		// process an incoming connection response packet
		private void processConnectionResponse(ByteArrayReaderWriter reader, NetcodePacketHeader header, int size, EndPoint sender)
		{
			log("Got connection response", NetcodeLogLevel.Debug);

			// encryption mapping was not registered, so don't bother
			int cryptIdx = encryptionManager.FindEncryptionMapping(sender, time);
			if (cryptIdx == -1)
			{
				log("No crytpo key for sender", NetcodeLogLevel.Debug);
				return;
			}

			// grab the decryption key and decrypt the packet
			var decryptKey = encryptionManager.GetReceiveKey(cryptIdx);

			var connectionResponsePacket = new NetcodeConnectionChallengeResponsePacket() { Header = header };
			if (!connectionResponsePacket.Read(reader, size - (int)reader.ReadPosition, decryptKey, protocolID))
			{
				log("Failed to decrypt packet", NetcodeLogLevel.Debug);
				return;
			}

			var challengeToken = new NetcodeChallengeToken();
			if (!challengeToken.Read(connectionResponsePacket.ChallengeTokenBytes, connectionResponsePacket.ChallengeTokenSequence, challengeKey))
			{
				log("Failed to read challenge token", NetcodeLogLevel.Debug);
				connectionResponsePacket.Release();
				return;
			}

			// if a client from packet source IP / port is already connected, ignore the packet
			if (clientSlots.Any(x => x != null && x.RemoteEndpoint.Equals(sender)))
			{
				log("Client {0} already connected", NetcodeLogLevel.Debug, sender.ToString());
				return;
			}

			// if a client with the same id is already connected, ignore the packet
			if (clientSlots.Any(x => x != null && x.ClientID == challengeToken.ClientID))
			{
				log("Client ID {0} already connected", NetcodeLogLevel.Debug, challengeToken.ClientID);
				return;
			}

			// if the server is full, deny the connection
			int nextSlot = getFreeClientSlot();
			if (nextSlot == -1)
			{
				log("Server full, denying connection", NetcodeLogLevel.Info);
				denyConnection(sender, encryptionManager.GetSendKey(cryptIdx));
				return;
			}

			// assign the endpoint and client ID to a free client slot and set connected to true
			RemoteClient client = new RemoteClient(this);
			client.ClientID = challengeToken.ClientID;
			client.RemoteEndpoint = sender;
			client.Connected = true;
			client.replayProtection = new NetcodeReplayProtection();

			// assign timeout to client
			client.timeoutSeconds = encryptionManager.GetTimeoutSeconds(cryptIdx);

			// assign client to a free slot
			client.ClientIndex = (uint)nextSlot;
			this.clientSlots[nextSlot] = client;

			encryptionManager.SetClientID(cryptIdx, client.ClientIndex);

			// copy user data so application can make use of it, and set confirmed to false
			client.UserData = challengeToken.UserData;
			client.Confirmed = false;

			client.Touch(time);

			// respond with a connection keep alive packet
			sendKeepAlive(client);
		}

		// process an incoming connection request packet
		private void processConnectionRequest(ByteArrayReaderWriter reader, int size, EndPoint sender)
		{
			log("Got connection request", NetcodeLogLevel.Debug);

			var connectionRequestPacket = new NetcodeConnectionRequestPacket();
			if (!connectionRequestPacket.Read(reader, size - (int)reader.ReadPosition, protocolID))
			{
				log("Failed to read request", NetcodeLogLevel.Debug);
				return;
			}

			// expiration timestamp should be greater than current timestamp
			if (connectionRequestPacket.Expiration <= (ulong)Math.Truncate(time))
			{
				log("Connect token expired", NetcodeLogLevel.Debug);
				connectionRequestPacket.Release();
				return;
			}

			var privateConnectToken = new NetcodePrivateConnectToken();
			if (!privateConnectToken.Read(connectionRequestPacket.ConnectTokenBytes, privateKey, protocolID, connectionRequestPacket.Expiration, connectionRequestPacket.TokenSequenceNum))
			{
				log("Failed to read private token", NetcodeLogLevel.Debug);
				connectionRequestPacket.Release();
				return;
			}

			// if this server's public IP is not in the list of endpoints, packet is not valid
            /*
             * We run in Docker, so our listen endpoint (0.0.0.0:40000) won't ever appear in the connect
             * token's IP endpoint (<public IP>:40000). We don't really need this "security", since the
             * private keypair secures servers anyway.
             * 
			bool serverAddressInEndpoints = privateConnectToken.ConnectServers.Any(x => x.Endpoint.CompareEndpoint(this.listenEndpoint, this.Port));
			if (!serverAddressInEndpoints)
			{
				log("Server address not listen in token", NetcodeLogLevel.Debug);
				return;
			}
            */

			// if a client from packet source IP / port is already connected, ignore the packet
			if (clientSlots.Any(x => x != null && x.RemoteEndpoint.Equals(sender)))
			{
				log("Client {0} already connected", NetcodeLogLevel.Debug, sender.ToString());
				return;
			}

			// if a client with the same id as the connect token is already connected, ignore the packet
			if (clientSlots.Any(x => x != null && x.ClientID == privateConnectToken.ClientID))
			{
				log("Client ID {0} already connected", NetcodeLogLevel.Debug, privateConnectToken.ClientID);
				return;
			}

			// if the connect token has already been used by a different endpoint, ignore the packet
			// otherwise, add the token hmac and endpoint to the used token history
			// compares the last 16 bytes (token mac)
			byte[] token_mac = BufferPool.GetBuffer(Defines.MAC_SIZE);
			System.Array.Copy(connectionRequestPacket.ConnectTokenBytes, Defines.NETCODE_CONNECT_TOKEN_PRIVATE_BYTES - Defines.MAC_SIZE, token_mac, 0, Defines.MAC_SIZE);
			if (!findOrAddConnectToken(sender, token_mac, time))
			{
				log("Token already used", NetcodeLogLevel.Debug);
				BufferPool.ReturnBuffer(token_mac);
				return;
			}

			BufferPool.ReturnBuffer(token_mac);

			// if we have no slots, we need to respond with a connection denied packet
			var nextSlot = getFreeClientSlot();
			if (nextSlot == -1)
			{
				denyConnection(sender, privateConnectToken.ServerToClientKey);
				log("Server is full, denying connection", NetcodeLogLevel.Info);
				return;
			}

			// add encryption mapping for this endpoint as well as timeout
			// packets received from this endpoint are to be decrypted with the client-to-server key
			// packets sent to this endpoint are to be encrypted with the server-to-client key
			// if no messages are received within timeout from this endpoint, it is disconnected (unless timeout is negative)
			if (!encryptionManager.AddEncryptionMapping(sender,
				privateConnectToken.ServerToClientKey,
				privateConnectToken.ClientToServerKey,
				time,
				time + 30,
				privateConnectToken.TimeoutSeconds,
				0))
			{
				log("Failed to add encryption mapping", NetcodeLogLevel.Error);
				return;
			}

			// finally, send a connection challenge packet
			sendConnectionChallenge(privateConnectToken, sender);
		}

		#endregion

		#region Send Packet Methods

		// disconnect all clients
		private void disconnectAll()
		{
			for (int i = 0; i < clientSlots.Length; i++)
			{
				if (clientSlots[i] != null)
					disconnectClient(clientSlots[i]);
			}
		}

		// sends a disconnect packet to the client
		private void disconnectClient(RemoteClient client)
		{
			for (int i = 0; i < clientSlots.Length; i++)
			{
				if (clientSlots[i] == client)
				{
					clientSlots[i] = null;
					break;
				}
			}

			var cryptIdx = encryptionManager.FindEncryptionMapping(client.RemoteEndpoint, time);
			if (cryptIdx == -1)
			{
				return;
			}

			var cryptKey = encryptionManager.GetSendKey(cryptIdx);

			for (int i = 0; i < Defines.NUM_DISCONNECT_PACKETS; i++)
			{
				serializePacket(new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionDisconnect }, (writer) =>
				{
				}, client.RemoteEndpoint, cryptKey);
			}
		}

		// sends a connection denied packet to the endpoint
		private void denyConnection(EndPoint endpoint, byte[] cryptKey)
		{
			if (cryptKey == null)
			{
				var cryptIdx = encryptionManager.FindEncryptionMapping(endpoint, time);
				if (cryptIdx == -1) return;

				cryptKey = encryptionManager.GetSendKey(cryptIdx);
			}

			serializePacket(new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionDenied }, (writer) =>
			{
			}, endpoint, cryptKey);
		}

		// send a payload to a client
		private void sendPayloadToClient(RemoteClient client, byte[] payload, int payloadSize)
		{
			// if the client isn't confirmed, send a keep-alive packet before this packet
			if (!client.Confirmed)
				sendKeepAlive(client);

			var cryptIdx = encryptionManager.FindEncryptionMapping(client.RemoteEndpoint, time);
			if (cryptIdx == -1) return;

			var cryptKey = encryptionManager.GetSendKey(cryptIdx);

			serializePacket(new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionPayload }, (writer) =>
			{
				writer.WriteBuffer(payload, payloadSize);
			}, client.RemoteEndpoint, cryptKey);
		}

		// send a keep-alive packet to the client
		private void sendKeepAlive(RemoteClient client)
		{
			var packet = new NetcodeKeepAlivePacket() { ClientIndex = client.ClientIndex, MaxSlots = (uint)this.maxSlots };

			var cryptIdx = encryptionManager.FindEncryptionMapping(client.RemoteEndpoint, time);
			if (cryptIdx == -1) return;

			var cryptKey = encryptionManager.GetSendKey(cryptIdx);

			serializePacket(new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionKeepAlive }, (writer) =>
			{
				packet.Write(writer);
			}, client.RemoteEndpoint, cryptKey);
		}

		// sends a connection challenge packet to the endpoint
		private void sendConnectionChallenge(NetcodePrivateConnectToken connectToken, EndPoint endpoint)
		{
			log("Sending connection challenge", NetcodeLogLevel.Debug);

			var challengeToken = new NetcodeChallengeToken();
			challengeToken.ClientID = connectToken.ClientID;
			challengeToken.UserData = connectToken.UserData;

			ulong challengeSequence = nextChallengeSequenceNumber++;

			byte[] tokenBytes = BufferPool.GetBuffer(300);
			using (var tokenWriter = ByteArrayReaderWriter.Get(tokenBytes))
				challengeToken.Write(tokenWriter);

			byte[] encryptedToken = BufferPool.GetBuffer(300);
			int encryptedTokenBytes;

			try
			{
				encryptedTokenBytes = PacketIO.EncryptChallengeToken(challengeSequence, tokenBytes, challengeKey, encryptedToken);
			}
			catch
			{
				BufferPool.ReturnBuffer(tokenBytes);
				BufferPool.ReturnBuffer(encryptedToken);
				return;
			}

			var challengePacket = new NetcodeConnectionChallengeResponsePacket();
			challengePacket.ChallengeTokenSequence = challengeSequence;
			challengePacket.ChallengeTokenBytes = encryptedToken;

			var cryptIdx = encryptionManager.FindEncryptionMapping(endpoint, time);
			if (cryptIdx == -1) return;

			var cryptKey = encryptionManager.GetSendKey(cryptIdx);

			serializePacket(new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionChallenge }, (writer) =>
			{
				challengePacket.Write(writer);
			}, endpoint, cryptKey);

			BufferPool.ReturnBuffer(tokenBytes);
			BufferPool.ReturnBuffer(encryptedToken);
		}

		// encrypts a packet and sends it to the endpoint
		private void sendPacketToClient(NetcodePacketHeader packetHeader, byte[] packetData, int packetDataLen, EndPoint endpoint, byte[] key)
		{
			// assign a sequence number to this packet
			packetHeader.SequenceNumber = this.nextSequenceNumber++;

			// encrypt packet data
			byte[] encryptedPacketBuffer = BufferPool.GetBuffer(2048);

			int encryptedBytes = PacketIO.EncryptPacketData(packetHeader, protocolID, packetData, packetDataLen, key, encryptedPacketBuffer);

			int packetLen = 0;

			// write packet to byte array
			var packetBuffer = BufferPool.GetBuffer(2048);
			using (var packetWriter = ByteArrayReaderWriter.Get(packetBuffer))
			{
				packetHeader.Write(packetWriter);
				packetWriter.WriteBuffer(encryptedPacketBuffer, encryptedBytes);

				packetLen = (int)packetWriter.WritePosition;
			}

			// send packet
			listenSocket.SendTo(packetBuffer, packetLen, endpoint);

			BufferPool.ReturnBuffer(packetBuffer);
			BufferPool.ReturnBuffer(encryptedPacketBuffer);
		}

		private void serializePacket(NetcodePacketHeader packetHeader, Action<ByteArrayReaderWriter> write, EndPoint endpoint, byte[] key)
		{
			byte[] tempPacket = BufferPool.GetBuffer(2048);
			int writeLen = 0;
			using (var writer = ByteArrayReaderWriter.Get(tempPacket))
			{
				write(writer);
				writeLen = (int)writer.WritePosition;
			}

			sendPacketToClient(packetHeader, tempPacket, writeLen, endpoint, key);
			BufferPool.ReturnBuffer(tempPacket);
		}

		#endregion

		#region Misc Util Methods

		// find or add a connect token entry
		// intentional constant time worst case search
		private bool findOrAddConnectToken(EndPoint address, byte[] mac, double time)
		{
			int matchingTokenIndex = -1;
			int oldestTokenIndex = -1;
			double oldestTokenTime = 0.0;

			for (int i = 0; i < connectTokenHistory.Length; i++)
			{
				var token = connectTokenHistory[i];
				if (MiscUtils.CompareHMACConstantTime(token.mac, mac))
					matchingTokenIndex = i;

				if (oldestTokenIndex == -1 || token.time < oldestTokenTime)
				{
					oldestTokenTime = token.time;
					oldestTokenIndex = i;
				}
			}

			// if no entry is found with the mac, this is a new connect token. replace the oldest token entry.
			if (matchingTokenIndex == -1)
			{
				connectTokenHistory[oldestTokenIndex].time = time;
				connectTokenHistory[oldestTokenIndex].endpoint = address;
				Buffer.BlockCopy(mac, 0, connectTokenHistory[oldestTokenIndex].mac, 0, mac.Length);
				return true;
			}

			// allow connect tokens we have already seen from the same address
			if (connectTokenHistory[matchingTokenIndex].endpoint.Equals(address))
				return true;

			return false;
		}

		// reset connect token history
		private void resetConnectTokenHistory()
		{
			for (int i = 0; i < connectTokenHistory.Length; i++)
			{
				connectTokenHistory[i].endpoint = null;
				Array.Clear(connectTokenHistory[i].mac, 0, 16);
				connectTokenHistory[i].time = -1000.0;
			}
		}

		// initialize connect token history
		private void initConnectTokenHistory()
		{
			for (int i = 0; i < connectTokenHistory.Length; i++)
			{
				connectTokenHistory[i].mac = new byte[Defines.MAC_SIZE];
			}

			resetConnectTokenHistory();
		}

		/// <summary>
		/// allocate the next free client slot
		/// </summary>
		private int getFreeClientSlot()
		{
			for (int i = 0; i < maxSlots; i++)
			{
				if (clientSlots[i] == null)
					return i;
			}

			return -1;
		}

		private void log(string log, NetcodeLogLevel logLevel)
		{
			if (logLevel > this.LogLevel)
				return;

			Console.WriteLine(log);
		}

		private void log(string log, NetcodeLogLevel logLevel, params object[] args)
		{
			if (logLevel > this.LogLevel)
				return;

			Console.WriteLine(string.Format(log, args));
		}

		#endregion
	}
}
