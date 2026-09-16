using Microsoft.ML.OnnxRuntime;
using NodeVision.Core;
using NodeVision.Inference;
using OpenCvSharp;

// Manual smoke test tool for the hand-tracking pipeline - not part of the
// app itself.
//
// Default mode: opens a live OpenCV preview window with the skeleton drawn
// on top in real time, so you can watch it work yourself. Press ESC or Q to
// close.
//
// `--capture` mode: the older one-shot behaviour - grabs a single frame and
// writes an annotated PNG next to this project. Useful for scripted/remote
// verification where nothing can look at a live window.

var modelsDir = Path.Combine(AppContext.BaseDirectory, "Models");

using var palmSession = new InferenceSession(Path.Combine(modelsDir, "hand_detector.onnx"));
using var landmarkSession = new InferenceSession(Path.Combine(modelsDir, "hand_landmarks_detector.onnx"));
var palmDetector = new PalmDetector(palmSession);
var landmarkDetector = new HandLandmarkDetector(landmarkSession);

if (args.Contains("--capture"))
{
    await RunSingleCaptureAsync(palmDetector, landmarkDetector);
}
else
{
    RunLivePreview(palmDetector, landmarkDetector);
}

static async Task RunSingleCaptureAsync(PalmDetector palmDetector, HandLandmarkDetector landmarkDetector)
{
    var outputPath = Path.Combine(AppContext.BaseDirectory, "palm_test_output.png");

    using var capture = new VideoCapture(0, VideoCaptureAPIs.MSMF);
    if (!capture.IsOpened())
    {
        Console.WriteLine("ERROR: could not open webcam device 0.");
        return;
    }

    Console.WriteLine("Warming up camera (letting auto-exposure settle)...");
    using var warmupFrame = new Mat();
    for (var i = 0; i < 30; i++)
    {
        capture.Read(warmupFrame);
        await Task.Delay(50);
    }

    Console.WriteLine("Capturing in 3...");
    await Task.Delay(1000);
    Console.WriteLine("2...");
    await Task.Delay(1000);
    Console.WriteLine("1...");
    await Task.Delay(1000);
    Console.WriteLine("Capturing now - show your hand to the camera!");

    using var bgrFrame = new Mat();
    capture.Read(bgrFrame);
    capture.Release();

    if (bgrFrame.Empty())
    {
        Console.WriteLine("ERROR: captured frame was empty.");
        return;
    }

    var frame = MatToWebcamFrame(bgrFrame);
    var hand = DetectAndDraw(bgrFrame, frame, palmDetector, landmarkDetector, verbose: true);

    if (hand is { } h)
    {
        Console.WriteLine($"Landmarks decoded: presence={h.Presence:F3} handedness={h.HandednessScore:F3}");
        foreach (var lm in h.Landmarks)
        {
            Console.WriteLine($"  {lm.Name,-18} x={lm.X:F0} y={lm.Y:F0} z={lm.Z:F1}");
        }
    }

    var saved = Cv2.ImWrite(outputPath, bgrFrame);
    Console.WriteLine(saved
        ? $"Saved annotated frame to: {outputPath}"
        : $"ERROR: Cv2.ImWrite returned false for path: {outputPath}");
}

static void RunLivePreview(PalmDetector palmDetector, HandLandmarkDetector landmarkDetector)
{
    using var capture = new VideoCapture(0, VideoCaptureAPIs.MSMF);
    if (!capture.IsOpened())
    {
        Console.WriteLine("ERROR: could not open webcam device 0.");
        return;
    }

    using var window = new Window("NodeVision - Hand Tracking Demo (ESC or Q to quit)");
    using var bgrFrame = new Mat();

    var gestureProcessor = new GestureProcessor();
    PinchEvent? lastPinch = null;
    var triggerFlashUntilMs = 0L;
    var handState = HandState.Unknown;

    gestureProcessor.PinchChanged += e => lastPinch = e;
    gestureProcessor.PinchTriggered += e =>
    {
        Console.WriteLine($"PINCH TRIGGERED at ({e.Position.X:F0},{e.Position.Y:F0})");
        triggerFlashUntilMs = Environment.TickCount64 + 500;
    };
    gestureProcessor.HandStateChanged += e =>
    {
        Console.WriteLine($"HAND STATE: {e.PreviousState} -> {e.NewState}");
        handState = e.NewState;
    };

    Console.WriteLine("Live preview running - show your hand to the camera. Press ESC or Q in the window to quit.");

    while (true)
    {
        capture.Read(bgrFrame);
        if (bgrFrame.Empty())
            continue;

        var frame = MatToWebcamFrame(bgrFrame);
        var hand = DetectAndDraw(bgrFrame, frame, palmDetector, landmarkDetector, verbose: false);
        gestureProcessor.Update(hand?.Landmarks ?? Array.Empty<HandLandmark>());

        DrawPinchOverlay(bgrFrame, lastPinch, triggerFlashUntilMs);
        DrawHandStateOverlay(bgrFrame, handState);
        DrawDiagnosticsOverlay(bgrFrame, gestureProcessor.LastDiagnostics);

        window.ShowImage(bgrFrame);

        var key = Cv2.WaitKey(1);
        if (key is 27 or 'q' or 'Q')
            break;
    }

    capture.Release();
}

