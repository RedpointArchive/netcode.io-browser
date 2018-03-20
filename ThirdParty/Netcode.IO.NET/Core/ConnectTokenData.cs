using System;
using System.Net;

using NetcodeIO.NET.Utils.IO;

namespace NetcodeIO.NET
{
	internal enum NetcodeAddressType : byte
	{
		None = 0,
		IPv4 = 1,
		IPv6 = 2,
	}

	internal class ConnectTokenServerEntry
	{
		private static byte[] tempIPV4 = new byte[4];
		private static byte[] tempIPV6 = new byte[16];

		public NetcodeAddressType AddressType;
		public IPEndPoint Endpoint;

		public void ReadData(ByteArrayReaderWriter stream)
		{
			byte addressVal = stream.ReadByte();

			// if address type is not 0 or 1, data is not valid
			if (addressVal != 0 && addressVal != 1)
				throw new FormatException();

			this.AddressType = (NetcodeAddressType)addressVal;

			IPAddress ip = null;

			if (this.AddressType == NetcodeAddressType.IPv4)
			{
				stream.ReadBytesIntoBuffer(tempIPV4, 4);
				ip = new IPAddress(tempIPV4);
			}
			else
			{
				stream.ReadBytesIntoBuffer(tempIPV6, 16);
				ip = new IPAddress(tempIPV6);
			}

			var port = stream.ReadUInt16();

			this.Endpoint = new IPEndPoint(ip, port);
		}

		public void WriteData(ByteArrayReaderWriter stream)
		{
			stream.Write((byte)this.AddressType);
			stream.Write(this.Endpoint.Address.GetAddressBytes());
			stream.Write((ushort)this.Endpoint.Port);
		}
	}
}
