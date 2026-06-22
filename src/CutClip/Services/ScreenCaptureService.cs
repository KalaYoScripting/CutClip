using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using CutClip.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace CutClip.Services;

public sealed class ScreenCaptureService : IDisposable
{
    private IDirect3DDevice? _device;
    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _captureItem;
    private ID3D11Texture2D? _stagingTexture;
    private int _cropX;
    private int _cropY;
    private int _cropWidth;
    private int _cropHeight;
    private long _lastFrameTicks;
    private long _frameIntervalTicks;
    private bool _disposed;

    public bool IsSupported => GraphicsCaptureSession.IsSupported();

    public void Start(System.Windows.Rect region, int fps, BlockingCollection<CaptureFrame> frameQueue, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Windows Graphics Capture requires Windows 10 version 1903 or later.");
        }

        _cropWidth = Math.Max(1, (int)region.Width);
        _cropHeight = Math.Max(1, (int)region.Height);
        _frameIntervalTicks = Stopwatch.Frequency / Math.Max(1, fps);
        _lastFrameTicks = 0;

        var centerX = (int)(region.X + region.Width / 2);
        var centerY = (int)(region.Y + region.Height / 2);
        var monitorHandle = MonitorHelper.GetMonitorHandleForPoint(centerX, centerY);
        _captureItem = CaptureHelper.CreateItemForMonitor(monitorHandle);

        var monitorBounds = MonitorHelper.GetMonitorBounds(monitorHandle);
        _cropX = (int)(region.X - monitorBounds.Left);
        _cropY = (int)(region.Y - monitorBounds.Top);

        CreateD3DDevice();

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device!,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _captureItem.Size);

        _framePool.FrameArrived += (_, _) =>
        {
            if (cancellationToken.IsCancellationRequested || frameQueue.IsAddingCompleted)
            {
                return;
            }

            try
            {
                using var frame = _framePool.TryGetNextFrame();
                if (frame is null)
                {
                    return;
                }

                var now = Stopwatch.GetTimestamp();
                if (_lastFrameTicks != 0 && now - _lastFrameTicks < _frameIntervalTicks)
                {
                    return;
                }

                _lastFrameTicks = now;

                var cropped = CropFrame(frame);
                if (cropped is not null && !frameQueue.IsAddingCompleted)
                {
                    if (!frameQueue.TryAdd(cropped, 0))
                    {
                        cropped.Dispose();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Shutting down
            }
        };

        _session = _framePool.CreateCaptureSession(_captureItem);
        _session.IsCursorCaptureEnabled = true;
        DisableSystemCaptureBorder(_session);
        _session.StartCapture();
    }

    public void Stop()
    {
        try
        {
            _session?.Dispose();
            _framePool?.Dispose();
            _captureItem = null;
        }
        catch
        {
            // ignore during shutdown
        }
        finally
        {
            _session = null;
            _framePool = null;
        }
    }

    private static void DisableSystemCaptureBorder(GraphicsCaptureSession session)
    {
        try
        {
            var property = session.GetType().GetProperty("IsBorderRequired");
            property?.SetValue(session, false);
        }
        catch
        {
            // Requires Windows 11 22H2+ with a recent Windows SDK.
        }
    }

    private CaptureFrame? CropFrame(Direct3D11CaptureFrame frame)
    {
        var surface = frame.Surface;
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var guid = typeof(ID3D11Texture2D).GUID;
        var texturePtr = access.GetInterface(ref guid);
        using var sourceTexture = new ID3D11Texture2D(texturePtr);

        EnsureStagingTexture(sourceTexture);

        var box = new Box(
            _cropX,
            _cropY,
            0,
            _cropX + _cropWidth,
            _cropY + _cropHeight,
            1);

        _d3dContext!.CopySubresourceRegion(_stagingTexture!, 0, 0, 0, 0, sourceTexture, 0, box);

        var mapped = _d3dContext.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var rowPitch = mapped.RowPitch;
            var data = new byte[_cropWidth * _cropHeight * 4];

            for (var y = 0; y < _cropHeight; y++)
            {
                var source = IntPtr.Add(mapped.DataPointer, y * (int)rowPitch);
                Marshal.Copy(source, data, y * _cropWidth * 4, _cropWidth * 4);
            }

            return new CaptureFrame(data, _cropWidth, _cropHeight);
        }
        finally
        {
            _d3dContext.Unmap(_stagingTexture!, 0);
        }
    }

    private void EnsureStagingTexture(ID3D11Texture2D source)
    {
        if (_stagingTexture is not null)
        {
            return;
        }

        var desc = source.Description;
        desc.Width = (uint)_cropWidth;
        desc.Height = (uint)_cropHeight;
        desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read;
        desc.Usage = ResourceUsage.Staging;
        desc.MiscFlags = ResourceOptionFlags.None;

        _stagingTexture = _d3dDevice!.CreateTexture2D(desc);
    }

    private void CreateD3DDevice()
    {
        D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            Array.Empty<FeatureLevel>(),
            out _d3dDevice,
            out _d3dContext).CheckError();

        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var nativeDevice);
        if (hr != 0)
        {
            throw new COMException($"CreateDirect3D11DeviceFromDXGIDevice failed: 0x{hr:X8}");
        }

        _device = MarshalInterface<IDirect3DDevice>.FromAbi(nativeDevice);
        Marshal.Release(nativeDevice);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _stagingTexture?.Dispose();
        _d3dContext?.Dispose();
        _d3dDevice?.Dispose();
        _device?.Dispose();
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}

public sealed class CaptureFrame : IDisposable
{
    public CaptureFrame(byte[] data, int width, int height)
    {
        Data = data;
        Width = width;
        Height = height;
    }

    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }

    public void Dispose()
    {
        // byte[] is managed; nothing to dispose
    }
}

internal static class CaptureHelper
{
    private static readonly Guid IGraphicsCaptureItemInteropGuid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow([In] IntPtr window, [In] ref Guid iid, [Out] out IntPtr result);

        [PreserveSig]
        int CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid, [Out] out IntPtr result);
    }

    // IID of Windows.Graphics.Capture.IGraphicsCaptureItem (the ABI interface),
    // required as the riid for IGraphicsCaptureItemInterop.CreateForMonitor/Window.
    private static readonly Guid IGraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitorHandle)
    {
        var itemGuid = IGraphicsCaptureItemGuid;
        var interopGuid = IGraphicsCaptureItemInteropGuid;

        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        WindowsCreateString(className, className.Length, out var hClassName);
        try
        {
            var hr = RoGetActivationFactory(hClassName, ref interopGuid, out var factoryPtr);
            if (hr != 0)
            {
                throw new COMException($"RoGetActivationFactory failed: 0x{hr:X8}");
            }

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            Marshal.Release(factoryPtr);

            hr = interop.CreateForMonitor(monitorHandle, ref itemGuid, out var itemPtr);
            if (hr != 0)
            {
                throw new COMException($"CreateForMonitor failed: 0x{hr:X8}");
            }

            try
            {
                return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(itemPtr);
            }
        }
        finally
        {
            WindowsDeleteString(hClassName);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length,
        out IntPtr hString);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hString);
}

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    IntPtr GetInterface([In] ref Guid iid);
}
