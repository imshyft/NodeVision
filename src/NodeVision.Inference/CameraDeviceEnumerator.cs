using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using NodeVision.Core;
using OpenCvSharp;

namespace NodeVision.Inference;

/// <summary>
/// Lists the cameras available to the capture service.
/// </summary>
public static class CameraDeviceEnumerator
{
    private const int MaxProbedDevices = 8;

    private static readonly Guid SystemDeviceEnumClassId = new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");

    /// <summary>
    /// Cameras in the order OpenCV's DirectShow backend indexes them, so a listed
    /// <see cref="CameraDeviceOption.DeviceIndex"/> can be opened as-is. Falls back to probing indices
    /// when the system enumerator is unavailable.
    /// </summary>
    public static IReadOnlyList<CameraDeviceOption> Enumerate()
    {
        var names = OperatingSystem.IsWindows() ? DirectShowNames() : Array.Empty<string>();

        if (names.Count > 0)
        {
            var devices = new List<CameraDeviceOption>(names.Count);
            for (var index = 0; index < names.Count; index++)
                devices.Add(new CameraDeviceOption(index, names[index]));

            return devices;
        }

        return ProbeDevices();
    }

    private static IReadOnlyList<CameraDeviceOption> ProbeDevices()
    {
        var devices = new List<CameraDeviceOption>();

        for (var index = 0; index < MaxProbedDevices; index++)
        {
            using var capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (capture.IsOpened())
                devices.Add(new CameraDeviceOption(index, $"Camera {index + 1}"));
        }

        return devices;
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> DirectShowNames()
    {
        var names = new List<string>();

        ICreateDevEnum? deviceEnumerator = null;
        IEnumMoniker? monikers = null;

        try
        {
            deviceEnumerator = (ICreateDevEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(SystemDeviceEnumClassId)!)!;

            var category = VideoInputDeviceCategory;
            if (deviceEnumerator.CreateClassEnumerator(ref category, out monikers, 0) != 0 || monikers is null)
                return names;

            var buffer = new IMoniker[1];
            while (monikers.Next(1, buffer, IntPtr.Zero) == 0)
            {
                var moniker = buffer[0];
                try
                {
                    var bagId = typeof(IPropertyBag).GUID;
                    moniker.BindToStorage(null!, null!, ref bagId, out var bagObject);

                    var bag = (IPropertyBag)bagObject;
                    try
                    {
                        object value = string.Empty;
                        if (bag.Read("FriendlyName", ref value, IntPtr.Zero) == 0 && value is string name && !string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(bag);
                    }
                }
                catch (COMException)
                {
                    // A moniker that cannot be read is not a usable camera; skip it.
                }
                finally
                {
                    Marshal.ReleaseComObject(moniker);
                }
            }
        }
        catch (COMException)
        {
            // No camera category on this machine; fall back to probing.
        }
        finally
        {
            if (monikers is not null)
                Marshal.ReleaseComObject(monikers);
            if (deviceEnumerator is not null)
                Marshal.ReleaseComObject(deviceEnumerator);
        }

        return names;
    }

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid pType, out IEnumMoniker ppEnumMoniker, int dwFlags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar, IntPtr pErrorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
    }
}
