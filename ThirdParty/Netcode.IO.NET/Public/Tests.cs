using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;

using NetcodeIO.NET.Utils;
using NetcodeIO.NET.Utils.IO;
using NetcodeIO.NET.Internal;

using System.Diagnostics;

namespace NetcodeIO.NET.Tests
{
	/// <summary>
	/// A suite of test methods for verifying that components of the API functions as intended
	/// All methods will either throw an exception if the test fails, or will simply return with no errors on success.
	/// </summary>
	public static class Tests
	{
		const ulong TEST_PROTOCOL_ID = 0x1122334455667788L;
		const ulong TEST_CLIENT_ID = 0x1L;
		const int TEST_SERVER_PORT = 40000;
		const int TEST_CONNECT_TOKEN_EXPIRY = 30;

		static readonly byte[] _privateKey = new byte[]
		{
			0x60, 0x6a, 0xbe, 0x6e, 0xc9, 0x19, 0x10, 0xea,
			0x9a, 0x65, 0x62, 0xf6, 0x6f, 0x2b, 0x30, 0xe4,
			0x43, 0x71, 0xd6, 0x2c, 0xd1, 0x99, 0x27, 0x26,
			0x6b, 0x3c, 0x60, 0xf4, 0xb7, 0x15, 0xab, 0xa1,
		};

		private struct testEncryptionMapping
		{
			public EndPoint address;
			public byte[] SendKey;
			public byte[] ReceiveKey;
			public int TimeoutSeconds;
			public uint ClientID;
		}

		public static void TestSequence()
		{
			assert(MiscUtils.SequenceBytesRequired(0) == 1);
			assert(MiscUtils.SequenceBytesRequired(0x11) == 1);
			assert(MiscUtils.SequenceBytesRequired(0x1122) == 2);
			assert(MiscUtils.SequenceBytesRequired(0x112233) == 3);
			assert(MiscUtils.SequenceBytesRequired(0x11223344) == 4);
			assert(MiscUtils.SequenceBytesRequired(0x1122334455) == 5);
			assert(MiscUtils.SequenceBytesRequired(0x112233445566) == 6);
			assert(MiscUtils.SequenceBytesRequired(0x11223344556677) == 7);
			assert(MiscUtils.SequenceBytesRequired(0x1122334455667788) == 8);
		}

		public static void TestConnectToken()
		{
			TokenFactory factory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint serverEndpoint2 = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 10);

			IPEndPoint[] connectServers = new IPEndPoint[]
			{
				serverEndpoint, serverEndpoint2
			};

			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			ulong timestamp = DateTime.Now.ToUnixTimestamp();
			byte[] connectToken = factory.GenerateConnectToken(connectServers, TEST_CONNECT_TOKEN_EXPIRY, 5, 1, TEST_CLIENT_ID, userData);

			NetcodePublicConnectToken token = new NetcodePublicConnectToken();
			using (var reader = ByteArrayReaderWriter.Get(connectToken))
			{
				token.Read(reader);
			}

			assert(token.ProtocolID == TEST_PROTOCOL_ID, "Token protocol ID does not match");
			assert(token.TimeoutSeconds == 5, "Token expiry does not match");

			assert(token.ConnectServers.Length == connectServers.Length, "Token connect server lists do not match");
			for (int i = 0; i < token.ConnectServers.Length; i++)
			{
				bool match = token.ConnectServers[i].Endpoint.Equals(connectServers[i]);
				assert(match, "Token connect server lists do not match");
			}

			assert(token.ExpireTimestamp > token.CreateTimestamp, "Token expire timestamp invalid");
			assert(token.CreateTimestamp >= timestamp, "Token create timestamp invalid");

			byte[] privateConnectToken = token.PrivateConnectTokenBytes;
			NetcodePrivateConnectToken privateToken = new NetcodePrivateConnectToken();
			privateToken.Read(privateConnectToken, _privateKey, TEST_PROTOCOL_ID, token.ExpireTimestamp, token.ConnectTokenSequence);

			assert(privateToken.ClientID == TEST_CLIENT_ID, "Private token client ID does not match");
			assert(MiscUtils.ByteArraysEqual(userData, privateToken.UserData), "Private token user data mismatch");
		}

		public static void TestChallengeToken()
		{
			// generate a challenge token
			NetcodeChallengeToken challengeToken = new NetcodeChallengeToken();
			challengeToken.ClientID = TEST_CLIENT_ID;

			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			challengeToken.UserData = userData;

			// write challenge token
			byte[] challengeTokenBytes = new byte[300];
			using (var writer = ByteArrayReaderWriter.Get(challengeTokenBytes))
				challengeToken.Write(writer);

			// encrypt buffer
			byte[] encryptedChallengeToken = new byte[300];
			ulong sequence = 1000;
			byte[] key = new byte[32];
			KeyUtils.GenerateKey(key);

			int challengeTokenLength = PacketIO.EncryptChallengeToken(sequence, challengeTokenBytes, key, encryptedChallengeToken);

			// read challenge token back in
			NetcodeChallengeToken readChallengeToken = new NetcodeChallengeToken();
			readChallengeToken.Read(encryptedChallengeToken, sequence, key);

			assert(readChallengeToken.ClientID == challengeToken.ClientID, "Client ID mismatch");
			assert(MiscUtils.ByteArraysEqual(readChallengeToken.UserData, challengeToken.UserData), "User data mismatch");
		}

