namespace Protocol.Rudp;

public class NackTracker
{
    private class MissingFragState
    {
        public ushort FragmentID;
        public long LastNackSentTick;
        public int RetryCount;
    }

    private class FrameNackState
    {
        public uint FrameID;
        public ushort TotalFragments;
        public long FirstSeenTick;
        public ushort HighestFragmentSeen;
        public bool[] ReceivedFragments;
        public Dictionary<ushort, MissingFragState> Missing = new();

        public FrameNackState(uint frameId, ushort totalFragments)
        {
            FrameID = frameId;
            TotalFragments = totalFragments;
            FirstSeenTick = Environment.TickCount64;
            ReceivedFragments = new bool[Math.Max((int)totalFragments, 32)];
        }
    }

    private readonly Dictionary<uint, FrameNackState> _activeFrames = new();
    private readonly Lock _lock = new();

    private long _totalNacksSent;
    private long _totalMissingDetected;
    private long _totalRecovered;

    public bool IsEnabled { get; set; } = false;
    public int RtoMs { get; set; } = 15; // Retransmission timeout (ms)
    public int MaxRetries { get; set; } = 4;
    public int MaxFrameAgeMs { get; set; } = 200;

    public long TotalNacksSent => Interlocked.Read(ref _totalNacksSent);
    public long TotalMissingDetected => Interlocked.Read(ref _totalMissingDetected);
    public long TotalRecovered => Interlocked.Read(ref _totalRecovered);

    public event Action<uint, ushort[]>? NackRequested;

    /// <summary>
    /// Call when a fragment is received.
    /// </summary>
    public void OnFragmentReceived(uint frameId, ushort fragmentId, ushort totalFragments)
    {
        if (!IsEnabled) return;

        List<ushort>? newMissing = null;

        lock (_lock)
        {
            if (!_activeFrames.TryGetValue(frameId, out var state))
            {
                state = new FrameNackState(frameId, totalFragments);
                _activeFrames[frameId] = state;
            }

            if (fragmentId >= state.ReceivedFragments.Length)
            {
                Array.Resize(ref state.ReceivedFragments, Math.Max((int)totalFragments, fragmentId + 1));
            }

            // If this was a previously missing fragment, record recovery
            if (state.Missing.Remove(fragmentId))
            {
                Interlocked.Increment(ref _totalRecovered);
            }

            // Already received previously?
            if (state.ReceivedFragments[fragmentId])
            {
                return;
            }

            state.ReceivedFragments[fragmentId] = true;

            // Detect sequence gaps before this fragment
            if (fragmentId > state.HighestFragmentSeen)
            {
                for (ushort i = state.HighestFragmentSeen; i < fragmentId; i++)
                {
                    if (!state.ReceivedFragments[i] && !state.Missing.ContainsKey(i))
                    {
                        newMissing ??= [];
                        newMissing.Add(i);

                        state.Missing[i] = new MissingFragState
                        {
                            FragmentID = i,
                            LastNackSentTick = Environment.TickCount64,
                            RetryCount = 1
                        };
                        Interlocked.Increment(ref _totalMissingDetected);
                    }
                }

                state.HighestFragmentSeen = fragmentId;
            }
        }

        if (newMissing is { Count: > 0 })
        {
            Interlocked.Add(ref _totalNacksSent, newMissing.Count);
            NackRequested?.Invoke(frameId, newMissing.ToArray());
        }
    }

    /// <summary>
    /// Call when all fragments of a frame have been received and assembled.
    /// </summary>
    public void OnFrameCompleted(uint frameId)
    {
        lock (_lock)
        {
            _activeFrames.Remove(frameId);
        }
    }

    /// <summary>
    /// Check for unreceived tail fragments or retransmission timeouts.
    /// </summary>
    public void CheckTimeouts()
    {
        if (!IsEnabled) return;

        long now = Environment.TickCount64;
        var nacksToSend = new List<(uint FrameId, ushort[] Fragments)>();

        lock (_lock)
        {
            var framesToRemove = new List<uint>();

            foreach (var (frameId, state) in _activeFrames)
            {
                // Discard frames that are too old
                if (now - state.FirstSeenTick > MaxFrameAgeMs)
                {
                    framesToRemove.Add(frameId);
                    continue;
                }

                var missingForThisFrame = new List<ushort>();

                // Check missing tail fragments if some time has passed since first seeing this frame
                if (now - state.FirstSeenTick > RtoMs && state.HighestFragmentSeen < state.TotalFragments - 1)
                {
                    for (ushort i = (ushort)(state.HighestFragmentSeen + 1); i < state.TotalFragments; i++)
                    {
                        if (!state.ReceivedFragments[i] && !state.Missing.ContainsKey(i))
                        {
                            state.Missing[i] = new MissingFragState
                            {
                                FragmentID = i,
                                LastNackSentTick = now,
                                RetryCount = 1
                            };
                            missingForThisFrame.Add(i);
                            Interlocked.Increment(ref _totalMissingDetected);
                        }
                    }
                    state.HighestFragmentSeen = (ushort)(state.TotalFragments - 1);
                }

                // Check for retries on already identified missing fragments
                foreach (var (fragId, missing) in state.Missing)
                {
                    if (now - missing.LastNackSentTick >= RtoMs)
                    {
                        if (missing.RetryCount < MaxRetries)
                        {
                            missing.LastNackSentTick = now;
                            missing.RetryCount++;
                            if (!missingForThisFrame.Contains(fragId))
                            {
                                missingForThisFrame.Add(fragId);
                            }
                        }
                    }
                }

                if (missingForThisFrame.Count > 0)
                {
                    nacksToSend.Add((frameId, missingForThisFrame.ToArray()));
                }
            }

            foreach (var frameId in framesToRemove)
            {
                _activeFrames.Remove(frameId);
            }
        }

        foreach (var (frameId, frags) in nacksToSend)
        {
            Interlocked.Add(ref _totalNacksSent, frags.Length);
            NackRequested?.Invoke(frameId, frags);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _activeFrames.Clear();
        }
        Interlocked.Exchange(ref _totalNacksSent, 0);
        Interlocked.Exchange(ref _totalMissingDetected, 0);
        Interlocked.Exchange(ref _totalRecovered, 0);
    }
}
