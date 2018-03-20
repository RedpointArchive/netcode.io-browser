using System;
using Org.BouncyCastle.Utilities;

namespace Org.BouncyCastle.Crypto.Parameters
{
    public class ParametersWithIV
		: ICipherParameters
    {
		private ICipherParameters	parameters;
		private byte[]				iv;

		public ParametersWithIV(
            ICipherParameters	parameters,
            byte[]				iv)
			: this(parameters, iv, 0, iv.Length)
		{
		}

		public ParametersWithIV(
            ICipherParameters	parameters,
            byte[]				iv,
            int					ivOff,
            int					ivLen)
        {
            // NOTE: 'parameters' may be null to imply key re-use
			if (iv == null)
				throw new ArgumentNullException("iv");

			this.parameters = parameters;
			this.iv = new byte[ivLen];
            Array.Copy(iv, ivOff, this.iv, 0, ivLen);
        }

        public void Set( ICipherParameters parameters, byte[] iv )
        {
            this.parameters = parameters;
            this.iv = BufferPool.GetBuffer(iv.Length);
            Array.Copy(iv, 0, this.iv, 0, this.iv.Length);
        }

        public void Reset()
        {
            BufferPool.ReturnBuffer(this.iv);
            this.iv = null;
        }

		public byte[] GetIV()
        {
            //return (byte[]) iv.Clone();
            return iv;
        }

		public ICipherParameters Parameters
        {
            get { return parameters; }
        }
    }
}
