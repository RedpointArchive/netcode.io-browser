namespace NetcodeIO.NET.Internal
{
	/// <summary>
	/// Helper class for protecting against packet replay
	/// </summary>
	internal sealed class NetcodeReplayProtection
	{
		private const int NETCODE_REPLAY_PROTECTION_BUFFER_SIZE = 256;

		public ulong mostRecentSequence;
		public ulong[] receivedPackets;

		public NetcodeReplayProtection()
		{
			mostRecentSequence = 0;
			receivedPackets = new ulong[NETCODE_REPLAY_PROTECTION_BUFFER_SIZE];

			Reset();
		}

		/// <summary>
		/// Reset the packet replay buffer
		/// </summary>
		public void Reset()
		{
			mostRecentSequence = 0;
			for (int i = 0; i < receivedPackets.Length; i++)
				receivedPackets[i] = ulong.MaxValue;
		}

		/// <summary>
		/// Check if the given packet was already received. If not, store it in the replay buffer.
		/// </summary>
		public bool AlreadyReceived(ulong sequence)
		{
			if ((sequence & ((ulong)1 << 63)) != 0)
				return false;

			if (sequence + NETCODE_REPLAY_PROTECTION_BUFFER_SIZE <= mostRecentSequence)
				return true;

			int index = (int)(sequence % NETCODE_REPLAY_PROTECTION_BUFFER_SIZE);
			if (receivedPackets[index] == 0xFFFFFFFFFFFFFFFF)
			{
				receivedPackets[index] = sequence;
				return false;
			}

			if (receivedPackets[index] >= sequence)
				return true;

			receivedPackets[index] = sequence;
			return false;
		}
	}
}