		public static void TestEncryptionManager()
		{
			EncryptionManager encryptionManager = new EncryptionManager(256);
			encryptionManager.Reset();

			double time = 100.0;

			testEncryptionMapping[] encryptionMapping = new testEncryptionMapping[5];
			for (int i = 0; i < encryptionMapping.Length; i++)
			{
				encryptionMapping[i] = new testEncryptionMapping()
				{
					address = new IPEndPoint(IPAddress.Parse("0:0:0:0:0:0:0:1"), 20000 + i),
					SendKey = new byte[32],
					ReceiveKey = new byte[32],
					TimeoutSeconds = 10 + i,
					ClientID = (uint)i
				};

				KeyUtils.GenerateKey(encryptionMapping[i].SendKey);
				KeyUtils.GenerateKey(encryptionMapping[i].ReceiveKey);
			}

			// add encryption mappings and make sure they can be looked up by address
			for (int i = 0; i < encryptionMapping.Length; i++)
			{
				int encryptionIndex = encryptionManager.FindEncryptionMapping(encryptionMapping[i].address, time);
				assert(encryptionIndex == -1, "Encryption manager returned invalid index");
				assert(encryptionManager.GetSendKey(encryptionIndex) == null, "Encryption manager returned invalid key");
				assert(encryptionManager.GetReceiveKey(encryptionIndex) == null, "Encryption manager returned invalid key");

				assert(encryptionManager.AddEncryptionMapping(encryptionMapping[i].address, encryptionMapping[i].SendKey, encryptionMapping[i].ReceiveKey, time, -1.0, encryptionMapping[i].TimeoutSeconds, encryptionMapping[i].ClientID), "Encryption manager failed to add mapping");

				encryptionIndex = encryptionManager.FindEncryptionMapping(encryptionMapping[i].address, time);

				int timeoutSeconds = encryptionManager.GetTimeoutSeconds(encryptionIndex);
				uint clientID = encryptionManager.GetClientID(encryptionIndex);

				assert(timeoutSeconds == encryptionMapping[i].TimeoutSeconds, "Encryption manager returned invalid timeout seconds");
				assert(clientID == encryptionMapping[i].ClientID, "Encryption manager returned invalid client ID");

				byte[] sendKey = encryptionManager.GetSendKey(encryptionIndex);
				byte[] receiveKey = encryptionManager.GetReceiveKey(encryptionIndex);

				assert(sendKey != null, "Encryption manager failed to return key");
				assert(receiveKey != null, "Encryption manager failed to return key");

				assert(MiscUtils.ByteArraysEqual(sendKey, encryptionMapping[i].SendKey), "Encryption manager returned invalid key");
				assert(MiscUtils.ByteArraysEqual(receiveKey, encryptionMapping[i].ReceiveKey), "Encryption manager returned invalid key");
			}

			// removing an encryption mapping that doesn't exist should return false
			{
				IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse("0:0:0:0:0:0:0:1"), 50000);
				assert(encryptionManager.RemoveEncryptionMapping(endpoint, time) == false, "Encryption manager removed invalid entry");
			}

			// remove first and last encryption mappings
			assert(encryptionManager.RemoveEncryptionMapping(encryptionMapping[0].address, time), "Encryption manager failed to remove entry");
			assert(encryptionManager.RemoveEncryptionMapping(encryptionMapping[encryptionMapping.Length - 1].address, time), "Encryption manager failed to remove entry");

			// ensure removed encryption mappings cannot be looked up by address
			for (int i = 0; i < encryptionMapping.Length; i++)
			{
				int encryptionIndex = encryptionManager.FindEncryptionMapping(encryptionMapping[i].address, time);
				byte[] sendKey = encryptionManager.GetSendKey(encryptionIndex);
				byte[] receiveKey = encryptionManager.GetReceiveKey(encryptionIndex);

				if (i == 0 || i == (encryptionMapping.Length - 1))
				{
					assert(sendKey == null, "Encryption manager returned invalid key");
					assert(receiveKey == null, "Encryption manager returned invalid key");
				}
				else
				{
					assert(sendKey != null, "Encryption manager failed to return key");
					assert(receiveKey != null, "Encryption manager failed to return key");

					assert(MiscUtils.ByteArraysEqual(sendKey, encryptionMapping[i].SendKey), "Encryption manager returned invalid key");
					assert(MiscUtils.ByteArraysEqual(receiveKey, encryptionMapping[i].ReceiveKey), "Encryption manager returned invalid key");
				}
			}

			// add the encryption mappings back in
			assert(
				encryptionManager.AddEncryptionMapping(
					encryptionMapping[0].address,
					encryptionMapping[0].SendKey,
					encryptionMapping[0].ReceiveKey,
					time, -1.0,
					encryptionMapping[0].TimeoutSeconds,
					encryptionMapping[0].ClientID),
				"Encryption manager failed to add mapping");

			assert(
				encryptionManager.AddEncryptionMapping(
					encryptionMapping[encryptionMapping.Length - 1].address,
					encryptionMapping[encryptionMapping.Length - 1].SendKey,
					encryptionMapping[encryptionMapping.Length - 1].ReceiveKey,
					time, -1.0,
					encryptionMapping[encryptionMapping.Length - 1].TimeoutSeconds,
					encryptionMapping[encryptionMapping.Length - 1].ClientID),
				"Encryption manager failed to add mapping");

			// ensure all encryption mappings can be looked up
			for (int i = 0; i < encryptionMapping.Length; i++)
			{
				int encryptionIndex = encryptionManager.FindEncryptionMapping(encryptionMapping[i].address, time);

				int timeoutSeconds = encryptionManager.GetTimeoutSeconds(encryptionIndex);
				uint clientID = encryptionManager.GetClientID(encryptionIndex);

				assert(timeoutSeconds == encryptionMapping[i].TimeoutSeconds, "Encryption manager returned invalid timeout seconds");
				assert(clientID == encryptionMapping[i].ClientID, "Encryption manager returned invalid client ID");

				byte[] sendKey = encryptionManager.GetSendKey(encryptionIndex);
				byte[] receiveKey = encryptionManager.GetReceiveKey(encryptionIndex);

				assert(sendKey != null, "Encryption manager failed to return key");
				assert(receiveKey != null, "Encryption manager failed to return key");

				assert(MiscUtils.ByteArraysEqual(sendKey, encryptionMapping[i].SendKey), "Encryption manager returned invalid key");
				assert(MiscUtils.ByteArraysEqual(receiveKey, encryptionMapping[i].ReceiveKey), "Encryption manager returned invalid key");
			}

