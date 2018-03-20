using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;

namespace NetcodeIO.NET.Utils.IO
{
    /// <summary>
    /// A simple stream implementation for reading/writing from/to byte arrays which can be reused
    /// </summary>
    public class ByteStream : Stream
    {
        protected byte[] srcByteArray;

        public override long Position
        {
            get; set;
        }

        public override long Length
        {
            get
            {
                return srcByteArray.Length;
            }
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        /// <summary>
        /// Set a new byte array for this stream to read from
        /// </summary>
        public void SetStreamSource(byte[] sourceBuffer)
        {
            this.srcByteArray = sourceBuffer;
            this.Position = 0;
        }

        public byte[] ReadBytes(int length)
        {
            byte[] bytes = new byte[length];
            Read(bytes, 0, length);

            return bytes;
        }

        public char ReadChar()
        {
            int c = 0;

            for (int i = 0; i < sizeof(char); i++) {
                c |= ReadByte() << (i << 3);
            }

            return (char)c;
        }

        public char[] ReadChars(int length)
        {
            char[] chars = new char[length];

            for (int i = 0; i < length; i++)
                chars[i] = ReadChar();

            return chars;
        }

        public string ReadString()
        {
            uint len = ReadUInt32();
            char[] chars = ReadChars((int)len);
            return new string(chars);
        }

        public short ReadInt16()
        {
            int c = 0;

            for (int i = 0; i < sizeof(short); i++) {
                c |= ReadByte() << (i << 3);
            }

            return (short)c;
        }

        public int ReadInt32()
        {
            int c = 0;

            for (int i = 0; i < sizeof(int); i++) {
                c |= ReadByte() << (i << 3);
            }

            return c;
        }

        public long ReadInt64()
        {
            long c = 0;

            for (int i = 0; i < sizeof(long); i++) {
                c |= (long)ReadByte() << (i << 3);
            }

            return c;
        }

        public ushort ReadUInt16()
        {
            ushort c = 0;

            for (int i = 0; i < sizeof(ushort); i++) {
                c |= (ushort)(ReadByte() << (i << 3));
            }

            return c;
        }

        public uint ReadUInt32()
        {
            uint c = 0;

            for (int i = 0; i < sizeof(uint); i++) {
                c |= (uint)ReadByte() << (i << 3);
            }

            return c;
        }

        public ulong ReadUInt64()
        {
            ulong c = 0;

            for (int i = 0; i < sizeof(ulong); i++) {
                c |= (ulong)ReadByte() << (i << 3);
            }

            return c;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int readBytes = 0;
            long pos = this.Position;
            long len = this.Length;
            for (int i = 0; i < count && pos < len; i++) {
                buffer[i + offset] = srcByteArray[pos++];
                readBytes++;
            }

            this.Position = pos;
            return readBytes;
        }

        public new byte ReadByte()
        {
            long pos = this.Position;
            byte val = srcByteArray[pos++];
            this.Position = pos;

            return val;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
                WriteByte(buffer[i + offset]);
        }

        public override void WriteByte(byte value)
        {
            long pos = this.Position;
            srcByteArray[pos++] = value;
            this.Position = pos;
        }

        public void Write(byte val)
        {
            WriteByte(val);
        }

        public void Write(byte[] val)
        {
            Write(val, 0, val.Length);
        }

        public void Write(char val)
        {
            uint uval = (uint)val;
            for (int i = 0; i < sizeof(char); i++) {
                WriteByte((byte)(uval & 0xFF));
                uval >>= 8;
            }
        }

        public void Write(char[] val)
        {
            for (int i = 0; i < val.Length; i++) {
                Write(val[i]);
            }
        }

        public void Write(string val)
        {
            Write((uint)val.Length);
            for (int i = 0; i < val.Length; i++) {
                Write(val[i]);
            }
        }

        public void Write(short val)
        {
            for (int i = 0; i < sizeof(short); i++) {
                WriteByte((byte)(val & 0xFF));
                val >>= 8;
            }
        }

        public void Write(int val)
        {
            for (int i = 0; i < sizeof(int); i++) {
                WriteByte((byte)(val & 0xFF));
                val >>= 8;
            }
        }

        public void Write(long val)
        {
            for (int i = 0; i < sizeof(long); i++) {
                WriteByte((byte)(val & 0xFF));
                val >>= 8;
            }
        }

        public void Write(ushort val)
        {
            for (int i = 0; i < sizeof(ushort); i++) {
                WriteByte((byte)(val & 0xFF));
                val >>= 8;
            }
        }

        public void Write(uint val)
        {
            for (int i = 0; i < sizeof(uint); i++) {
                WriteByte((byte)(val & 0xFF));
                val >>= 8;
            }
        }

        public void Write(ulong val)
        {
            for (int i = 0; i < sizeof(ulong); i++) {
                WriteByte((byte)(val & 0xFF));
                val >>= 8;
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
                this.Position = offset;
            else if (origin == SeekOrigin.Current)
                this.Position += offset;
            else
                this.Position = this.Length - offset - 1;

            return this.Position;
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
    }
}
