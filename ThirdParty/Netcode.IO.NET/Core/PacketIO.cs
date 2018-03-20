using System;

using NetcodeIO.NET.Utils;
using NetcodeIO.NET.Utils.IO;

using Org.BouncyCastle.Crypto.TlsExt;

namespace NetcodeIO.NET.Internal
{
	/// <summary>
	/// Helper class for reading/writing packets
	/// </summary>
	internal static class PacketIO
	{
		/// <summary>
		/// Read and decrypt packet data into an output buffer
		/// </summary>
		public static int ReadPacketData(NetcodePacketHeader header, ByteArrayReaderWriter stream, int length, ulong protocolID, byte[] key, byte[] outputBuffer)
		{
			byte[] encryptedBuffer = BufferPool.GetBuffer(2048);
			stream.ReadBytesIntoBuffer(encryptedBuffer, length);
			
			int decryptedBytes;
			try
			{
				decryptedBytes = DecryptPacketData(header, protocolID, encryptedBuffer, length, key, outputBuffer);
			}
			catch(Exception e)
			{
				BufferPool.ReturnBuffer(encryptedBuffer);
				throw e;
			}

            BufferPool.ReturnBuffer(encryptedBuffer);
			return decryptedBytes;
		}

		/// <summary>
		/// Encrypt a packet's data
		/// </summary>
		public static int EncryptPacketData(NetcodePacketHeader header, ulong protocolID, byte[] packetData, int packetDataLen, byte[] key, byte[] outBuffer)
		{
			byte[] additionalData = BufferPool.GetBuffer(Defines.NETCODE_VERSION_INFO_BYTES + 8 + 1);
			using (var writer = ByteArrayReaderWriter.Get(additionalData))
			{
				writer.WriteASCII(Defines.NETCODE_VERSION_INFO_STR);
				writer.Write(protocolID);
				writer.Write(header.GetPrefixByte());
			}

			byte[] nonce = BufferPool.GetBuffer(12);
			using (var writer = ByteArrayReaderWriter.Get(nonce))
			{
				writer.Write((UInt32)0);
				writer.Write(header.SequenceNumber);
			}

			int ret;
			try
			{
				ret = AEAD_Chacha20_Poly1305.Encrypt(packetData, 0, packetDataLen, additionalData, nonce, key, outBuffer);
			}
			catch (Exception e)
			{
				BufferPool.ReturnBuffer(additionalData);
				BufferPool.ReturnBuffer(nonce);
				throw e;
			}

			BufferPool.ReturnBuffer(additionalData);
			BufferPool.ReturnBuffer(nonce);

			return ret;
		}

		/// <summary>
		/// Decrypt a packet's data
		/// </summary>
		public static int DecryptPacketData(NetcodePacketHeader header, ulong protocolID, byte[] packetData, int packetDataLen, byte[] key, byte[] outBuffer)
		{
			byte[] additionalData = BufferPool.GetBuffer(Defines.NETCODE_VERSION_INFO_BYTES + 8 + 1);
			using (var writer = ByteArrayReaderWriter.Get(additionalData))
			{
				writer.WriteASCII(Defines.NETCODE_VERSION_INFO_STR);
				writer.Write(protocolID);
				writer.Write(header.ReadSequenceByte);
			}

			byte[] nonce = BufferPool.GetBuffer(12);
			using (var writer = ByteArrayReaderWriter.Get(nonce))
			{
				writer.Write((UInt32)0);
				writer.Write(header.SequenceNumber);
			}

			int ret;
			try
			{
				ret = AEAD_Chacha20_Poly1305.Decrypt(packetData, 0, packetDataLen, additionalData, nonce, key, outBuffer);
			}
			catch(Exception e)
			{
				BufferPool.ReturnBuffer(additionalData);
				BufferPool.ReturnBuffer(nonce);
				throw e;
			}

			BufferPool.ReturnBuffer(additionalData);
			BufferPool.ReturnBuffer(nonce);

			return ret;
		}

