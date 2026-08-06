using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NodeVision.Core;
using OpenCvSharp;

namespace NodeVision.Inference;

/// <summary>
/// Configuration for webcam capture.
/// </summary>
public sealed record CaptureConfig(
    int DeviceIndex = 0,
    int Width = 1280,
    int Height = 720,
    int Fps = 30,
    VideoCaptureAPIs PreferredBackend = VideoCaptureAPIs.MSMF); // media foundatiuon api

public sealed class WebcamCaptureEvents
{
    public event Action<WebcamFrame>? FrameCaptured;
    public event Action<Exception>? Error;
    public event Action? Started;
    public event Action? Stopped;

    internal void OnFrameCaptured(WebcamFrame frame) => FrameCaptured?.Invoke(frame);
    internal void OnError(Exception ex) => Error?.Invoke(ex);
    internal void OnStarted() => Started?.Invoke();
    internal void OnStopped() => Stopped?.Invoke();
}

/// <summary>
/// Webcam capture service using OpenCvSharp
/// runs on background thread, pushes frames to ring buffer
/// </summary>
public sealed class WebcamCaptureService : IDisposable
{
    private readonly CaptureConfig _config;
    private readonly WebcamFrameRingBuffer _ringBuffer;
    private readonly WebcamCaptureEvents _events = new();
    
    private Task? _captureTask;
    private CancellationTokenSource? _cts;
    private volatile bool _isRunning;
    private VideoCapture? _capture;
    private Mat? _frame;
    private Mat? _bgraFrame;

    public WebcamCaptureEvents Events => _events;
    public WebcamFrameRingBuffer RingBuffer => _ringBuffer;
    public bool IsRunning => _isRunning;

    public WebcamCaptureService(CaptureConfig config, WebcamFrameRingBuffer? ringBuffer = null)
    {
        _config = config;
        _ringBuffer = ringBuffer ?? new WebcamFrameRingBuffer(4);
    }

    /// <summary>
    /// starts on bg thread
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureTask = Task.Run(CaptureLoopAsync, _cts.Token);
        
        // wait for first frame or error
        await WaitForFirstFrameAsync(_cts.Token);
    }
    
    public async ValueTask StopAsync()
    {
        if (!_isRunning)
            return;

        _cts?.Cancel();
        
        if (_captureTask != null)
        {
            try
            {
                await _captureTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }

        _cts?.Dispose();
        _cts = null;
        _captureTask = null;
    }

    public int RegisterConsumer()
    {
        return _ringBuffer.RegisterConsumer();
    }
    
    public void UnregisterConsumer(int consumerId)
    {
        _ringBuffer.UnregisterConsumer(consumerId);
    }

    private async ValueTask WaitForFirstFrameAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        using var reg = ct.Register(() => tcs.TrySetCanceled());
        
        void Handler(WebcamFrame _) => tcs.TrySetResult(true);
        _events.FrameCaptured += Handler;
        
        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _events.FrameCaptured -= Handler;
        }
    }

    private async Task CaptureLoopAsync()
    {
        try
        {
            _capture = new VideoCapture(_config.DeviceIndex, _config.PreferredBackend);
            
            _capture.Set(VideoCaptureProperties.FrameWidth, _config.Width);
            _capture.Set(VideoCaptureProperties.FrameHeight, _config.Height);
            _capture.Set(VideoCaptureProperties.Fps, _config.Fps);
            
            if (!_capture.IsOpened())
            {
                throw new InvalidOperationException($"Failed to open webcam device {_config.DeviceIndex}");
            }

            _isRunning = true;
            _events.OnStarted();

            _frame = new Mat();
            _bgraFrame = new Mat();
            
            var timestamp = 0L;
            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _config.Fps);
            var nextFrameTime = DateTime.UtcNow;

            while (!_cts!.Token.IsCancellationRequested)
            {
                var readResult = _capture.Read(_frame);
                
                if (!readResult || _frame.Empty())
                {
                    await Task.Delay(10, _cts.Token).ConfigureAwait(false);
                    continue;
                }
                
                Cv2.CvtColor(_frame, _bgraFrame, ColorConversionCodes.BGR2BGRA);

                // get frame data
                var width = _bgraFrame.Width;
                var height = _bgraFrame.Height;
                var step = _bgraFrame.Step(); // stride
                var dataPtr = _bgraFrame.Data;
                
                // copy to managed array
                var bufferSize = height * step;
                var buffer = new byte[bufferSize];
                Marshal.Copy(dataPtr, buffer, 0, (int)bufferSize);

                // create frame and push to ring buffer
                var webcamFrame = WebcamFrame.Create(timestamp++, width, height, buffer);
                _ringBuffer.TryWrite(webcamFrame);
                _events.OnFrameCaptured(webcamFrame);

                // rate limiting
                nextFrameTime += frameInterval;
                var delay = nextFrameTime - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _cts.Token).ConfigureAwait(false);
                }
                else
                {
                    nextFrameTime = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // on stop
        }
        catch (Exception ex)
        {
            _events.OnError(ex);
        }
        finally
        {
            _isRunning = false;
            
            _bgraFrame?.Dispose();
            _bgraFrame = null;
            _frame?.Dispose();
            _frame = null;
            _capture?.Dispose();
            _capture = null;
            
            _events.OnStopped();
        }
    }

    public void Dispose()
    {
        StopAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _cts?.Dispose();
    }
}