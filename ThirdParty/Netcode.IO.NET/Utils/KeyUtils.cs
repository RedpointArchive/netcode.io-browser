using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Security.Cryptography;

namespace NetcodeIO.NET.Utils
{
	/// <summary>
	/// Utility for generating crypto keys
	/// </summary>
	public static class KeyUtils
	{
		private static RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();

		/// <summary>
		/// Generate a random key
		/// </summary>
		public static void GenerateKey(byte[] keyBuffer)
		{
			rng.GetBytes(keyBuffer);
		}
	}
}
