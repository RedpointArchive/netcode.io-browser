using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Org.BouncyCastle.Utilities
{
	/// <summary>
	/// Helper methods for allocating temporary buffers
	/// </summary>
	public static class BufferPool
	{
		private static Dictionary<int, Queue<byte[]>> bufferPool = new Dictionary<int, Queue<byte[]>>();

		/// <summary>
		/// Retrieve a buffer of the given size
		/// </summary>
		public static byte[] GetBuffer(int size)
		{
			lock(bufferPool)
			{
				if (bufferPool.ContainsKey(size))
				{
					if (bufferPool[size].Count > 0)
						return bufferPool[size].Dequeue();
				}
			}

			return new byte[size];
		}

		/// <summary>
		/// Return a buffer to the pool
		/// </summary>
		public static void ReturnBuffer(byte[] buffer)
		{
			lock(bufferPool)
			{
				if (!bufferPool.ContainsKey(buffer.Length))
					bufferPool.Add(buffer.Length, new Queue<byte[]>());

				System.Array.Clear(buffer, 0, buffer.Length);
				bufferPool[buffer.Length].Enqueue(buffer);
			}
		}
	}
}
