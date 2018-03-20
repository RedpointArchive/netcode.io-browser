using NetcodeIO.NET.Utils;
using NetcodeIO.NET.Utils.IO;
using NetcodeIO.NET.Internal;

namespace NetcodeIO.NET
{
	/// <summary>
	/// Packet type code
	/// </summary>
	internal enum NetcodePacketType
	{
		/// <summary>
		/// Connection request (sent from client to server)
		/// </summary>
		ConnectionRequest = 0,

		/// <summary>
		/// Connection denied (sent from server to client)
		/// </summary>
		ConnectionDenied = 1,

		/// <summary>
		/// Connection challenge (sent from server to client)
		/// </summary>
		ConnectionChallenge = 2,

		/// <summary>
		/// Challenge response (sent from client to server)
		/// </summary>
		ChallengeResponse = 3,

		/// <summary>
		/// Connection keep-alive (sent by both client and server)
		/// </summary>
		ConnectionKeepAlive = 4,

		/// <summary>
		/// Connection payload (sent by both client and server)
		/// </summary>
		ConnectionPayload = 5,

		/// <summary>
		/// Connection disconnect (sent by both client and server)
		/// </summary>
		ConnectionDisconnect = 6,

		/// <summary>
		/// Invalid packet
		/// </summary>
		InvalidPacket = 7,
	}

	/// <summary>
	/// Header for a netcode.io packet
	/// </summary>
	internal struct NetcodePacketHeader
	{
		public NetcodePacketType PacketType;
		public ulong SequenceNumber;
		public byte ReadSequenceByte;

		/// <summary>
		/// Create the prefix byte for this packet header
		/// </summary>
		public byte GetPrefixByte()
		{
			if (this.PacketType == NetcodePacketType.ConnectionRequest)
			{
				return 0;
			}
			else
			{
				byte prefixByte = 0;
				prefixByte |= (byte)this.PacketType;

				// check how many bytes are required to write sequence number
				int sequenceBytes = 0;
				ulong tempSequenceNumber = this.SequenceNumber;
				while (tempSequenceNumber > 0)
				{
					sequenceBytes++;
					tempSequenceNumber >>= 8;
				}

				if (sequenceBytes == 0)
					sequenceBytes = 1;

				prefixByte |= (byte)(sequenceBytes << 4);

				return prefixByte;
			}
		}

		/// <summary>
		/// Reads a packet from the stream.
		/// If packet is a connection request packet, stream read position lies at version info
		/// Otherwise, stream read position lies at packet-specific data
		/// </summary>
		public void Read(ByteArrayReaderWriter stream)
		{
			byte prefixByte = stream.ReadByte();
			this.ReadSequenceByte = prefixByte;

			// read in packet type
			int packetTypeNibble = prefixByte & 0x0F;
			if (packetTypeNibble >= 7)
			{
				this.PacketType = NetcodePacketType.InvalidPacket;
				return;
			}
			else
			{
				this.PacketType = (NetcodePacketType)packetTypeNibble;
			}

			// read in the sequence number
			// high 4 bits of prefix byte are number of bytes used to encode sequence number
			if (this.PacketType != NetcodePacketType.ConnectionRequest)
			{
				int numSequenceBytes = (prefixByte >> 4);

				// num sequence bytes is between 1 and 8.
				// if it is outside this range, we have an invalid packet
				if (numSequenceBytes < 1 || numSequenceBytes > 8)
				{
					this.PacketType = NetcodePacketType.InvalidPacket;
					return;
				}

				ulong sequenceNumber = 0;
				for (int i = 0; i < numSequenceBytes; i++)
				{
					sequenceNumber |= ((ulong)stream.ReadByte() << (i * 8));
				}

				this.SequenceNumber = sequenceNumber;
			}
		}

