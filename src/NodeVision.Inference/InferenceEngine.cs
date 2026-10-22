using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using NodeVision.Core;

namespace NodeVision.Inference;

public sealed class InferenceEngine : IDisposable
{
    private readonly WebcamFrameRingBuffer _ringBuffer;
    private readonly int _consumerId;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _inferenceTask;
    private readonly InferenceSession _palmSession;
    private readonly InferenceSession _landmarkSession;
    private readonly PalmDetector _palmDetector;
    private readonly HandLandmarkDetector _handLandmarkDetector;
    private volatile bool _isRunning;

    public event Action<InferenceResult>? InferenceCompleted;
    public event Action<Exception>? Error;

    /// <summary>When true the inference loop idles, so capture/render can be measured alone.</summary>
    public bool Paused { get; set; }

    /// <summary>Last frame's palm-detection time in milliseconds (perf diagnostics).</summary>
    public double PalmMs { get; private set; }

    /// <summary>Last frame's landmark-detection time in milliseconds (perf diagnostics).</summary>
    public double LandmarkMs { get; private set; }

    /// <summary>Last frame's total detection time in milliseconds (perf diagnostics).</summary>
    public double TotalMs { get; private set; }

    public InferenceEngine(WebcamFrameRingBuffer ringBuffer)
        : this(ringBuffer, Path.Combine(AppContext.BaseDirectory, "Models"))
    {
    }

    public InferenceEngine(WebcamFrameRingBuffer ringBuffer, string modelsDirectory)
    {
        _ringBuffer = ringBuffer;
        _consumerId = ringBuffer.RegisterConsumer();

        _palmSession = new InferenceSession(Path.Combine(modelsDirectory, "hand_detector.onnx"));
        _landmarkSession = new InferenceSession(Path.Combine(modelsDirectory, "hand_landmarks_detector.onnx"));
        _palmDetector = new PalmDetector(_palmSession);
        _handLandmarkDetector = new HandLandmarkDetector(_landmarkSession);

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
                if (Paused)
                {
                    await Task.Delay(50, _cts.Token).ConfigureAwait(false);
                    continue;
                }

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
    /// Runs the palm-detect -> landmark-decode pipeline on a frame. Palm and
    /// landmark inference are both synchronous CPU-bound ONNX Runtime calls;
    /// this already runs on InferenceLoopAsync's dedicated background task,
    /// so no further Task.Run offloading is needed.
    /// </summary>
    private ValueTask<InferenceResult?> RunInferenceAsync(WebcamFrame frame, CancellationToken ct)
    {
        var handLandmarks = Array.Empty<HandLandmark>();
        PalmDetection? palm = null;
        var presence = 0f;
        var handedness = 0f;

        var start = Stopwatch.GetTimestamp();

        var palmDetection = _palmDetector.Detect(frame);
        var afterPalm = Stopwatch.GetTimestamp();
        PalmMs = Stopwatch.GetElapsedTime(start, afterPalm).TotalMilliseconds;

        if (palmDetection is { } detectedPalm)
        {
            palm = detectedPalm;
            var hand = _handLandmarkDetector.Detect(frame, detectedPalm);
            var afterLandmark = Stopwatch.GetTimestamp();
            LandmarkMs = Stopwatch.GetElapsedTime(afterPalm, afterLandmark).TotalMilliseconds;

            if (hand is { } handResult)
            {
                handLandmarks = handResult.Landmarks;
                presence = handResult.Presence;
                handedness = handResult.HandednessScore;
            }
        }
        else
        {
            LandmarkMs = 0;
        }

        TotalMs = Stopwatch.GetElapsedTime(start, Stopwatch.GetTimestamp()).TotalMilliseconds;

        var result = new InferenceResult(
            Timestamp: frame.Timestamp,
            FrameWidth: frame.Width,
            FrameHeight: frame.Height,
            PoseLandmarks: Array.Empty<PoseLandmark>(),
            HandLandmarks: handLandmarks,
            Palm: palm,
            HandPresence: presence,
            HandednessScore: handedness);

        return ValueTask.FromResult<InferenceResult?>(result);
    }

    public void Dispose()
    {
        StopAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _ringBuffer.UnregisterConsumer(_consumerId);
        _palmSession.Dispose();
        _landmarkSession.Dispose();
    }
}


public readonly record struct InferenceResult(
    long Timestamp,
    int FrameWidth,
    int FrameHeight,
    PoseLandmark[] PoseLandmarks,
    HandLandmark[] HandLandmarks,
    PalmDetection? Palm,
    float HandPresence,
    float HandednessScore);

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