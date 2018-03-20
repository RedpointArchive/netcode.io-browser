using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace NetcodeIO.NET.Utils.IO
{
	/// <summary>
	/// Helper class for a quick non-allocating way to read or write from/to temporary byte arrays as streams
	/// </summary>
	public class ByteArrayReaderWriter : IDisposable
	{
		protected static Queue<ByteArrayReaderWriter> readerPool = new Queue<ByteArrayReaderWriter>();

		/// <summary>
		/// Get a reader/writer for the given byte array
		/// </summary>
		public static ByteArrayReaderWriter Get(byte[] byteArray)
		{
			ByteArrayReaderWriter reader = null;

			lock (readerPool)
			{
				if (readerPool.Count > 0)
				{
					reader = readerPool.Dequeue();
				}
				else
				{
					reader = new ByteArrayReaderWriter();
				}
			}

			reader.SetStream(byteArray);
			return reader;
		}

		/// <summary>
		/// Release a reader/writer to the pool
		/// </summary>
		public static void Release(ByteArrayReaderWriter reader)
		{
			if (reader == null)
				throw new NullReferenceException();

			lock (readerPool)
				readerPool.Enqueue(reader);
		}

		public long ReadPosition
		{
			get { return readStream.Position; }
		}

		public long WritePosition
		{
			get { return writeStream.Position; }
		}

		protected ByteStream readStream;
		protected ByteStream writeStream;

		public ByteArrayReaderWriter()
		{
			this.readStream = new ByteStream();
			this.writeStream = new ByteStream();
		}

		public void SetStream(byte[] byteArray)
		{
			this.readStream.SetStreamSource(byteArray);
			this.writeStream.SetStreamSource(byteArray);
		}

		public void Write(byte val) { writeStream.Write(val); }
		public void Write(byte[] val) { writeStream.Write(val); }
		public void Write(char val) { writeStream.Write(val); }
		public void Write(char[] val) { writeStream.Write(val); }
		public void Write(string val) { writeStream.Write(val); }
		public void Write(short val) { writeStream.Write(val); }
		public void Write(int val) { writeStream.Write(val); }
		public void Write(long val) { writeStream.Write(val); }
		public void Write(ushort val) { writeStream.Write(val); }
		public void Write(uint val) { writeStream.Write(val); }
		public void Write(ulong val) { writeStream.Write(val); }

		public void WriteASCII(char[] chars)
		{
			for (int i = 0; i < chars.Length; i++)
			{
				byte asciiCode = (byte)(chars[i] & 0xFF);
				Write(asciiCode);
			}
		}

		public void WriteASCII(string str)
		{
			for (int i = 0; i < str.Length; i++)
			{
				byte asciiCode = (byte)(str[i] & 0xFF);
				Write(asciiCode);
			}
		}

		public void WriteBuffer(byte[] buffer, int length)
		{
			for (int i = 0; i < length; i++)
				Write(buffer[i]);
		}

		public byte ReadByte() { return readStream.ReadByte(); }
		public byte[] ReadBytes(int length) { return readStream.ReadBytes(length); }
		public char ReadChar() { return readStream.ReadChar(); }
		public char[] ReadChars(int length) { return readStream.ReadChars(length); }
		public string ReadString() { return readStream.ReadString(); }
		public short ReadInt16() { return readStream.ReadInt16(); }
		public int ReadInt32() { return readStream.ReadInt32(); }
		public long ReadInt64() { return readStream.ReadInt64(); }
		public ushort ReadUInt16() { return readStream.ReadUInt16(); }
		public uint ReadUInt32() { return readStream.ReadUInt32(); }
		public ulong ReadUInt64() { return readStream.ReadUInt64(); }

		public void ReadASCIICharsIntoBuffer(char[] buffer, int length)
		{
			for (int i = 0; i < length; i++)
				buffer[i] = (char)ReadByte();
		}

		public void ReadBytesIntoBuffer(byte[] buffer, int length)
		{
			for (int i = 0; i < length; i++)
				buffer[i] = ReadByte();
		}

		public void Dispose()
		{
			Release(this);
		}
	}
}