		/// <summary>
		/// Writes packet header to the stream
		/// </summary>
		public void Write(ByteArrayReaderWriter stream)
		{
			if (this.PacketType == NetcodePacketType.ConnectionRequest)
			{
				stream.Write((byte)0);
			}
			else
			{
				byte prefixByte = this.GetPrefixByte();

				// now write prefix byte and sequence number bytes
				stream.Write(prefixByte);

				int sequenceBytes = prefixByte >> 4;
				ulong tempSequenceNumber = this.SequenceNumber;
				for (int i = 0; i < sequenceBytes; i++)
				{
					stream.Write((byte)(tempSequenceNumber & 0xFF));
					tempSequenceNumber >>= 8;
				}
			}
		}
	}

	internal struct NetcodeKeepAlivePacket
	{
		public NetcodePacketHeader Header;
		public uint ClientIndex;
		public uint MaxSlots;

		public bool Read(ByteArrayReaderWriter stream, int length, byte[] key, ulong protocolID)
		{
			if (length != 8 + Defines.MAC_SIZE)
				return false;

			byte[] tempBuffer = BufferPool.GetBuffer(length);
			try
			{
				PacketIO.ReadPacketData(Header, stream, length, protocolID, key, tempBuffer);
			}
			catch
			{
				BufferPool.ReturnBuffer(tempBuffer);
				return false;
			}

			using (var dataReader = ByteArrayReaderWriter.Get(tempBuffer))
			{
				ClientIndex = dataReader.ReadUInt32();
				MaxSlots = dataReader.ReadUInt32();
			}

			return true;
		}

		public void Write(ByteArrayReaderWriter stream)
		{
			stream.Write(ClientIndex);
			stream.Write(MaxSlots);
		}
	}

	internal struct NetcodePublicConnectToken
	{
		public ulong ProtocolID;
		public ulong CreateTimestamp;
		public ulong ExpireTimestamp;
		public ulong ConnectTokenSequence;
		public byte[] PrivateConnectTokenBytes;
		public ConnectTokenServerEntry[] ConnectServers;
		public byte[] ClientToServerKey;
		public byte[] ServerToClientKey;
		public int TimeoutSeconds;

		public bool Read(ByteArrayReaderWriter reader)
		{
			char[] vInfo = new char[13];
			reader.ReadASCIICharsIntoBuffer(vInfo, 13);
			if (!MiscUtils.MatchChars(vInfo, Defines.NETCODE_VERSION_INFO_STR))
				return false;

			ProtocolID = reader.ReadUInt64();

			CreateTimestamp = reader.ReadUInt64();
			ExpireTimestamp = reader.ReadUInt64();
			ConnectTokenSequence = reader.ReadUInt64();
			PrivateConnectTokenBytes = reader.ReadBytes(Defines.NETCODE_CONNECT_TOKEN_PRIVATE_BYTES);
			TimeoutSeconds = (int)reader.ReadUInt32();

			int numServers = (int)reader.ReadUInt32();
			if (numServers < 1 || numServers > Defines.MAX_SERVER_ADDRESSES)
				return false;

			this.ConnectServers = new ConnectTokenServerEntry[numServers];
			for (int i = 0; i < numServers; i++)
			{
				this.ConnectServers[i] = new ConnectTokenServerEntry();
				this.ConnectServers[i].ReadData(reader);
			}

			ClientToServerKey = reader.ReadBytes(32);
			ServerToClientKey = reader.ReadBytes(32);

			return true;
		}

		public void Write(ByteArrayReaderWriter writer)
		{
			writer.WriteASCII(Defines.NETCODE_VERSION_INFO_STR);
			writer.Write(ProtocolID);

			writer.Write(CreateTimestamp);
			writer.Write(ExpireTimestamp);
			writer.Write(ConnectTokenSequence);
			writer.Write(PrivateConnectTokenBytes);
			writer.Write((uint)TimeoutSeconds);

			writer.Write((uint)ConnectServers.Length);
			for (int i = 0; i < ConnectServers.Length; i++)
				ConnectServers[i].WriteData(writer);

			writer.Write(ClientToServerKey);
			writer.Write(ServerToClientKey);
		}
	}

	internal struct NetcodePrivateConnectToken
	{
		public ulong ClientID;
		public int TimeoutSeconds;
		public ConnectTokenServerEntry[] ConnectServers;
		public byte[] ClientToServerKey;
		public byte[] ServerToClientKey;
		public byte[] UserData;