			// check that encryption mappings time out properly
			time += Defines.NETCODE_TIMEOUT_SECONDS * 2;

			for (int i = 0; i < encryptionMapping.Length; i++)
			{
				int encryptionIndex = encryptionManager.FindEncryptionMapping(encryptionMapping[i].address, time);
				byte[] sendKey = encryptionManager.GetSendKey(encryptionIndex);
				byte[] receiveKey = encryptionManager.GetReceiveKey(encryptionIndex);

				assert(sendKey == null, "Encryption manager returned invalid key");
				assert(receiveKey == null, "Encryption manager returned invalid key");
			}

			// add the same encryption mappings after timeout
			for (int i = 0; i < encryptionMapping.Length; i++)
			{
				int encryptionIndex = encryptionManager.FindEncryptionMapping(encryptionMapping[i].address, time);
				assert(encryptionIndex == -1, "Encryption manager returned invalid index");
				assert(encryptionManager.GetSendKey(encryptionIndex) == null, "Encryption manager returned invalid key");
				assert(encryptionManager.GetReceiveKey(encryptionIndex) == null, "Encryption manager returned invalid key");

				assert(encryptionManager.AddEncryptionMapping(encryptionMapping[i].address, encryptionMapping[i].SendKey, encryptionMapping[i].ReceiveKey, time, -1.0, encryptionMapping[i].TimeoutSeconds, encryptionMapping[i].ClientID), "Encryption manager failed to add mapping");

				encryptionIndex = encryptionManager.FindEncryptionMapping(encryptionMapping[i].address, time);

				int timeoutSeconds = encryptionManager.GetTimeoutSeconds(encryptionIndex);
				uint clientID = encryptionManager.GetClientID(encryptionIndex);

				assert(timeoutSeconds == encryptionMapping[i].TimeoutSeconds, "Encryption manager returned invalid timeout seconds");
				assert(clientID == encryptionMapping[i].ClientID, "Encryption manager returned invalid client ID");

				byte[] sendKey = encryptionManager.GetSendKey(encryptionIndex);
				byte[] receiveKey = encryptionManager.GetReceiveKey(encryptionIndex);

				assert(sendKey != null, "Encryption manager failed to return key");
				assert(receiveKey != null, "Encryption manager failed to return key");

				assert(MiscUtils.ByteArraysEqual(sendKey, encryptionMapping[i].SendKey), "Encryption manager returned invalid key");
				assert(MiscUtils.ByteArraysEqual(receiveKey, encryptionMapping[i].ReceiveKey), "Encryption manager returned invalid key");
			}

			// reset the encryption manager and ensure all entries are removed
			encryptionManager.Reset();
			for (int i = 0; i < encryptionMapping.Length; i++)
			{
				int encryptionIndex = encryptionManager.FindEncryptionMapping(encryptionMapping[i].address, time);
				byte[] sendKey = encryptionManager.GetSendKey(encryptionIndex);
				byte[] receiveKey = encryptionManager.GetReceiveKey(encryptionIndex);

				assert(sendKey == null, "Encryption manager returned invalid key");
				assert(receiveKey == null, "Encryption manager returned invalid key");
			}

