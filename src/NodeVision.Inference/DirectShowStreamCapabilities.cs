using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using NodeVision.Core;

namespace NodeVision.Inference;

/// <summary>
/// Reads the capture formats a DirectShow device advertises through IAMStreamConfig, the same
/// list applications like OBS show in their resolution / frame-rate menu. The stream caps COM
/// interfaces are not surfaced by OpenCV, so this is hand-rolled like the name enumeration.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DirectShowStreamCapabilities
{
    private static readonly Guid BaseFilterId = new("56A86895-0AD4-11CE-B03A-0020AF0BA770");
    private static readonly Guid FormatVideoInfo = new("05589F80-C356-11CE-BF01-00AA0055595A");
    private static readonly Guid FormatVideoInfo2 = new("F72A76A0-EB0A-11D0-ACE4-0000C0CC16BA");

    public static IReadOnlyList<CameraMode> Read(IMoniker moniker)
    {
        var modes = new List<CameraMode>();
        object? filterObject = null;
        IEnumPins? pins = null;

        try
        {
            var filterId = BaseFilterId;
            moniker.BindToObject(null!, null!, ref filterId, out filterObject);
            if (filterObject is not IBaseFilter filter)
                return modes;

            filter.EnumPins(out pins);
            if (pins is null)
                return modes;

            var buffer = new IPin[1];
            while (pins.Next(1, buffer, IntPtr.Zero) == 0)
            {
                var pin = buffer[0];
                try
                {
                    if (pin.QueryDirection(out var direction) != 0 || direction != PinDirection.Output)
                        continue;

                    if (pin is not IAMStreamConfig config)
                        continue;

                    ReadCaps(config, modes);
                    if (modes.Count > 0)
                        break;
                }
                finally
                {
                    Marshal.ReleaseComObject(pin);
                }
            }
        }
        catch (COMException)
        {
            // A device that cannot be bound has no readable formats; the caller falls back to defaults.
        }
        catch (InvalidCastException)
        {
        }
        finally
        {
            if (pins is not null)
                Marshal.ReleaseComObject(pins);
            if (filterObject is not null)
                Marshal.ReleaseComObject(filterObject);
        }

        return Deduplicate(modes);
    }

    private static void ReadCaps(IAMStreamConfig config, List<CameraMode> modes)
    {
        if (config.GetNumberOfCapabilities(out var count, out var size) != 0 || count <= 0 || size <= 0)
            return;

        var caps = Marshal.AllocCoTaskMem(size);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (config.GetStreamCaps(i, out var mediaTypePtr, caps) != 0 || mediaTypePtr == IntPtr.Zero)
                    continue;

                try
                {
                    var mediaType = Marshal.PtrToStructure<AMMediaType>(mediaTypePtr);
                    if (mediaType.FormatPtr != IntPtr.Zero && TryReadFormat(mediaType, out var mode))
                        modes.Add(mode);
                }
                finally
                {
                    FreeMediaType(mediaTypePtr);
                }
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(caps);
        }
    }

    private static bool TryReadFormat(AMMediaType mediaType, out CameraMode mode)
    {
        if (mediaType.FormatType == FormatVideoInfo)
        {
            var header = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.FormatPtr);
            return BuildMode(header.AverageTimePerFrame, header.BitmapHeader, out mode);
        }

        if (mediaType.FormatType == FormatVideoInfo2)
        {
            var header = Marshal.PtrToStructure<VideoInfoHeader2>(mediaType.FormatPtr);
            return BuildMode(header.AverageTimePerFrame, header.BitmapHeader, out mode);
        }

        mode = null!;
        return false;
    }

    private static bool BuildMode(long averageTimePerFrame, BitmapInfoHeader bitmap, out CameraMode mode)
    {
        var width = bitmap.Width;
        var height = Math.Abs(bitmap.Height);
        if (width <= 0 || height <= 0)
        {
            mode = null!;
            return false;
        }

        var fps = averageTimePerFrame > 0 ? 10_000_000.0 / averageTimePerFrame : 0;
        mode = new CameraMode(width, height, Math.Round(fps, 2), FourCcToString(bitmap.Compression));
        return true;
    }

    private static string FourCcToString(uint compression)
    {
        if (compression == 0)
            return "RGB";

        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((compression >> (i * 8)) & 0xFF);
            if (c is < ' ' or > '~')
                return $"0x{compression:X8}";
            chars[i] = c;
        }

        return new string(chars);
    }

    private static void FreeMediaType(IntPtr mediaTypePtr)
    {
        var mediaType = Marshal.PtrToStructure<AMMediaType>(mediaTypePtr);
        if (mediaType.FormatPtr != IntPtr.Zero)
            Marshal.FreeCoTaskMem(mediaType.FormatPtr);
        Marshal.FreeCoTaskMem(mediaTypePtr);
    }

    private static IReadOnlyList<CameraMode> Deduplicate(List<CameraMode> modes)
    {
        var seen = new HashSet<string>();
        var result = new List<CameraMode>(modes.Count);

        foreach (var mode in modes)
        {
            var key = $"{mode.Width}x{mode.Height}@{mode.Fps:0.##}:{mode.PixelFormat}";
            if (seen.Add(key))
                result.Add(mode);
        }

        return result;
    }

    private enum PinDirection
    {
        Input = 0,
        Output = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AMMediaType
    {
        public Guid MajorType;
        public Guid Subtype;
        [MarshalAs(UnmanagedType.Bool)] public bool FixedSizeSamples;
        [MarshalAs(UnmanagedType.Bool)] public bool TemporalCompression;
        public uint SampleSize;
        public Guid FormatType;
        public IntPtr Unknown;
        public uint FormatSize;
        public IntPtr FormatPtr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VideoInfoHeader
    {
        public int SrcLeft;
        public int SrcTop;
        public int SrcRight;
        public int SrcBottom;
        public int TargetLeft;
        public int TargetTop;
        public int TargetRight;
        public int TargetBottom;
        public uint BitRate;
        public uint BitErrorRate;
        public long AverageTimePerFrame;
        public BitmapInfoHeader BitmapHeader;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VideoInfoHeader2
    {
        public int SrcLeft;
        public int SrcTop;
        public int SrcRight;
        public int SrcBottom;
        public int TargetLeft;
        public int TargetTop;
        public int TargetRight;
        public int TargetBottom;
        public uint BitRate;
        public uint BitErrorRate;
        public long AverageTimePerFrame;
        public uint InterlaceFlags;
        public uint CopyProtectFlags;
        public uint PictAspectRatioX;
        public uint PictAspectRatioY;
        public uint ControlFlags;
        public uint Reserved2;
        public BitmapInfoHeader BitmapHeader;
    }

    // Method order matches the COM vtables; only EnumPins / QueryDirection / the stream caps
    // methods are actually called, but the preceding slots must be declared to reach them.
    [ComImport]
    [Guid("56A86891-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPin
    {
        [PreserveSig] int Connect(IPin pReceivePin, IntPtr mediaType);
        [PreserveSig] int ReceiveConnection(IPin pConnector, IntPtr mediaType);
        [PreserveSig] int Disconnect();
        [PreserveSig] int ConnectedTo(out IPin ppPin);
        [PreserveSig] int ConnectionMediaType(IntPtr mediaType);
        [PreserveSig] int QueryPinInfo(IntPtr pInfo);
        [PreserveSig] int QueryDirection(out PinDirection pPinDir);
        [PreserveSig] int QueryId(out IntPtr id);
        [PreserveSig] int QueryAccept(IntPtr mediaType);
        [PreserveSig] int EnumMediaTypes(out IntPtr ppEnum);
        [PreserveSig] int QueryInternalConnections(IntPtr apPin, ref int nPin);
        [PreserveSig] int EndOfStream();
        [PreserveSig] int BeginFlush();
        [PreserveSig] int EndFlush();
        [PreserveSig] int NewSegment(long tStart, long tStop, double dRate);
    }

    [ComImport]
    [Guid("56A86892-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumPins
    {
        [PreserveSig]
        int Next(int cPins, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IPin[] ppPins, IntPtr pcFetched);

        [PreserveSig] int Skip(int cPins);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumPins ppEnum);
    }

    [ComImport]
    [Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBaseFilter
    {
        [PreserveSig] int GetClassID(out Guid pClassId);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long dwStart);
        [PreserveSig] int GetState(int dwMilliSecsTimeout, out int pFilterState);
        [PreserveSig] int SetSyncSource(IntPtr pClock);
        [PreserveSig] int GetSyncSource(out IntPtr ppClock);
        [PreserveSig] int EnumPins(out IEnumPins ppEnum);
        [PreserveSig] int FindPin([MarshalAs(UnmanagedType.LPWStr)] string id, out IPin ppPin);
        [PreserveSig] int QueryFilterInfo(IntPtr pInfo);
        [PreserveSig] int JoinFilterGraph(IntPtr pGraph, IntPtr pName);
        [PreserveSig] int QueryVendorInfo(out IntPtr ppVendorInfo);
    }

    [ComImport]
    [Guid("C6E13340-30AC-11D0-A18C-00A0C9118956")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMStreamConfig
    {
        [PreserveSig] int SetFormat(IntPtr pmt);
        [PreserveSig] int GetFormat(out IntPtr pmt);
        [PreserveSig] int GetNumberOfCapabilities(out int piCount, out int piSize);
        [PreserveSig] int GetStreamCaps(int iIndex, out IntPtr ppmt, IntPtr pScc);
    }
}
