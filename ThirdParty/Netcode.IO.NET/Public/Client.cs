using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

using NetcodeIO.NET.Utils;
using NetcodeIO.NET.Utils.IO;
using NetcodeIO.NET.Internal;

namespace NetcodeIO.NET
{
	/// <summary>
	/// State of a client object
	/// </summary>
	public enum ClientState
	{
		/// <summary>
		/// The connect token has expired
		/// </summary>
		ConnectTokenExpired = -6,

		/// <summary>
		/// The connect token is invalid
		/// </summary>
		InvalidConnectToken = -5,

		/// <summary>
		/// Connection timed out while connected to a server
		/// </summary>
		ConnectionTimedOut = -4,

		/// <summary>
		/// Connection timed out while sending challenge response
		/// </summary>
		ChallengeResponseTimedOut = -3,

		/// <summary>
		/// Connection timed out while sending connection request
		/// </summary>
		ConnectionRequestTimedOut = -2,

		/// <summary>
		/// The connection request was denied by the server
		/// </summary>
		ConnectionDenied = -1,

		/// <summary>
		/// Client is not currently connected
		/// </summary>
		Disconnected = 0,

		/// <summary>
		/// Client is currently sending a connection request
		/// </summary>
		SendingConnectionRequest = 1,

		/// <summary>
		/// Client is currently sending a connection response to a server
		/// </summary>
		SendingChallengeResponse = 2,

		/// <summary>
		/// The client is connected to a server
		/// </summary>
		Connected = 3,
	}

	/// <summary>
	/// Event handler for when a client's state changes
	/// </summary>
	public delegate void ClientStateChangedHandler(ClientState state);

	/// <summary>
	/// Event handler for when payloads are received from the server
	/// </summary>
	public delegate void ClientMessageReceivedHandler(byte[] payload, int payloadSize);

	/// <summary>
	/// Class for connecting to and communicating with Netcode.IO servers
	/// </summary>
	public sealed class Client
	{
		#region Public Fields/Properties

		/// <summary>
		/// Gets or sets the internal tickrate of the client in ticks per second. Value must be between 1 and 1000
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
		/// Gets the current state of the client
		/// </summary>
		public ClientState State
		{
			get { return state; }
		}

		/// <summary>
		/// Gets the client index as assigned by a server, or -1 if not connected to a server
		/// </summary>
		public int ClientIndex
		{
			get
			{
				if (state == ClientState.Connected)
					return (int)clientIndex;
				else
					return -1;
			}
		}

		/// <summary>
		/// Gets the maximum client slots on the server, or -1 if not connected to a server
		/// </summary>
		public int MaxSlots
		{
			get
			{
				if (state == ClientState.Connected)
					return (int)maxSlots;
				else
					return -1;
			}
		}

		/// <summary>
		/// Gets the port the client socket is bound to, or -1 if not bound
		/// </summary>
		public int Port
		{
			get
			{
				if (socket == null)
					return -1;

				return socket.BoundPort;
			}
		}

		/// <summary>
		/// Event triggered when client state changes
		/// </summary>
		public event ClientStateChangedHandler OnStateChanged;

		/// <summary>
		/// Event triggered when a payload is received from the server
		/// </summary>
		public event ClientMessageReceivedHandler OnMessageReceived;

		#endregion

		#region Private fields

		private int tickrate = 60;

		private ISocketContext socket;

		private bool isRunning = false;

		private ClientState state;
		private ClientState pendingDisconnectState;

		private double lastResponseTime;
		internal double time;
		internal double dt;

		private uint clientIndex;
		private uint maxSlots;
		private byte[] clientToServerKey;
		private byte[] serverToClientKey;

		private ulong nextPacketSequence = 0;

		private NetcodePublicConnectToken connectToken;
		private NetcodeConnectionChallengeResponsePacket challengeResponse;
		private Queue<EndPoint> connectServers = new Queue<EndPoint>();
		private EndPoint currentServerEndpoint;

		private NetcodeReplayProtection replayProtection;

		private Func<EndPoint, ISocketContext> socketFactory;

		#endregion