		/// <summary>
		/// Encrypt a challenge token
		/// </summary>
		public static int EncryptChallengeToken(ulong sequenceNum, byte[] packetData, byte[] key, byte[] outBuffer)
		{
			byte[] additionalData = BufferPool.GetBuffer(0);

			byte[] nonce = BufferPool.GetBuffer(12);
			using (var writer = ByteArrayReaderWriter.Get(nonce))
			{
				writer.Write((UInt32)0);
				writer.Write(sequenceNum);
			}

			int ret;
			try
			{
				ret = AEAD_Chacha20_Poly1305.Encrypt(packetData, 0, 300 - Defines.MAC_SIZE, additionalData, nonce, key, outBuffer);
			}
			catch (Exception e)
			{
				BufferPool.ReturnBuffer(additionalData);
				BufferPool.ReturnBuffer(nonce);
				throw e;
			}

			BufferPool.ReturnBuffer(additionalData);
			BufferPool.ReturnBuffer(nonce);

			return ret;
		}

		/// <summary>
		/// Decrypt a challenge token
		/// </summary>
		public static int DecryptChallengeToken(ulong sequenceNum, byte[] packetData, byte[] key, byte[] outBuffer)
		{
			byte[] additionalData = BufferPool.GetBuffer(0);

			byte[] nonce = BufferPool.GetBuffer(12);
			using (var writer = ByteArrayReaderWriter.Get(nonce))
			{
				writer.Write((UInt32)0);
				writer.Write(sequenceNum);
			}

			int ret;
			try
			{
				ret = AEAD_Chacha20_Poly1305.Decrypt(packetData, 0, 300, additionalData, nonce, key, outBuffer);
			}
			catch (Exception e)
			{
				BufferPool.ReturnBuffer(additionalData);
				BufferPool.ReturnBuffer(nonce);
				throw e;
			}

			BufferPool.ReturnBuffer(additionalData);
			BufferPool.ReturnBuffer(nonce);

			return ret;
		}

		// Encrypt a private connect token
		public static int EncryptPrivateConnectToken(byte[] privateConnectToken, ulong protocolID, ulong expireTimestamp, ulong sequence, byte[] key, byte[] outBuffer)
		{
			int len = privateConnectToken.Length;

			byte[] additionalData = BufferPool.GetBuffer(Defines.NETCODE_VERSION_INFO_BYTES + 8 + 8);
			using (var writer = ByteArrayReaderWriter.Get(additionalData))
			{
				writer.WriteASCII(Defines.NETCODE_VERSION_INFO_STR);
				writer.Write(protocolID);
				writer.Write(expireTimestamp);
			}

			byte[] nonce = BufferPool.GetBuffer(12);
			using (var writer = ByteArrayReaderWriter.Get(nonce))
			{
				writer.Write((UInt32)0);
				writer.Write(sequence);
			}

			var ret = AEAD_Chacha20_Poly1305.Encrypt(privateConnectToken, 0, len - Defines.MAC_SIZE, additionalData, nonce, key, outBuffer);

			BufferPool.ReturnBuffer(additionalData);
			BufferPool.ReturnBuffer(nonce);

			return ret;
		}

		// Decrypt a private connect token
		public static int DecryptPrivateConnectToken(byte[] encryptedConnectToken, ulong protocolID, ulong expireTimestamp, ulong sequence, byte[] key, byte[] outBuffer)
		{
			int len = encryptedConnectToken.Length;

			byte[] additionalData = BufferPool.GetBuffer(Defines.NETCODE_VERSION_INFO_BYTES + 8 + 8);
			using (var writer = ByteArrayReaderWriter.Get(additionalData))
			{
				writer.WriteASCII(Defines.NETCODE_VERSION_INFO_STR);
				writer.Write(protocolID);
				writer.Write(expireTimestamp);
			}

			byte[] nonce = BufferPool.GetBuffer(12);
			using (var writer = ByteArrayReaderWriter.Get(nonce))
			{
				writer.Write((UInt32)0);
				writer.Write(sequence);
			}

			var ret = AEAD_Chacha20_Poly1305.Decrypt(encryptedConnectToken, 0, len, additionalData, nonce, key, outBuffer);

			BufferPool.ReturnBuffer(additionalData);
			BufferPool.ReturnBuffer(nonce);

			return ret;
		}
	}
}
