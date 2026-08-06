using System.Runtime.CompilerServices;

namespace NodeVision.Core;

/// <summary>
/// 1 producer multi consumer ring buffer of frames
/// </summary>
public sealed class WebcamFrameRingBuffer : IDisposable
{
    private readonly FrameSlot[] _slots;
    private readonly int _mask; // _slots.Length - 1 (power of 2)
    private long _writeIndex = -1;

    // per consumer index
    private readonly Dictionary<int, long> _readIndices = new();
    private readonly object _readIndicesLock = new();

    public int Capacity => _slots.Length;
    public int SlotCount => _slots.Length;

    public WebcamFrameRingBuffer(int capacity = 4)
    {
        if (capacity < 2)
            throw new ArgumentException("Capacity must be >= 2", nameof(capacity));
        
        // round up to power of 2 for fast masking
        var pow2 = 1;
        while (pow2 < capacity) pow2 <<= 1;
        
        _slots = new FrameSlot[pow2];
        _mask = pow2 - 1;
        
        // pre allocate
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new FrameSlot();
        }
    }

    /// <summary>
    /// register new consumer with id, sets its read idx to latest write
    /// </summary>
    public int RegisterConsumer()
    {
        lock (_readIndicesLock)
        {
            var id = _readIndices.Count;
            _readIndices[id] = _writeIndex;
            return id;
        }
    }
    
    public void UnregisterConsumer(int consumerId)
    {
        lock (_readIndicesLock)
        {
            _readIndices.Remove(consumerId);
        }
    }

    /// <summary>
    /// Writes new frame, overriding oldest
    /// </summary>
    public long TryWrite(in WebcamFrame frame)
    {
        var nextWriteIndex = _writeIndex + 1;
        var slotIndex = nextWriteIndex & _mask;
        
        var slot = _slots[slotIndex];
        if (slot.Buffer.Length < frame.BufferSize)
        {
            slot.Buffer = new byte[frame.BufferSize];
        }
        
        // copy frame data
        frame.BgraData.Span.CopyTo(slot.Buffer);
        slot.Width = frame.Width;
        slot.Height = frame.Height;
        slot.Stride = frame.Stride;
        slot.Timestamp = frame.Timestamp;
        slot.Sequence = nextWriteIndex;
        slot.IsValid = true;
        
        // release fence
        Volatile.Write(ref _writeIndex, nextWriteIndex);
        
        return nextWriteIndex;
    }

    /// <summary>
    /// Tries to read the latest frame for a consumer.
    /// Returns true if a new frame was available (sequence > consumer's last read).
    /// </summary>
    public bool TryRead(int consumerId, out WebcamFrame frame)
    {
        frame = default;

        // get fence
        var currentWriteIndex = Volatile.Read(ref _writeIndex);
        
        long consumerReadIndex;
        lock (_readIndicesLock)
        {
            if (!_readIndices.TryGetValue(consumerId, out consumerReadIndex))
                return false;
        }

        // no new frames
        if (currentWriteIndex <= consumerReadIndex)
            return false;
        
        var slotIndex = currentWriteIndex & _mask;
        var slot = _slots[slotIndex];
        
        if (!slot.IsValid)
            return false;

        // HACK: doesnt make a copy, so consumer has to use frame quickly else it gets overwritten
        frame = WebcamFrame.Wrap(
            slot.Timestamp,
            slot.Width,
            slot.Height,
            slot.Stride,
            slot.Buffer.AsMemory(0, slot.Width * slot.Height * 4));
        
        lock (_readIndicesLock)
        {
            _readIndices[consumerId] = currentWriteIndex;
        }

        return true;
    }

    /// <summary>
    /// number of frames consumer is behind
    /// </summary>
    public long GetLag(int consumerId)
    {
        var currentWriteIndex = Volatile.Read(ref _writeIndex);
        
        lock (_readIndicesLock)
        {
            if (!_readIndices.TryGetValue(consumerId, out var readIndex))
                return 0;
            return currentWriteIndex - readIndex;
        }
    }

    /// <summary>
    /// Gets latest frame without increasing read idx, for most up to date frame
    /// </summary>
    public bool TryPeekLatest(out WebcamFrame frame)
    {
        frame = default;
        
        var currentWriteIndex = Volatile.Read(ref _writeIndex);
        if (currentWriteIndex < 0)
            return false;

        var slotIndex = currentWriteIndex & _mask;
        var slot = _slots[slotIndex];
        
        if (!slot.IsValid)
            return false;

        frame = WebcamFrame.Wrap(
            slot.Timestamp,
            slot.Width,
            slot.Height,
            slot.Stride,
            slot.Buffer.AsMemory(0, slot.Width * slot.Height * 4));
        
        return true;
    }

    public void Dispose()
    {
        // gc clears buffers
    }

    private sealed class FrameSlot
    {
        public byte[] Buffer = Array.Empty<byte>();
        public int Width;
        public int Height;
        public int Stride;
        public long Timestamp;
        public long Sequence;
        public bool IsValid;
    }
}