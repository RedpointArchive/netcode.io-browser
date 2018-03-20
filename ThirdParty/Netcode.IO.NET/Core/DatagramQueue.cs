using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

using System.Threading;

using NetcodeIO.NET.Utils.IO;

namespace NetcodeIO.NET.Utils
{
	internal struct Datagram
	{
		public byte[] payload;
		public int payloadSize;
		public EndPoint sender;

		public void Release()
		{
			BufferPool.ReturnBuffer(payload);
		}
	}

	internal class DatagramQueue
	{
		protected Queue<Datagram> datagramQueue = new Queue<Datagram>();
		protected Queue<EndPoint> endpointPool = new Queue<EndPoint>();

		private object datagram_mutex = new object();
		private object endpoint_mutex = new object();

		public int Count
		{
			get
			{
				return datagramQueue.Count;
			}
		}

		public void Clear()
		{
			datagramQueue.Clear();
			endpointPool.Clear();
		}

		public void ReadFrom( Socket socket )
		{
			EndPoint sender;

			lock (endpoint_mutex)
			{
				if (endpointPool.Count > 0)
					sender = endpointPool.Dequeue();
				else
					sender = new IPEndPoint(IPAddress.Any, 0);
			}

			byte[] receiveBuffer = BufferPool.GetBuffer(2048);
			int recv = socket.ReceiveFrom(receiveBuffer, ref sender);

			if (recv > 0)
			{
				Datagram packet = new Datagram();
				packet.sender = sender;
				packet.payload = receiveBuffer;
				packet.payloadSize = recv;

				lock (datagram_mutex)
					datagramQueue.Enqueue(packet);
			}
		}

		public void Enqueue(Datagram datagram)
		{
			lock(datagram_mutex)
				datagramQueue.Enqueue(datagram);
		}

		public Datagram Dequeue()
		{
			lock(datagram_mutex)
				return datagramQueue.Dequeue();
		}
	}
}
