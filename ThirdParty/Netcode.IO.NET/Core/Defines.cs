namespace NetcodeIO.NET
{
	internal static class Defines
	{
		public const int NETCODE_CONNECT_TOKEN_PRIVATE_BYTES = 1024;
		public const int NETCODE_CONNECT_TOKEN_PUBLIC_BYTES = 2048;
		public const int MAC_SIZE = 16;
		public const int MAX_PAYLOAD_SIZE = 1200;

		public const string NETCODE_VERSION_INFO_STR = "NETCODE 1.01\0";
		public const int NETCODE_VERSION_INFO_BYTES = 13;

		public const int MAX_SERVER_ADDRESSES = 32;
		public const int NUM_DISCONNECT_PACKETS = 10;

		public const int NETCODE_TIMEOUT_SECONDS = 10;
	}
}