static void DrawHandStateOverlay(Mat bgrFrame, HandState handState)
{
    var (text, color) = handState switch
    {
        HandState.Open => ("HAND: OPEN", new Scalar(0, 200, 0)),
        HandState.Closed => ("HAND: CLOSED (fist)", new Scalar(0, 0, 220)),
        HandState.Pointing => ("HAND: POINTING", new Scalar(255, 150, 0)),
        _ => ("HAND: (transitioning)", new Scalar(150, 150, 150)),
    };

    Cv2.PutText(bgrFrame, text, new Point(20, 380), HersheyFonts.HersheySimplex, 0.8, color, 2);
}

static void DrawDiagnosticsOverlay(Mat bgrFrame, HandStateDiagnostics? diagnostics)
{
    if (diagnostics is not { } d)
        return;

    static string Flag(string name, bool curled) => $"{name}:{(curled ? "curl" : "OUT")}";

    var line1 = $"{Flag("idx", d.IndexCurled)} {Flag("mid", d.MiddleCurled)} {Flag("ring", d.RingCurled)} {Flag("pky", d.PinkyCurled)} up:{d.IndexPointingUp}";
    var line2 = $"candidate={d.Candidate} confirmed={d.Confirmed}";

    Cv2.PutText(bgrFrame, line1, new Point(20, 20), HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 255), 1);
    Cv2.PutText(bgrFrame, line2, new Point(20, 42), HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 255), 1);
}

static void DrawPinchOverlay(Mat bgrFrame, PinchEvent? lastPinch, long triggerFlashUntilMs)
{
    const int barX = 20, barY = 460, barWidth = 200, barHeight = 24;
    Cv2.Rectangle(bgrFrame, new Point(barX, barY), new Point(barX + barWidth, barY + barHeight), new Scalar(80, 80, 80), -1);

    if (lastPinch is { } pinch)
    {
        var filledWidth = (int)(barWidth * Math.Clamp(pinch.Strength, 0f, 1f));
        Cv2.Rectangle(bgrFrame, new Point(barX, barY), new Point(barX + filledWidth, barY + barHeight), new Scalar(0, 200, 255), -1);
        Cv2.PutText(bgrFrame, $"pinch {pinch.Strength:F2}", new Point(barX, barY - 8),
            HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 1);
    }

    Cv2.Rectangle(bgrFrame, new Point(barX, barY), new Point(barX + barWidth, barY + barHeight), new Scalar(255, 255, 255), 1);

    if (Environment.TickCount64 < triggerFlashUntilMs)
    {
        Cv2.PutText(bgrFrame, "PINCH TRIGGERED!", new Point(20, 420),
            HersheyFonts.HersheySimplex, 1.1, new Scalar(0, 255, 0), 3);
    }
}

static WebcamFrame MatToWebcamFrame(Mat bgrFrame)
{
    using var bgraFrame = new Mat();
    Cv2.CvtColor(bgrFrame, bgraFrame, ColorConversionCodes.BGR2BGRA);

    var width = bgraFrame.Width;
    var height = bgraFrame.Height;
    var step = (int)bgraFrame.Step();
    var buffer = new byte[height * step];
    System.Runtime.InteropServices.Marshal.Copy(bgraFrame.Data, buffer, 0, buffer.Length);
    return WebcamFrame.Create(0, width, height, buffer);
}

static HandLandmarkResult? DetectAndDraw(
    Mat bgrFrame,
    WebcamFrame frame,
    PalmDetector palmDetector,
    HandLandmarkDetector landmarkDetector,
    bool verbose)
{
    var palm = palmDetector.Detect(frame);
    if (palm is not { } palmDetection)
    {
        if (verbose) Console.WriteLine("No hand detected in the captured frame.");
        Cv2.PutText(bgrFrame, "NO HAND DETECTED", new Point(20, 40),
            HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 0, 255), 2);
        return null;
    }

    if (verbose) Console.WriteLine($"Palm detected: score={palmDetection.Box.Score:F3}");
    Cv2.Rectangle(bgrFrame,
        new Point(palmDetection.Box.X1, palmDetection.Box.Y1),
        new Point(palmDetection.Box.X2, palmDetection.Box.Y2),
        new Scalar(255, 0, 0), 2);

    var landmarkResult = landmarkDetector.Detect(frame, palmDetection);
    if (landmarkResult is not { } hand)
    {
        if (verbose) Console.WriteLine("Palm box found, but landmark decode was rejected (low presence or crop out of frame).");
        Cv2.PutText(bgrFrame, "NO HAND (low confidence)", new Point(20, 70),
            HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 165, 255), 2);
        return null;
    }

    // Skeleton connections (standard MediaPipe HAND_CONNECTIONS).
    int[][] connections =
    [
        [0, 1], [0, 5], [9, 13], [13, 17], [5, 9], [0, 17], // palm
        [1, 2], [2, 3], [3, 4], // thumb
        [5, 6], [6, 7], [7, 8], // index
        [9, 10], [10, 11], [11, 12], // middle
        [13, 14], [14, 15], [15, 16], // ring
        [17, 18], [18, 19], [19, 20], // pinky
    ];

    foreach (var c in connections)
    {
        var a = hand.Landmarks[c[0]];
        var b = hand.Landmarks[c[1]];
        Cv2.Line(bgrFrame, new Point(a.X, a.Y), new Point(b.X, b.Y), new Scalar(0, 255, 0), 2);
    }

    foreach (var lm in hand.Landmarks)
    {
        Cv2.Circle(bgrFrame, new Point(lm.X, lm.Y), 4, new Scalar(0, 0, 255), -1);
    }

    return hand;
}