		public Client()
		{
			state = ClientState.Disconnected;
			pendingDisconnectState = ClientState.Disconnected;

			replayProtection = new NetcodeReplayProtection();
			replayProtection.Reset();

			socketFactory = (endpoint) =>
			{
				var socket = new UDPSocketContext(endpoint.AddressFamily);
				var socketEndpoint = new IPEndPoint(endpoint.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
				socket.Bind(socketEndpoint);

				return socket;
			};
		}

		internal Client(Func<EndPoint, ISocketContext> socketFactory)
		{
			state = ClientState.Disconnected;
			pendingDisconnectState = ClientState.Disconnected;

			replayProtection = new NetcodeReplayProtection();
			replayProtection.Reset();

			this.socketFactory = socketFactory;
		}

		#region Public Methods

		/// <summary>
		/// Disconnect the client from the server, if connected
		/// </summary>
		public void Disconnect()
		{
			disconnect(ClientState.Disconnected);
		}

		/// <summary>
		/// Connect to a server using the connect token
		/// </summary>
		public void Connect(byte[] connectToken)
		{
			Connect(connectToken, true);
		}

		internal void Connect(byte[] connectToken, bool autoTick)
		{
			if (state != ClientState.Disconnected)
				throw new InvalidOperationException();

			keepAliveTimer = 0.0;

			connectServers.Clear();
			replayProtection.Reset();

			if (connectToken.Length != Defines.NETCODE_CONNECT_TOKEN_PUBLIC_BYTES)
			{
				changeState(ClientState.InvalidConnectToken);
				return;
			}

			NetcodePublicConnectToken tokenData = new NetcodePublicConnectToken();
			using (var reader = ByteArrayReaderWriter.Get(connectToken))
			{
				if (!tokenData.Read(reader))
				{
					changeState(ClientState.InvalidConnectToken);
					return;
				}
			}

			if (tokenData.CreateTimestamp >= tokenData.ExpireTimestamp)
			{
				changeState(ClientState.InvalidConnectToken);
				return;
			}

			clientToServerKey = tokenData.ClientToServerKey;
			serverToClientKey = tokenData.ServerToClientKey;

			foreach (var server in tokenData.ConnectServers)
				connectServers.Enqueue(server.Endpoint);

			this.connectToken = tokenData;
			this.state = ClientState.SendingConnectionRequest;

			// bind socket, spin up threads, and start trying to connect
			isRunning = true;

			currentServerEndpoint = connectServers.Dequeue();
			createSocket(currentServerEndpoint);

			if (autoTick)
			{
				this.time = DateTime.Now.GetTotalSeconds();
				ThreadPool.QueueUserWorkItem(clientTick);
			}
		}

		/// <summary>
		/// Send a payload to the server
		/// </summary>
		public void Send(byte[] payload, int payloadSize)
		{
			if (state != ClientState.Connected)
				throw new InvalidOperationException();

			serializePacket(new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionPayload }, (writer) =>
			{
				writer.WriteBuffer(payload, payloadSize);
			}, clientToServerKey);
		}

		#endregion

		#region Core

		private void createSocket(EndPoint endpoint)
		{
			this.socket = socketFactory(endpoint);
		}

		private void stopSocket()
		{
			if (socket != null)
			{
				socket.Close();
				socket = null;
			}
		}

		private void disconnect(ClientState disconnectState)
		{
			if (state == ClientState.Connected)
				sendDisconnect();

			isRunning = false;
			pendingDisconnectState = ClientState.Disconnected;
            
            changeState(disconnectState);

            stopSocket();

			connectServers.Clear();

			currentServerEndpoint = null;
			nextPacketSequence = 0;
			serverToClientKey = null;
		}

		internal void Tick(double time)
		{
			if (this.socket == null) return;

			this.socket.Pump();

			this.dt = time - this.time;
			this.time = time;

			// process buffered packets
			Datagram datagram;
			while (socket != null && socket.Read(out datagram))
			{
				processDatagram(datagram);
				datagram.Release();
			}

			// process current state
			switch (state)
			{
				case ClientState.SendingConnectionRequest:
					sendingConnectionRequest();
					break;
				case ClientState.SendingChallengeResponse:
					sendingChallengeResponse();
					break;
				case ClientState.Connected:
					connected();
					break;
			}
		}

		private double timer = 0.0;
		private void clientTick(Object stateInfo)
		{
			while (isRunning)
			{
				Tick(DateTime.Now.GetTotalSeconds());

				// sleep until next tick
				double tickLength = 1.0 / tickrate;
				Thread.Sleep((int)(tickLength * 1000));
			}
		}

		private bool checkTimer(int customTickrate)
		{
			if (timer <= 0.0)
			{
				timer = 1.0 / customTickrate;
				return true;
			}

			return false;
		}

		private void consumeTimer()
		{
			timer -= dt;
		}