		public bool Read(byte[] token, byte[] key, ulong protocolID, ulong expiration, ulong sequence)
		{
			byte[] tokenBuffer = BufferPool.GetBuffer(Defines.NETCODE_CONNECT_TOKEN_PRIVATE_BYTES);
			int tokenLen = 0;
			try
			{
				tokenLen = PacketIO.DecryptPrivateConnectToken(token, protocolID, expiration, sequence, key, tokenBuffer);
			}
			catch
			{
				BufferPool.ReturnBuffer(tokenBuffer);
				return false;
			}

			try
			{
				using (var reader = ByteArrayReaderWriter.Get(tokenBuffer))
				{
					this.ClientID = reader.ReadUInt64();
					this.TimeoutSeconds = (int)reader.ReadUInt32();
					uint numServerAddresses = reader.ReadUInt32();

					if (numServerAddresses == 0 || numServerAddresses > Defines.MAX_SERVER_ADDRESSES)
					{
						BufferPool.ReturnBuffer(tokenBuffer);
						return false;
					}

					this.ConnectServers = new ConnectTokenServerEntry[numServerAddresses];
					for (int i = 0; i < numServerAddresses; i++)
					{
						this.ConnectServers[i] = new ConnectTokenServerEntry();
						this.ConnectServers[i].ReadData(reader);
					}

					ClientToServerKey = new byte[32];
					ServerToClientKey = new byte[32];
					UserData = new byte[256];

					reader.ReadBytesIntoBuffer(ClientToServerKey, 32);
					reader.ReadBytesIntoBuffer(ServerToClientKey, 32);
					reader.ReadBytesIntoBuffer(UserData, 256);
				}
			}
			catch
			{
				BufferPool.ReturnBuffer(tokenBuffer);
				return false;
			}

			return true;
		}

		public void Write(ByteArrayReaderWriter stream)
		{
			stream.Write(ClientID);
			stream.Write((uint)TimeoutSeconds);
			stream.Write((uint)ConnectServers.Length);
			foreach (var server in ConnectServers)
				server.WriteData(stream);

			stream.Write(ClientToServerKey);
			stream.Write(ServerToClientKey);
			stream.Write(UserData);
		}
	}

	internal struct NetcodeChallengeToken
	{
		public ulong ClientID;
		public byte[] UserData;

		public bool Read(byte[] token, ulong sequenceNum, byte[] key)
		{
			byte[] tokenBuffer = BufferPool.GetBuffer(300);
			int tokenLen = 0;
			try
			{
				tokenLen = PacketIO.DecryptChallengeToken(sequenceNum, token, key, tokenBuffer);
			}
			catch
			{
				BufferPool.ReturnBuffer(tokenBuffer);
				return false;
			}

			using (var reader = ByteArrayReaderWriter.Get(tokenBuffer))
			{
				ClientID = reader.ReadUInt64();
				UserData = reader.ReadBytes(256);
			}

			return true;
		}

		public void Write(ByteArrayReaderWriter stream)
		{
			stream.Write(ClientID);
			stream.Write(UserData);
		}
	}

	internal struct NetcodeConnectionRequestPacket
	{
		public ulong Expiration;
		public ulong TokenSequenceNum;
		public byte[] ConnectTokenBytes;

		public bool Read(ByteArrayReaderWriter stream, int length, ulong protocolID)
		{
			if (length != 13 + 8 + 8 + 8 + Defines.NETCODE_CONNECT_TOKEN_PRIVATE_BYTES)
				return false;

			char[] vInfo = new char[Defines.NETCODE_VERSION_INFO_BYTES];
			stream.ReadASCIICharsIntoBuffer(vInfo, Defines.NETCODE_VERSION_INFO_BYTES);
			if (!MiscUtils.MatchChars(vInfo, Defines.NETCODE_VERSION_INFO_STR))
			{
				return false;
			}

			if (stream.ReadUInt64() != protocolID)
			{
				return false;
			}

			this.Expiration = stream.ReadUInt64();
			this.TokenSequenceNum = stream.ReadUInt64();
			this.ConnectTokenBytes = BufferPool.GetBuffer(Defines.NETCODE_CONNECT_TOKEN_PRIVATE_BYTES);
			stream.ReadBytesIntoBuffer(this.ConnectTokenBytes, Defines.NETCODE_CONNECT_TOKEN_PRIVATE_BYTES);

			return true;
		}

