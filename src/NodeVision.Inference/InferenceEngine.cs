using System;
using System.Threading;
using System.Threading.Tasks;
using NodeVision.Core;

namespace NodeVision.Inference;

public sealed class InferenceEngine : IDisposable
{
    private readonly WebcamFrameRingBuffer _ringBuffer;
    private readonly int _consumerId;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _inferenceTask;
    private volatile bool _isRunning;

    public event Action<InferenceResult>? InferenceCompleted;
    public event Action<Exception>? Error;

    public InferenceEngine(WebcamFrameRingBuffer ringBuffer)
    {
        _ringBuffer = ringBuffer;
        _consumerId = ringBuffer.RegisterConsumer();
        _inferenceTask = Task.Run(InferenceLoopAsync, _cts.Token);
    }

    /// <summary>
    /// Starts the inference loop (already started in constructor, but can be called explicitly).
    /// </summary>
    public void Start()
    {
        _isRunning = true;
    }

    /// <summary>
    /// Stops the inference loop.
    /// </summary>
    public async ValueTask StopAsync()
    {
        _isRunning = false;
        _cts.Cancel();
        
        try
        {
            await _inferenceTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task InferenceLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                if (_ringBuffer.TryRead(_consumerId, out var frame))
                {
                    // Process frame
                    var result = await RunInferenceAsync(frame, _cts.Token);
                    
                    if (result != null)
                    {
                        InferenceCompleted?.Invoke(result.Value);
                    }
                }
                else
                {
                    // No new frame, yield briefly
                    await Task.Delay(5, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                await Task.Delay(100, _cts.Token).ConfigureAwait(false); // Back off on error
            }
        }
    }

    /// <summary>
    /// Runs inference on a frame. Replace with actual model inference.
    /// </summary>
    private async ValueTask<InferenceResult?> RunInferenceAsync(WebcamFrame frame, CancellationToken ct)
    {
        // fake delay
        await Task.Delay(30, ct).ConfigureAwait(false);
        
        
        return new InferenceResult(
            Timestamp: frame.Timestamp,
            FrameWidth: frame.Width,
            FrameHeight: frame.Height,
            PoseLandmarks: Array.Empty<PoseLandmark>(),
            HandLandmarks: Array.Empty<HandLandmark>());
    }

    public void Dispose()
    {
        StopAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _ringBuffer.UnregisterConsumer(_consumerId);
    }
}


public readonly record struct InferenceResult(
    long Timestamp,
    int FrameWidth,
    int FrameHeight,
    PoseLandmark[] PoseLandmarks,
    HandLandmark[] HandLandmarks);

public readonly record struct PoseLandmark(
    float X,      // Normalized 0-1
    float Y,      // Normalized 0-1
    float Z,      // Normalized depth
    float Visibility,
    string Name); // e.g., "left_wrist"

public readonly record struct HandLandmark(
    float X,
    float Y,
    float Z,
    string Name); // e.g., "wrist", "index_finger_tip"