			// test the expire time works as expected
			assert(encryptionManager.AddEncryptionMapping(encryptionMapping[0].address, encryptionMapping[0].SendKey, encryptionMapping[0].ReceiveKey, time, time + 1.0, encryptionMapping[0].TimeoutSeconds, encryptionMapping[0].ClientID), "Encryption manager failed to add mapping");
			{
				int encryptionIndex = encryptionManager.FindEncryptionMapping(encryptionMapping[0].address, time);
				assert(encryptionIndex != -1, "Encryption manager failed to find entry");

				assert(encryptionManager.FindEncryptionMapping(encryptionMapping[0].address, time + 1.1) == -1, "Encryption manager returned invalid entry");
				encryptionManager.SetExpireTime(encryptionIndex, -1.0);

				assert(encryptionManager.FindEncryptionMapping(encryptionMapping[0].address, time) == encryptionIndex, "Encryption manager returned invalid entry");
			}
		}

		public static void TestReplayProtection()
		{
			NetcodeReplayProtection replayProtection = new NetcodeReplayProtection();
			for (int i = 0; i < 2; i++)
			{
				replayProtection.Reset();
				assert(replayProtection.mostRecentSequence == 0, "Replay protection failed to reset");

				// sequence numbers with high bits set should be ignored
				assert(replayProtection.AlreadyReceived(1UL << 63) == false, "Replay protection failed to ignore sequence");
				assert(replayProtection.mostRecentSequence == 0, "Replay protection failed to ignore sequence");

				const int max_sequence = (256 * 4);
				for (ulong sequence = 0; sequence < max_sequence; sequence++)
				{
					assert(replayProtection.AlreadyReceived(sequence) == false, "Replay protection found invalid sequence");
				}

				// old packets outside the buffer should be considered already received
				assert(replayProtection.AlreadyReceived(0), "Replay protection failed to flag old sequence");

				// packets received a second time should be flagged already received
				for (ulong sequence = max_sequence - 10; sequence < max_sequence; sequence++)
				{
					assert(replayProtection.AlreadyReceived(sequence), "Replay protection failed to flag already received sequence");
				}

				// jumping ahead to a much higher sequence should be considered not already received
				assert(replayProtection.AlreadyReceived(max_sequence + 256) == false, "Replay protection flagged new sequence");

				// old packets should be considered already received
				for (ulong sequence = 0; sequence < max_sequence; sequence++)
				{
					assert(replayProtection.AlreadyReceived(sequence), "Replay protection failed to flag old sequence");
				}
			}
		}

		public static void TestClientServerConnection()
		{
			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 256, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			ulong clientID = 1000;
			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID, userData);
			client.Connect(connectToken, false);

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (client.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.Connected, "Client failed to connect: " + client.State);

			int clientMessagesReceived = 0;
			int serverMessagesReceived = 0;

			server.OnClientMessageReceived += (sender, payload, size) =>
			{
				// send message back to client
				sender.SendPayload(payload, size);

				serverMessagesReceived++;
			};

			client.OnMessageReceived += (payload, size) =>
			{
				clientMessagesReceived++;
			};

			// send messages to the server until client and server have at least 10 messages each (or until we hit iteration max)
			for (int i = 0; i < 1000; i++)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				byte[] testPayload = new byte[256];
				client.Send(testPayload, testPayload.Length);

				if (clientMessagesReceived >= 10 && serverMessagesReceived >= 10)
					break;
			}

			assert(clientMessagesReceived >= 10 && serverMessagesReceived >= 10, "Sending messages failed!");

			client.Disconnect();
			server.Stop();
		}

		public static void TestClientServerKeepAlive()
		{
			Console.WriteLine("TestClientServerKeepAlive");

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 256, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			ulong clientID = 1000;
			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, time, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID, userData);
			client.Connect(connectToken, false);

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (client.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.Connected, "Client failed to connect: " + client.State);

			// pump client and server long enough that they would timeout without keep-alive packets
			int iterations = (int)Math.Ceiling(1.5 * Defines.NETCODE_TIMEOUT_SECONDS / dt);

			for (int i = 0; i < iterations; i++)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;
			}

			assert(client.State == ClientState.Connected, "Client disconnected: " + client.State);

			client.Disconnect();
			server.Stop();
		}

		public static void TestClientServerMultipleClients()
		{
			Console.WriteLine("TestClientServerMultipleClients");

			int[] client_counts = new int[] { 2, 32, 5 };
			int start_stop_iters = client_counts.Length;

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);

			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 32, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			ulong clientID = 1000UL;
			ulong tokenSequence = 0UL;

			for (int i = 0; i < start_stop_iters; i++)
			{
				Client[] clients = new Client[client_counts[i]];
				for (int j = 0; j < clients.Length; j++)
				{
					IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100 + j);
					clients[j] = new Client((endpoint) =>
					{
						var socket = socketMgr.CreateContext(clientEndpoint);
						socket.Bind(clientEndpoint);

						return socket;
					});
					clients[j].time = time;

					byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, time,
						30, 5, tokenSequence, clientID, new byte[256]);

					clientID++;
					tokenSequence++;

					clients[j].Connect(connectToken, false);
				}

				var sw = new Stopwatch();

				// make sure all clients can connect
				while (true)
				{
					time += dt;

					socketMgr.Update(time);
					server.Tick(time);

					foreach (Client c in clients)
						c.Tick(time);

					int numConnectedClients = 0;
					bool disconnect = false;

					foreach (Client c in clients)
					{
						if (c.State <= ClientState.Disconnected)
						{
							disconnect = true;
							break;
						}

						if (c.State == ClientState.Connected)
							numConnectedClients++;
					}

					if (disconnect)
						break;

					if (numConnectedClients == client_counts[i])
						break;
				}

				foreach (Client c in clients)
					assert(c.State == ClientState.Connected, "Some clients failed to connect (iteration " + i + ")");

				// make sure all clients can exchange packets with the server
				int[] clientPacketsReceived = new int[clients.Length];

				int serverMessagesReceived = 0;

				server.OnClientMessageReceived += (sender, payload, size) =>
				{
					// send message back to client
					sender.SendPayload(payload, size);

					serverMessagesReceived++;
				};

				for (int x = 0; x < clients.Length; x++)
				{
					int clientIDX = x;
					clients[x].OnMessageReceived += (payload, size) =>
					{
						clientPacketsReceived[clientIDX]++;
					};
				}

				// send messages until everybody's got at least 10 messages (or we hit iteration max)
				for (int x = 0; x < 1000; x++)
				{
					time += dt;

					foreach (Client c in clients)
						c.Tick(time);

					server.Tick(time);
					socketMgr.Update(time);

					foreach (Client c in clients)
					{
						byte[] testPayload = new byte[256];
						c.Send(testPayload, testPayload.Length);
					}

					if (clientPacketsReceived.All(n => n >= 10) && serverMessagesReceived >= 10)
						break;
				}

				assert(clientPacketsReceived.All(n => n >= 10) && serverMessagesReceived >= 10, "Packets failed to send");

				foreach (Client c in clients)
					c.Disconnect();
			}

			server.Stop();
		}

		public static void TestClientServerMultipleServers()
		{
			Console.WriteLine("TestClientServerMultipleServers");

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 32, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			IPEndPoint[] testServerEndpoints = new IPEndPoint[]
			{
				new IPEndPoint(IPAddress.Parse("10.10.10.10"), 1000),
				new IPEndPoint(IPAddress.Parse("100.100.100.100"), 50000),
				new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT),
			};

			byte[] connectToken = tokenFactory.GenerateConnectToken(testServerEndpoints, time, 30, 5, 1UL, 1000UL, new byte[256]);

			client.Connect(connectToken, false);

			while (true)
			{
				time += dt;

				socketMgr.Update(time);
				client.Tick(time);
				server.Tick(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (client.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.Connected, "Client failed to connect: " + client.State);

			int clientMessagesReceived = 0;
			int serverMessagesReceived = 0;

			server.OnClientMessageReceived += (sender, payload, size) =>
			{
				// send message back to client
				sender.SendPayload(payload, size);

				serverMessagesReceived++;
			};

			client.OnMessageReceived += (payload, size) =>
			{
				clientMessagesReceived++;
			};

			// send messages to the server until client and server have at least 10 messages each (or until we hit iteration max)
			for (int i = 0; i < 1000; i++)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				byte[] testPayload = new byte[256];
				client.Send(testPayload, testPayload.Length);

				if (clientMessagesReceived >= 10 && serverMessagesReceived >= 10)
					break;
			}

			assert(clientMessagesReceived >= 10 && serverMessagesReceived >= 10, "Sending messages failed!");

			client.Disconnect();
			server.Stop();
		}

		public static void TestConnectTokenExpired()
		{
			Console.WriteLine("TestConnectTokenExpired");

			double time = 0.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			IPEndPoint[] testServerEndpoints = new IPEndPoint[]
			{
				new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT),
			};

			// token expires one second from when it is created
			byte[] connectToken = tokenFactory.GenerateConnectToken(testServerEndpoints, time, 1, 1, 1UL, 1000UL, new byte[256]);

			client.Connect(connectToken, false);

			// advancing time by 10 seconds should immediately force token to expire
			time += 10.0;
			client.Tick(time);

			assert(client.State == ClientState.ConnectTokenExpired, "Connect token failed to expire!");
		}

		public static void TestClientInvalidConnectToken()
		{
			Console.WriteLine("TestClientInvalidConnectToken");

			double time = 0.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			IPEndPoint[] testServerEndpoints = new IPEndPoint[]
			{
				new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT),
			};

			// fill a connect token with random bytes
			// this should cause the client to immediately fail with InvalidConnectToken
			byte[] connectToken = new byte[2048];
			KeyUtils.GenerateKey(connectToken);

			client.Connect(connectToken, false);

			assert(client.State == ClientState.InvalidConnectToken, "Client accepted invalid connect token");
		}

		public static void TestConnectionTimeout()
		{
			Console.WriteLine("TestConnectionTimeout");

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 256, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			ulong clientID = 1000;
			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID, userData);
			client.Connect(connectToken, false);

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (client.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.Connected, "Client failed to connect: " + client.State);

			// now stop updating the server and run long enough that the client should disconnect
			int iterations = (int)Math.Ceiling(1.5 * Defines.NETCODE_TIMEOUT_SECONDS / dt);

			for (int i = 0; i < iterations; i++)
			{
				time += dt;

				socketMgr.Update(time);
				client.Tick(time);

				if (client.State <= ClientState.Disconnected)
					break;
			}

			assert(client.State == ClientState.ConnectionTimedOut, "Client failed to time out: " + client.State);

			client.Disconnect();
			server.Stop();
		}

		public static void TestChallengeResponseTimeout()
		{
			Console.WriteLine("TestChallengeResponseTimeout");

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 256, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			// server should not respond to challenge response packets. this should make the client time out with ChallengeResponseTimedOut state
			server.debugIgnoreChallengeResponse = true;

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			ulong clientID = 1000;
			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID, userData);
			client.Connect(connectToken, false);

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (client.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.ChallengeResponseTimedOut, "Wrong client state (should be ChallengeResponseTimedOut): " + client.State);

			client.Disconnect();
			server.Stop();
		}

		public static void TestConnectionRequestTimeout()
		{
			Console.WriteLine("TestConnectionRequestTimeout");

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 256, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			// server should not respond to connection request packets. this should make the client time out with ConnectionRequestTimedOut state
			server.debugIgnoreConnectionRequest = true;

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			ulong clientID = 1000;
			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID, userData);
			client.Connect(connectToken, false);

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (client.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.ConnectionRequestTimedOut, "Wrong client state (should be ConnectionRequestTimedOut): " + client.State);

			client.Disconnect();
			server.Stop();
		}

		public static void TestConnectionDenied()
		{
			Console.WriteLine("TestConnectionDenied");

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);
			IPEndPoint clientEndpoint2 = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 101);

			// server only has room for one player - second player should be denied
			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 1, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			ulong clientID = 1000;
			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID, userData);
			client.Connect(connectToken, false);

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (client.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.Connected, "Client failed to connect: " + client.State);

			// now attempt to connect a second client. it should be denied.
			Client client2 = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint2);
				socket.Bind(clientEndpoint2);

				return socket;
			});
			client2.time = time;

			ulong clientID2 = 1001;

			byte[] connectToken2 = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID2, userData);
			client2.Connect(connectToken2, false);

			while (true)
			{
				time += dt;
				client.Tick(time);
				client2.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client2.State <= ClientState.Disconnected)
					break;

				if (client2.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.Connected, "Client1 was disconnected: " + client.State);
			assert(client2.State == ClientState.ConnectionDenied, "Client2 connection not denied: " + client2.State);

			client.Disconnect();
			client2.Disconnect();
			server.Stop();
		}

		public static void TestClientSideDisconnect()
		{
			Console.WriteLine("TestClientSideDisconnect");

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			// server only has room for one player - second player should be denied
			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 1, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			ulong clientID = 1000;
			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID, userData);
			client.Connect(connectToken, false);

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (client.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.Connected, "Client failed to connect: " + client.State);

			client.Disconnect();

			bool clientDisconnected = false;
			server.OnClientDisconnected += (remoteClient) =>
			{
				clientDisconnected = true;
			};

			time += 1.0;
			socketMgr.Update(time);
			server.Tick(time);

			assert(clientDisconnected, "Server did not detect client disconnect");

			server.Stop();
		}

		public static void TestServerSideDisconnect()
		{
			Console.WriteLine("TestServerSideDisconnect");

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			// server only has room for one player - second player should be denied
			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 1, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			ulong clientID = 1000;
			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID, userData);
			client.Connect(connectToken, false);

			RemoteClient remoteClient = null;
			server.OnClientConnected += (newClient) =>
			{
				remoteClient = newClient;
			};

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (server.NumConnectedClients == 1)
					break;
			}

			assert(client.State == ClientState.Connected, "Client failed to connect: " + client.State);
			assert(remoteClient != null, "Server failed to fire OnClientConnected");

			server.Disconnect(remoteClient);

			time += 1.0;
			socketMgr.Update(time);
			server.Tick(time);
			client.Tick(time);

			assert(client.State == ClientState.Disconnected, "Client was not disconnected: " + client.State);

			server.Stop();
		}

		public static void TestReconnect()
		{
			Console.WriteLine("TestReconnect");

			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT);
			IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT + 100);

			// server only has room for one player - second player should be denied
			Server server = new Server(socketMgr.CreateContext(serverEndpoint), 1, "127.0.0.1", TEST_SERVER_PORT, TEST_PROTOCOL_ID, _privateKey);
			server.Start(false);
			server.time = time;

			Client client = new Client((endpoint) =>
			{
				var socket = socketMgr.CreateContext(clientEndpoint);
				socket.Bind(clientEndpoint);

				return socket;
			});
			client.time = time;

			ulong clientID = 1000;
			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID, userData);
			client.Connect(connectToken, false);

			RemoteClient remoteClient = null;
			server.OnClientConnected += (newClient) =>
			{
				remoteClient = newClient;
			};

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (server.NumConnectedClients == 1)
					break;
			}

			assert(client.State == ClientState.Connected, "Client failed to connect: " + client.State);
			assert(remoteClient != null, "Server failed to fire OnClientConnected");

			server.Disconnect(remoteClient);

			time += 1.0;
			socketMgr.Update(time);
			server.Tick(time);
			client.Tick(time);

			assert(client.State == ClientState.Disconnected, "Client was not disconnected: " + client.State);

			// now get a new connect token and attempt to reconnect
			connectToken = tokenFactory.GenerateConnectToken(new IPEndPoint[] { serverEndpoint }, TEST_CONNECT_TOKEN_EXPIRY, 5, 0, clientID + 1, userData);
			client.Connect(connectToken, false);

			while (true)
			{
				time += dt;
				client.Tick(time);
				server.Tick(time);
				socketMgr.Update(time);

				if (client.State <= ClientState.Disconnected)
					break;

				if (client.State == ClientState.Connected)
					break;
			}

			assert(client.State == ClientState.Connected, "Client failed to reconnect: " + client.State);

			client.Disconnect();
			server.Stop();
		}

		public static void SoakTestClientServerConnection(int testDurationMinutes = -1)
		{
			double time = 0.0;
			double dt = 1.0 / 10.0;

			NetworkSimulatorSocketManager socketMgr = new NetworkSimulatorSocketManager();
			socketMgr.LatencyMS = 250;
			socketMgr.JitterMS = 250;
			socketMgr.PacketLossChance = 5;
			socketMgr.DuplicatePacketChance = 10;
			socketMgr.AutoTime = false;

			TokenFactory tokenFactory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			ulong nextTokenSequence = 0;
			ulong nextClientID = 0;

			const int NUM_SERVERS = 32;
			const int NUM_CLIENTS = 1024;

			const int BASE_SERVER_PORT = 40000;
			const int BASE_CLIENT_PORT = 1000;

			Client[] clients = new Client[NUM_CLIENTS];
			Server[] servers = new Server[NUM_SERVERS];

			IPEndPoint[] serverEndpoints = new IPEndPoint[NUM_SERVERS];

			Dictionary<Client, int> receivedPackets = new Dictionary<Client, int>();

			Random rand = new Random();

			// create and start servers
			for (int i = 0; i < servers.Length; i++)
			{
				IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), BASE_SERVER_PORT + i);
				serverEndpoints[i] = serverEndpoint;

				var socket = socketMgr.CreateContext(serverEndpoint);

				int slots = rand.Next(0, NUM_CLIENTS) + 1;
				var server = new Server(socket, slots, "127.0.0.1", BASE_SERVER_PORT + i, TEST_PROTOCOL_ID, _privateKey);
				server.Start(false);
				server.time = time;

				server.OnClientMessageReceived += (sender, payload, size) =>
				{
					// send payload back to client
					sender.SendPayload(payload, size);
				};

				servers[i] = server;
			}

			Dictionary<Client, bool> clientsConnected = new Dictionary<Client, bool>();

			// create clients
			for (int i = 0; i < clients.Length; i++)
			{
				IPEndPoint clientEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), BASE_CLIENT_PORT + i);

				Client client = new Client((endpoint) =>
				{
					var socket = socketMgr.CreateContext(clientEndpoint);
					socket.Bind(clientEndpoint);

					return socket;
				});

				// keep track of how many packets the client has received from the server
				client.OnMessageReceived += (payload, size) =>
				{
					receivedPackets[client]++;
				};

				// if the client disconnects from a server, reset packets received
				client.OnStateChanged += (state) =>
				{
					if (state == ClientState.Disconnected)
						receivedPackets[client] = 0;
					else if (state == ClientState.Connected)
						clientsConnected[client] = true;
				};

				clientsConnected.Add(client, false);
				receivedPackets.Add(client, 0);

				client.time = time;

				clients[i] = client;
			}

			bool runTest = true;

			Stopwatch sw = new Stopwatch();
			sw.Start();

			// now keep running the test!
			while (runTest)
			{
				time += dt;

				socketMgr.Update(time);

				foreach (Server server in servers)
					server.Tick(time);

				foreach (Client client in clients)
					client.Tick(time);

				// iterate over each client:
				// - if state is disconnected, grab a new connect token and connect
				// - if state is connected: if packets received is >=10, disconnect. otherwise, send a packet

				foreach (Client client in clients)
				{
					if (client.State == ClientState.Disconnected)
					{
						var connectToken = tokenFactory.GenerateConnectToken(serverEndpoints, time, serverEndpoints.Length * 5, 5, nextTokenSequence++, nextClientID++, new byte[256]);
						client.Connect(connectToken, false);
					}
					else if (client.State == ClientState.Connected)
					{
						if (receivedPackets[client] >= 10)
							client.Disconnect();
						else
						{
							byte[] payload = new byte[100];
							client.Send(payload, 100);
						}
					}
					else if (client.State < ClientState.Disconnected)
						break;
				}

				if (testDurationMinutes > 0)
				{
					runTest = sw.ElapsedMilliseconds <= (1000 * 60 * testDurationMinutes);
				}
			}

			foreach (Client client in clients)
			{
				assert(client.State >= ClientState.Disconnected, "Client in bad state: " + client.State);
				assert(clientsConnected[client] == true, "Client never connected!");
			}

			foreach (Server server in servers)
				server.Stop();
		}

		public static void TestConnectionRequestPacket()
		{
			// generate a connect token
			TokenFactory factory = new TokenFactory(TEST_PROTOCOL_ID, _privateKey);

			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);

			byte[] publicConnectTokenBuffer = factory.GenerateConnectToken(new IPEndPoint[]
			{
				new IPEndPoint( IPAddress.Parse("127.0.0.1"), TEST_SERVER_PORT )
			}, TEST_CONNECT_TOKEN_EXPIRY, 5, 1, TEST_CLIENT_ID, userData);

			NetcodePublicConnectToken publicConnectToken = new NetcodePublicConnectToken();
			using (var reader = ByteArrayReaderWriter.Get(publicConnectTokenBuffer))
				publicConnectToken.Read(reader);

			// set up a connection request packet wrapping the public token
			byte[] packetBuffer = new byte[1 + 13 + 8 + 8 + 8 + Defines.NETCODE_CONNECT_TOKEN_PRIVATE_BYTES];
			using (var writer = ByteArrayReaderWriter.Get(packetBuffer))
			{
				writer.Write((byte)0);
				writer.WriteASCII(Defines.NETCODE_VERSION_INFO_STR);
				writer.Write(publicConnectToken.ProtocolID);
				writer.Write(publicConnectToken.ExpireTimestamp);
				writer.Write(publicConnectToken.ConnectTokenSequence);
				writer.Write(publicConnectToken.PrivateConnectTokenBytes);
			}

			// read connection request back in
			var connectionRequestPacket = new NetcodeConnectionRequestPacket();
			using (var reader = ByteArrayReaderWriter.Get(packetBuffer))
			{
				byte prefixByte = reader.ReadByte();
				assert(prefixByte == 0, "Wrong prefix byte");

				assert(connectionRequestPacket.Read(reader, packetBuffer.Length - (int)reader.ReadPosition, TEST_PROTOCOL_ID), "Failed to read connection request packet");
			}

			// ensure read packet matches what was written
			assert(connectionRequestPacket.Expiration == publicConnectToken.ExpireTimestamp, "Expiration timestamps do not match");
			assert(connectionRequestPacket.TokenSequenceNum == publicConnectToken.ConnectTokenSequence, "Token sequence numbers do not match");
			assert(MiscUtils.ByteArraysEqual(connectionRequestPacket.ConnectTokenBytes, publicConnectToken.PrivateConnectTokenBytes), "Token private bytes do not match");
		}

		public static void TestConnectionDeniedPacket()
		{
			ulong testSequence = 1000;

			byte[] key = new byte[32];
			KeyUtils.GenerateKey(key);

			byte[] packet = serializePacket(testSequence, new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionDenied }, (writer) =>
			{
			}, key);

			// read packet back in
			NetcodePacketHeader header = new NetcodePacketHeader();
			using (var reader = ByteArrayReaderWriter.Get(packet))
			{
				header.Read(reader);
				assert(header.PacketType == NetcodePacketType.ConnectionDenied, "Packet type mismatch");
				assert(header.SequenceNumber == testSequence, "Packet sequence number mismatch");
			}
		}

		public static void TestConnectionKeepAlivePacket()
		{
			ulong testSequence = 1000;
			uint testMaxSlots = 256;
			uint testClientIndex = 10;

			byte[] key = new byte[32];
			KeyUtils.GenerateKey(key);

			byte[] packet = serializePacket(testSequence, new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionKeepAlive }, (writer) =>
			{
				var keepAlivePacket = new NetcodeKeepAlivePacket();
				keepAlivePacket.ClientIndex = testClientIndex;
				keepAlivePacket.MaxSlots = testMaxSlots;

				keepAlivePacket.Write(writer);
			}, key);

			// read packet back in
			NetcodePacketHeader header = new NetcodePacketHeader();
			using (var reader = ByteArrayReaderWriter.Get(packet))
			{
				header.Read(reader);
				assert(header.PacketType == NetcodePacketType.ConnectionKeepAlive, "Packet type mismatch");
				assert(header.SequenceNumber == testSequence, "Packet sequence number mismatch");

				NetcodeKeepAlivePacket keepAlivePacket = new NetcodeKeepAlivePacket() { Header = header };
				assert(keepAlivePacket.Read(reader, packet.Length - (int)reader.ReadPosition, key, TEST_PROTOCOL_ID), "Failed to read packet");
				assert(keepAlivePacket.ClientIndex == testClientIndex, "Client index mismatch");
				assert(keepAlivePacket.MaxSlots == testMaxSlots, "Max slot count mismatch");
			}
		}

		public static void TestConnectionChallengePacket()
		{
			NetcodeChallengeToken challengeToken = new NetcodeChallengeToken();
			challengeToken.ClientID = TEST_CLIENT_ID;

			byte[] userData = new byte[256];
			KeyUtils.GenerateKey(userData);
			challengeToken.UserData = userData;

			byte[] challengeKey = new byte[32];
			KeyUtils.GenerateKey(challengeKey);

			byte[] key = new byte[32];
			KeyUtils.GenerateKey(key);

			ulong testSequence = 500;
			ulong testChallengeSequence = 1000;

			byte[] tokenBytes = new byte[300];
			using (var writer = ByteArrayReaderWriter.Get(tokenBytes))
				challengeToken.Write(writer);

			byte[] encryptedToken = BufferPool.GetBuffer(300);
			int encryptedTokenBytes = PacketIO.EncryptChallengeToken(testChallengeSequence, tokenBytes, challengeKey, encryptedToken);

			var challengePacket = new NetcodeConnectionChallengeResponsePacket();
			challengePacket.ChallengeTokenSequence = testChallengeSequence;
			challengePacket.ChallengeTokenBytes = encryptedToken;

			byte[] packet = serializePacket(testSequence, new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionChallenge }, (writer) =>
			{
				challengePacket.Write(writer);
			}, key);

			// read packet back in
			NetcodePacketHeader header = new NetcodePacketHeader();
			using (var reader = ByteArrayReaderWriter.Get(packet))
			{
				header.Read(reader);
				assert(header.PacketType == NetcodePacketType.ConnectionChallenge, "Packet type mismatch");
				assert(header.SequenceNumber == testSequence, "Packet sequence number mismatch");

				NetcodeConnectionChallengeResponsePacket packetReader = new NetcodeConnectionChallengeResponsePacket() { Header = header };
				assert(packetReader.Read(reader, packet.Length - (int)reader.ReadPosition, key, TEST_PROTOCOL_ID), "Failed to read packet");

				assert(packetReader.ChallengeTokenSequence == testChallengeSequence, "Challenge sequence mismatch");
				assert(MiscUtils.ByteArraysEqual(packetReader.ChallengeTokenBytes, encryptedToken), "Challenge token encrypted bytes mismatch");

				NetcodeChallengeToken readToken = new NetcodeChallengeToken();
				assert(readToken.Read(packetReader.ChallengeTokenBytes, packetReader.ChallengeTokenSequence, challengeKey), "Failed to decrypt/read challenge token");

				assert(readToken.ClientID == TEST_CLIENT_ID, "Client ID mismatch");
				assert(MiscUtils.ByteArraysEqual(readToken.UserData, userData), "User data mismatch");
			}
		}

		public static void TestConnectionPayloadPacket()
		{
			ulong testSequence = 1000;

			byte[] key = new byte[32];
			KeyUtils.GenerateKey(key);

			byte[] payload = new byte[1024];
			KeyUtils.GenerateKey(payload);

			byte[] packet = serializePacket(testSequence, new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionPayload }, (writer) =>
			{
				writer.Write(payload);
			}, key);

			// read packet back in
			NetcodePacketHeader header = new NetcodePacketHeader();
			using (var reader = ByteArrayReaderWriter.Get(packet))
			{
				header.Read(reader);
				assert(header.PacketType == NetcodePacketType.ConnectionPayload, "Packet type mismatch");
				assert(header.SequenceNumber == testSequence, "Packet sequence number mismatch");

				NetcodePayloadPacket payloadPacket = new NetcodePayloadPacket() { Header = header };
				assert(payloadPacket.Read(reader, packet.Length - (int)reader.ReadPosition, key, TEST_PROTOCOL_ID), "Failed to read packet");

				byte[] readPayload = new byte[payloadPacket.Length];
				Buffer.BlockCopy(payloadPacket.Payload, 0, readPayload, 0, payloadPacket.Length);

				assert(MiscUtils.ByteArraysEqual(readPayload, payload), "Payload mismatch");
			}
		}

		public static void TestConnectionDisconnectPacket()
		{
			ulong testSequence = 1000;

			byte[] key = new byte[32];
			KeyUtils.GenerateKey(key);

			byte[] packet = serializePacket(testSequence, new NetcodePacketHeader() { PacketType = NetcodePacketType.ConnectionDisconnect }, (writer) =>
			{
			}, key);

			// read packet back in
			NetcodePacketHeader header = new NetcodePacketHeader();
			using (var reader = ByteArrayReaderWriter.Get(packet))
			{
				header.Read(reader);
				assert(header.PacketType == NetcodePacketType.ConnectionDisconnect, "Packet type mismatch");
				assert(header.SequenceNumber == testSequence, "Packet sequence number mismatch");
			}
		}

		private static byte[] serializePacket(ulong sequence, NetcodePacketHeader packetHeader, Action<ByteArrayReaderWriter> write, byte[] key)
		{
			byte[] tempPacket = new byte[2048];
			int writeLen = 0;
			using (var writer = ByteArrayReaderWriter.Get(tempPacket))
			{
				write(writer);
				writeLen = (int)writer.WritePosition;
			}

			return serializePacketToBuffer(sequence, packetHeader, tempPacket, writeLen, key);
		}

		private static byte[] serializePacketToBuffer(ulong sequence, NetcodePacketHeader packetHeader, byte[] packetData, int packetDataLen, byte[] key)
		{
			// assign a sequence number to this packet
			packetHeader.SequenceNumber = sequence;

			// encrypt packet data
			byte[] encryptedPacketBuffer = new byte[2048];
			int encryptedBytes = PacketIO.EncryptPacketData(packetHeader, TEST_PROTOCOL_ID, packetData, packetDataLen, key, encryptedPacketBuffer);

			int packetLen = 0;

			// write packet to byte array
			var packetBuffer = new byte[2048];
			using (var packetWriter = ByteArrayReaderWriter.Get(packetBuffer))
			{
				packetHeader.Write(packetWriter);
				packetWriter.WriteBuffer(encryptedPacketBuffer, encryptedBytes);

				packetLen = (int)packetWriter.WritePosition;
			}

			byte[] ret = new byte[packetLen];
			Buffer.BlockCopy(packetBuffer, 0, ret, 0, packetLen);

			return ret;
		}

		private static void assert(bool condition, string message = null)
		{
			if (!condition)
				throw new Exception(message);
		}
	}
}