		public void Release()
		{
			BufferPool.ReturnBuffer(this.ConnectTokenBytes);
		}
	}
	
	internal struct NetcodeConnectionChallengeResponsePacket
	{
		public NetcodePacketHeader Header;
		public ulong ChallengeTokenSequence;
		public byte[] ChallengeTokenBytes;

		public bool Read(ByteArrayReaderWriter stream, int length, byte[] key, ulong protocolID)
		{
			byte[] packetBuffer = BufferPool.GetBuffer(8 + 300 + Defines.MAC_SIZE);
			int packetLen = 0;
			try
			{
				packetLen = PacketIO.ReadPacketData(Header, stream, length, protocolID, key, packetBuffer);
			}
			catch(System.Exception e)
			{
				BufferPool.ReturnBuffer(packetBuffer);
				return false;
			}

			if (packetLen != 308)
			{
				BufferPool.ReturnBuffer(packetBuffer);
				return false;
			}

			ChallengeTokenBytes = BufferPool.GetBuffer(300);
			using (var reader = ByteArrayReaderWriter.Get(packetBuffer))
			{
				ChallengeTokenSequence = reader.ReadUInt64();
				reader.ReadBytesIntoBuffer(ChallengeTokenBytes, 300);
			}

			BufferPool.ReturnBuffer(packetBuffer);
			return true;
		}

		public void Write(ByteArrayReaderWriter stream)
		{
			stream.Write(ChallengeTokenSequence);
			stream.Write(ChallengeTokenBytes);
		}

		public void Release()
		{
			BufferPool.ReturnBuffer(ChallengeTokenBytes);
		}
	}

	internal struct NetcodePayloadPacket
	{
		public NetcodePacketHeader Header;
		public byte[] Payload;
		public int Length;

		public bool Read(ByteArrayReaderWriter stream, int length, byte[] key, ulong protocolID)
		{
			Payload = BufferPool.GetBuffer(2048);
			Length = 0;
			try
			{
				Length = PacketIO.ReadPacketData(Header, stream, length, protocolID, key, Payload);
			}
			catch
			{
				BufferPool.ReturnBuffer(Payload);
				return false;
			}

			if (Length < 1 || Length > Defines.MAX_PAYLOAD_SIZE)
			{
				BufferPool.ReturnBuffer(Payload);
				return false;
			}

			return true;
		}

		public void Release()
		{
			BufferPool.ReturnBuffer(Payload);
			Payload = null;
			Length = 0;
		}
	}

	internal struct NetcodeDenyConnectionPacket
	{
		public NetcodePacketHeader Header;

		public bool Read(ByteArrayReaderWriter stream, int length, byte[] key, ulong protocolID)
		{
			if (length != Defines.MAC_SIZE)
				return false;

			byte[] tempBuffer = BufferPool.GetBuffer(0);
			try
			{
				PacketIO.ReadPacketData(Header, stream, length, protocolID, key, tempBuffer);
			}
			catch
			{
				BufferPool.ReturnBuffer(tempBuffer);
				return false;
			}

			return true;
		}
	}

	internal struct NetcodeDisconnectPacket
	{
		public NetcodePacketHeader Header;

		public bool Read(ByteArrayReaderWriter stream, int length, byte[] key, ulong protocolID)
		{
			if (length != Defines.MAC_SIZE)
				return false;

			byte[] tempBuffer = BufferPool.GetBuffer(0);
			try
			{
				PacketIO.ReadPacketData(Header, stream, length, protocolID, key, tempBuffer);
			}
			catch
			{
				BufferPool.ReturnBuffer(tempBuffer);
				return false;
			}

			return true;
		}
	}
}