		private void processDatagram(Datagram datagram)
		{
			if (!MiscUtils.AddressEqual(datagram.sender, currentServerEndpoint))
				return;

			using (var reader = ByteArrayReaderWriter.Get(datagram.payload))
			{
				NetcodePacketHeader packetHeader = new NetcodePacketHeader();
				packetHeader.Read(reader);

				int length = datagram.payloadSize - (int)reader.ReadPosition;

				switch (packetHeader.PacketType)
				{
					case NetcodePacketType.ConnectionChallenge:
						processChallengePacket(packetHeader, length, reader);
						break;
					case NetcodePacketType.ConnectionDenied:
						processConnectionDenied(packetHeader, length, reader);
						break;
					case NetcodePacketType.ConnectionKeepAlive:
						processConnectionKeepAlive(packetHeader, length, reader);
						break;
					case NetcodePacketType.ConnectionPayload:
						processConnectionPayload(packetHeader, length, reader);
						break;
					case NetcodePacketType.ConnectionDisconnect:
						processConnectionDisconnect(packetHeader, length, reader);
						break;
				}
			}
		}

		#endregion

		#region states

		private double keepAliveTimer = 0.0;
		private void connected()
		{
			keepAliveTimer += dt;
			if (keepAliveTimer >= 0.1)
			{
				keepAliveTimer = 0.0;
				sendKeepAlive();
			}

			if ((time - lastResponseTime) >= Defines.NETCODE_TIMEOUT_SECONDS)
			{
				disconnect(ClientState.ConnectionTimedOut);
			}
		}

		private double connectionTimer = 0.0;
		private void sendingConnectionRequest()
		{
			// check and make sure connect token hasn't expired while we've been trying to connect
			if ((ulong)Math.Truncate(time) >= connectToken.ExpireTimestamp)
			{
				disconnect(ClientState.ConnectTokenExpired);
				return;
			}

			connectionTimer += dt;

			if (checkTimer(10))
			{
				// send a connection request 10 times per second
				sendConnectionRequest(currentServerEndpoint);
			}

			// if we don't get a response within timeout, move on.
			if ((int)connectionTimer >= connectToken.TimeoutSeconds && connectToken.TimeoutSeconds >= 0)
			{
				pendingDisconnectState = ClientState.ConnectionRequestTimedOut;
				connectionMoveNextEndpoint();
			}

			consumeTimer();
		}

		private void sendingChallengeResponse()
		{
			connectionTimer += dt;

			if (checkTimer(10))
			{
				// send a connection response 10 times per second
				sendConnectionResponse(currentServerEndpoint);
			}

			// if we don't get a response within timeout, move on.
			if ((int)connectionTimer >= connectToken.TimeoutSeconds && connectToken.TimeoutSeconds >= 0)
			{
				pendingDisconnectState = ClientState.ChallengeResponseTimedOut;
				connectionMoveNextEndpoint();
			}

			consumeTimer();
		}

		#endregion

		#region Process messages

		private void processConnectionDisconnect(NetcodePacketHeader header, int length, ByteArrayReaderWriter stream)
		{
			if (checkReplay(header)) return;
			if (this.state != ClientState.Connected) return;

			var decryptKey = serverToClientKey;
			var disconnectPacket = new NetcodeDisconnectPacket() { Header = header };
			if (!disconnectPacket.Read(stream, length, decryptKey, connectToken.ProtocolID))
			{
				return;
			}

			Disconnect();
		}

		private void processConnectionPayload(NetcodePacketHeader header, int length, ByteArrayReaderWriter stream)
		{
			if (checkReplay(header)) return;
			if (this.state != ClientState.Connected) return;

			var decryptKey = serverToClientKey;
			var payloadPacket = new NetcodePayloadPacket() { Header = header };
			if (!payloadPacket.Read(stream, length, decryptKey, connectToken.ProtocolID))
			{
				return;
			}

			lastResponseTime = time;
			if (OnMessageReceived != null)
				OnMessageReceived(payloadPacket.Payload, payloadPacket.Length);

			payloadPacket.Release();
		}

		private void processConnectionKeepAlive(NetcodePacketHeader header, int length, ByteArrayReaderWriter stream)
		{
			if (checkReplay(header)) return;

			var decryptKey = serverToClientKey;
			var keepAlive = new NetcodeKeepAlivePacket() { Header = header };
			if (!keepAlive.Read(stream, length, decryptKey, connectToken.ProtocolID))
			{
				return;
			}

			if (this.state == ClientState.Connected || this.state == ClientState.SendingChallengeResponse)
				lastResponseTime = time;

			if (this.state == ClientState.SendingChallengeResponse)
			{
				this.clientIndex = keepAlive.ClientIndex;
				this.maxSlots = keepAlive.MaxSlots;
				changeState(ClientState.Connected);
			}
		}

		private void processConnectionDenied(NetcodePacketHeader header, int length, ByteArrayReaderWriter stream)
		{
			var decryptKey = serverToClientKey;
			var denyPacket = new NetcodeDenyConnectionPacket() { Header = header };
			if (!denyPacket.Read(stream, length, decryptKey, connectToken.ProtocolID))
			{
				return;
			}

			pendingDisconnectState = ClientState.ConnectionDenied;

			// move onto the next server
			timer = 0.0;
			connectionTimer = 0.0;
			connectionMoveNextEndpoint();
		}

