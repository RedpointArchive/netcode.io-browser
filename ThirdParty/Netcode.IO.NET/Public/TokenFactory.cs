using System;
using System.Net;
using System.Net.Sockets;

using NetcodeIO.NET.Internal;
using NetcodeIO.NET.Utils;
using NetcodeIO.NET.Utils.IO;

namespace NetcodeIO.NET
{
	/// <summary>
	/// Helper class for generating connect tokens
	/// </summary>
	public class TokenFactory
	{
		internal ulong protocolID;
		internal byte[] privateKey;

		public TokenFactory(ulong protocolID, byte[] privateKey)
		{
			this.protocolID = protocolID;
			this.privateKey = privateKey;
		}

		/// <summary>
		/// Generate a new public connect token
		/// </summary>
		/// <param name="addressList">The list of public server addresses in this connect token</param>
		/// <param name="expirySeconds">The number of seconds until this token expires</param>
		/// <param name="sequence">The token sequence number of this token</param>
		/// <param name="clientID">The unique ID to assign to the client consuming this token</param>
		/// <param name="userData">Up to 256 bytes of arbitrary user data</param>
		/// <returns>2048 byte connect token to send to client</returns>
		public byte[] GenerateConnectToken(IPEndPoint[] addressList, int expirySeconds, int serverTimeout, ulong sequence, ulong clientID, byte[] userData)
		{
			return GenerateConnectToken(addressList, DateTime.Now.GetTotalSeconds(), expirySeconds, serverTimeout, sequence, clientID, userData);
		}

		internal byte[] GenerateConnectToken(IPEndPoint[] addressList, double time, int expirySeconds, int serverTimeout, ulong sequence, ulong clientID, byte[] userData)
		{
			if (userData.Length > 256)
			{
				throw new ArgumentOutOfRangeException("User data cannot be larger than 256 bytes");
			}

			if (addressList == null)
			{
				throw new NullReferenceException("Address list cannot be null");
			}
			else if (addressList.Length == 0)
			{
				throw new ArgumentOutOfRangeException("Address list cannot be empty");
			}
			else if (addressList.Length > Defines.MAX_SERVER_ADDRESSES)
			{
				throw new ArgumentOutOfRangeException("Address list cannot contain more than " + Defines.MAX_SERVER_ADDRESSES + " entries");
			}

			NetcodePrivateConnectToken privateConnectToken = new NetcodePrivateConnectToken();
			privateConnectToken.ClientID = clientID;
			privateConnectToken.TimeoutSeconds = serverTimeout;

			// generate random crypto keys
			byte[] clientToServerKey = new byte[32];
			byte[] serverToClientKey = new byte[32];
			KeyUtils.GenerateKey(clientToServerKey);
			KeyUtils.GenerateKey(serverToClientKey);

			privateConnectToken.ClientToServerKey = clientToServerKey;
			privateConnectToken.ServerToClientKey = serverToClientKey;
			privateConnectToken.UserData = new byte[256];

			Buffer.BlockCopy(userData, 0, privateConnectToken.UserData, 0, userData.Length);

			privateConnectToken.ConnectServers = new ConnectTokenServerEntry[addressList.Length];
			for (int i = 0; i < privateConnectToken.ConnectServers.Length; i++)
			{
				privateConnectToken.ConnectServers[i] = new ConnectTokenServerEntry()
				{
					AddressType = addressList[i].AddressFamily == AddressFamily.InterNetwork ? NetcodeAddressType.IPv4 : NetcodeAddressType.IPv6,
					Endpoint = addressList[i]
				};
			}

			byte[] privateConnectTokenBytes = new byte[1024];
			using (var writer = ByteArrayReaderWriter.Get(privateConnectTokenBytes))
			{
				privateConnectToken.Write(writer);
			}

			ulong createTimestamp = (ulong)Math.Truncate(time);
			ulong expireTimestamp = expirySeconds >= 0 ? ( createTimestamp + (ulong)expirySeconds ) : 0xFFFFFFFFFFFFFFFFUL;

			byte[] encryptedPrivateToken = new byte[1024];
			PacketIO.EncryptPrivateConnectToken(privateConnectTokenBytes, protocolID, expireTimestamp, sequence, privateKey, encryptedPrivateToken);

			NetcodePublicConnectToken publicToken = new NetcodePublicConnectToken();
			publicToken.ProtocolID = protocolID;
			publicToken.CreateTimestamp = createTimestamp;
			publicToken.ExpireTimestamp = expireTimestamp;
			publicToken.ConnectTokenSequence = sequence;
			publicToken.PrivateConnectTokenBytes = encryptedPrivateToken;
			publicToken.ConnectServers = privateConnectToken.ConnectServers;
			publicToken.ClientToServerKey = clientToServerKey;
			publicToken.ServerToClientKey = serverToClientKey;
			publicToken.TimeoutSeconds = serverTimeout;

			byte[] publicTokenBytes = new byte[2048];
			using (var writer = ByteArrayReaderWriter.Get(publicTokenBytes))
			{
				publicToken.Write(writer);
			}

			return publicTokenBytes;
		}
	}
}
