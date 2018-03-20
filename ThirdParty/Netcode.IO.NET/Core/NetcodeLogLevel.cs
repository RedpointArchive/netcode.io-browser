using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetcodeIO.NET
{
	/// <summary>
	/// Log level for Netcode.IO
	/// </summary>
	public enum NetcodeLogLevel
	{
		/// <summary>
		/// Disable log output
		/// </summary>
		None = 0,

		/// <summary>
		/// Log errors
		/// </summary>
		Error = 1,

		/// <summary>
		/// Log client connect/disconnect/errors
		/// </summary>
		Info = 2,

		/// <summary>
		/// Verbose logging for debug purposes
		/// </summary>
		Debug = 3
	}
}