		private void processChallengePacket(NetcodePacketHeader header, int length, ByteArrayReaderWriter stream)
		{
			var decryptKey = serverToClientKey;
			var challengePacket = new NetcodeConnectionChallengeResponsePacket() { Header = header };
			if (!challengePacket.Read(stream, length, decryptKey, connectToken.ProtocolID))
			{
				return;
			}

			if (state == ClientState.SendingConnectionRequest)
			{
				this.challengeResponse = challengePacket;
				this.challengeResponse.Header.PacketType = NetcodePacketType.ChallengeResponse;

				// move onto sending challenge response
				timer = 0.0;
				connectionTimer = 0.0;
				changeState(ClientState.SendingChallengeResponse);
			}
		}

		#endregion

		#region Send methods

		private void changeState(ClientState newState)
		{
			if (newState != state)
			{
				state = newState;
				if (OnStateChanged != null)
					OnStateChanged(state);
			}
		}

		private void connectionMoveNextEndpoint()
		{
			timer = 0.0;
			connectionTimer = 0.0;

			if (connectServers.Count > 0)
			{
				currentServerEndpoint = connectServers.Dequeue();
				stopSocket();
				createSocket(currentServerEndpoint);

				changeState(ClientState.SendingConnectionRequest);
			}
			else
			{
				disconnect(pendingDisconnectState);
			}
		}

		private void sendKeepAlive()
		{
			serializePacket(new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionKeepAlive }, (writer) =>
			 {
				 writer.Write(clientIndex);
				 writer.Write(maxSlots);
			 }, clientToServerKey);
		}

		private void sendDisconnect()
		{
			for (int i = 0; i < Defines.NUM_DISCONNECT_PACKETS; i++)
			{
				serializePacket(new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionDisconnect }, (writer) =>
				{
				}, clientToServerKey);
			}
		}

		private void sendConnectionResponse(EndPoint server)
		{
			serializePacket(challengeResponse.Header, (writer) =>
		   {
			   challengeResponse.Write(writer);
		   }, clientToServerKey);
		}

		private void sendConnectionRequest(EndPoint server)
		{
			byte[] packetBuffer = BufferPool.GetBuffer(1 + 13 + 8 + 8 + 8 + Defines.NETCODE_CONNECT_TOKEN_PRIVATE_BYTES);
			using (var stream = ByteArrayReaderWriter.Get(packetBuffer))
			{
				stream.Write((byte)0);
				stream.WriteASCII(Defines.NETCODE_VERSION_INFO_STR);
				stream.Write(connectToken.ProtocolID);
				stream.Write(connectToken.ExpireTimestamp);
				stream.Write(connectToken.ConnectTokenSequence);
				stream.Write(connectToken.PrivateConnectTokenBytes);
			}

			socket.SendTo(packetBuffer, server);
			BufferPool.ReturnBuffer(packetBuffer);
		}

		#endregion

		#region Util methods

		private void sendPacket(NetcodePacketHeader packetHeader, byte[] packetData, int packetDataLen, byte[] key)
		{
			// assign a sequence number to this packet
			packetHeader.SequenceNumber = this.nextPacketSequence++;
            
			// encrypt packet data
			byte[] encryptedPacketBuffer = BufferPool.GetBuffer(2048);
			int encryptedBytes = PacketIO.EncryptPacketData(packetHeader, connectToken.ProtocolID, packetData, packetDataLen, key, encryptedPacketBuffer);

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
            try {
                socket.SendTo(packetBuffer, packetLen, currentServerEndpoint);
            }
            catch { }

            BufferPool.ReturnBuffer(packetBuffer);
			BufferPool.ReturnBuffer(encryptedPacketBuffer);
		}

		private void serializePacket(NetcodePacketHeader packetHeader, Action<ByteArrayReaderWriter> write, byte[] key)
		{
			byte[] tempPacket = BufferPool.GetBuffer(2048);
			int writeLen = 0;
			using (var writer = ByteArrayReaderWriter.Get(tempPacket))
			{
				write(writer);
				writeLen = (int)writer.WritePosition;
			}

			sendPacket(packetHeader, tempPacket, writeLen, key);
			BufferPool.ReturnBuffer(tempPacket);
		}

		private bool checkReplay(NetcodePacketHeader packetHeader)
		{
			return replayProtection.AlreadyReceived(packetHeader.SequenceNumber);
		}

		#endregion
	}
}
