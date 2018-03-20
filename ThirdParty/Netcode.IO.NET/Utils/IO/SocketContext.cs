using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

using System.Threading;

namespace NetcodeIO.NET.Utils.IO
{
	internal interface ISocketContext : IDisposable
	{
		int BoundPort { get; }
		void Close();
		void Bind(EndPoint endpoint);
		void SendTo(byte[] data, EndPoint remoteEP);
		void SendTo(byte[] data, int length, EndPoint remoteEP);
		bool Read(out Datagram packet);
		void Pump();
	}

	internal class UDPSocketContext : ISocketContext
	{
		public int BoundPort
		{
			get
			{
				return ((IPEndPoint)internalSocket.LocalEndPoint).Port;
			}
		}

		private Socket internalSocket;
		private Thread socketThread;

		private DatagramQueue datagramQueue;

		public UDPSocketContext(AddressFamily addressFamily)
		{
			datagramQueue = new DatagramQueue();
			internalSocket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
		}

		public void Bind(EndPoint endpoint)
		{
			internalSocket.Bind(endpoint);

			socketThread = new Thread(runSocket);
			socketThread.Start();
		}

		public void SendTo(byte[] data, EndPoint remoteEP)
		{
			internalSocket.SendTo(data, remoteEP);
		}

		public void SendTo(byte[] data, int length, EndPoint remoteEP)
		{
			internalSocket.SendTo(data, length, SocketFlags.None, remoteEP);
		}

		public void Pump()
		{
		}

		public bool Read(out Datagram packet)
		{
			if (datagramQueue.Count > 0)
			{
				packet = datagramQueue.Dequeue();
				return true;
			}

			packet = new Datagram();
			return false;
		}

		public void Close()
		{
			internalSocket.Close();
		}

		public void Dispose()
		{
			Close();
		}

		private void runSocket()
		{
			while (true)
			{
				try
				{
					datagramQueue.ReadFrom(internalSocket);
				}
				catch (Exception e)
				{
					if (e is SocketException)
					{
						var socketException = e as SocketException;
						if (socketException.SocketErrorCode == SocketError.ConnectionReset) continue;
					}
					return;
				}
			}
		}
	}

	internal class NetworkSimulatorSocketManager
	{
		public int LatencyMS = 0;
		public int JitterMS = 0;
		public int PacketLossChance = 0;
		public int DuplicatePacketChance = 0;

		public bool AutoTime = true;

		public double Time
		{
			get
			{
				if (AutoTime)
					return DateTime.Now.GetTotalSeconds();
				else
					return time;
			}
		}

		private Dictionary<EndPoint, NetworkSimulatorSocketContext> sockets = new Dictionary<EndPoint, NetworkSimulatorSocketContext>();
		private double time;

		public void Update(double time)
		{
			this.time = time;
		}

		public NetworkSimulatorSocketContext CreateContext(EndPoint endpoint)
		{
			var socket = new NetworkSimulatorSocketContext();
			socket.Manager = this;

			return socket;
		}

		public void ChangeContext(NetworkSimulatorSocketContext socket, EndPoint endpoint)
		{
			if (sockets.ContainsKey(endpoint))
				throw new SocketException();

			sockets.Add(endpoint, socket);
		}

		public void RemoveContext(EndPoint endpoint)
		{
			sockets.Remove(endpoint);
		}

		public NetworkSimulatorSocketContext FindContext(EndPoint endpoint)
		{
			if (!sockets.ContainsKey(endpoint))
				return null;

			return sockets[endpoint];
		}
	}

	internal class NetworkSimulatorSocketContext : ISocketContext
	{
		public int BoundPort
		{
			get { return ((IPEndPoint)endpoint).Port; }
		}

		private struct simulatedPacket
		{
			public double receiveTime;
			public byte[] packetData;
			public EndPoint sender;
		}

		public NetworkSimulatorSocketManager Manager;

		private EndPoint endpoint;
		private List<simulatedPacket> simulatedPackets = new List<simulatedPacket>();
		private DatagramQueue datagramQueue = new DatagramQueue();
		private object mutex = new object();

		private Random rand = new Random();
		private bool running = false;

		public void Bind(EndPoint endpoint)
		{
			if (this.endpoint != null && this.endpoint.Equals(endpoint)) return;

			this.running = true;
			this.endpoint = endpoint;

			Manager.ChangeContext(this, this.endpoint);
		}

		public void SimulateReceive(byte[] packetData, EndPoint sender)
		{
			if (!running) return;

			double receiveTime = Manager.Time;

			// add latency+jitter to receive time
			receiveTime += (Manager.LatencyMS / 1000.0) + (rand.Next(-Manager.JitterMS, Manager.JitterMS) / 2000.0);

			lock (mutex)
			{
				simulatedPackets.Add(new simulatedPacket()
				{
					receiveTime = receiveTime,
					packetData = packetData,
					sender = sender
				});
			}
		}

		public void SendTo(byte[] data, EndPoint remoteEP)
		{
			if (!running) throw new SocketException();

			// randomly drop packets
			if (rand.Next(100) < Manager.PacketLossChance)
			{
				return;
			}

			byte[] temp = new byte[data.Length];
			Buffer.BlockCopy(data, 0, temp, 0, data.Length);

			var endSocket = Manager.FindContext(remoteEP);

			if (endSocket != null)
			{
				endSocket.SimulateReceive(temp, this.endpoint);

				// randomly duplicate packets
				if (rand.Next(100) < Manager.DuplicatePacketChance)
					endSocket.SimulateReceive(temp, this.endpoint);
			}
		}

		public void SendTo(byte[] data, int length, EndPoint remoteEP)
		{
			if (!running) throw new SocketException();

			byte[] temp = new byte[length];
			Buffer.BlockCopy(data, 0, temp, 0, length);

			SendTo(temp, remoteEP);
		}

		public void Pump()
		{
			if (simulatedPackets.Count > 0)
			{
				lock (simulatedPackets)
				{
					// enqueue packets ready to be received
					for (int i = 0; i < simulatedPackets.Count; i++)
					{
						if (Manager.Time >= simulatedPackets[i].receiveTime)
						{
							var receivePacket = simulatedPackets[i];
							simulatedPackets.RemoveAt(i);

							byte[] receiveBuffer = BufferPool.GetBuffer(2048);
							Buffer.BlockCopy(receivePacket.packetData, 0, receiveBuffer, 0, receivePacket.packetData.Length);

							Datagram datagram = new Datagram();
							datagram.payload = receiveBuffer;
							datagram.payloadSize = receivePacket.packetData.Length;
							datagram.sender = receivePacket.sender;
							datagramQueue.Enqueue(datagram);
						}
					}
				}
			}
		}

		public bool Read(out Datagram packet)
		{
			if (datagramQueue.Count > 0)
			{
				packet = datagramQueue.Dequeue();
				return true;
			}

			packet = new Datagram();
			return false;
		}

		public void Close()
		{
			running = false;
			Manager.RemoveContext(this.endpoint);
		}

		public void Dispose()
		{
			Close();
		}
	}
}
