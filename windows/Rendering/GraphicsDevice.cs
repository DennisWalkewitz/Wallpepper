using System.Reflection;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Wallpepper.Settings;

namespace Wallpepper.Rendering;

/// <summary>
/// The Direct3D 11 device with everything all draws share: the full-screen triangle, the constant buffers of the
/// Windows Terminal shader contract (Texture2D shaderTexture, SamplerState, cbuffer {Time, Scale, Resolution,
/// Background}), the filter pass. Used by one thread: the render thread, or a command such as "--bench".
/// </summary>
internal sealed class GraphicsDevice : IDisposable
{
    // Not "name.vs.hlsl" / "name.ps.hlsl": the build would take "ps" for a language code (Pashto) and move the file
    // into a satellite assembly.
    private const string ShaderResourcePrefix = "hlsl/";
    private const string VertexShaderFile = "FullscreenTriangle.hlsl";
    private const string FilterShaderFile = "FilterPass.hlsl";

    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11Buffer _shaderConstants;
    private readonly ID3D11Buffer _monitorConstants;
    private readonly ID3D11Buffer _filterConstants;
    private readonly ID3D11Texture2D _textLayer;
    private readonly ID3D11ShaderResourceView _textLayerView;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11RasterizerState _scissorState;

    private GraphicsDevice(ID3D11Device device, ID3D11DeviceContext context, bool software)
    {
        Device = device;
        Context = context;
        IsSoftware = software;
        try
        {
            bool modern = device.FeatureLevel >= FeatureLevel.Level_11_0;
            PixelProfile = modern ? "ps_5_0" : "ps_4_0";
            string vertexProfile = modern ? "vs_5_0" : "vs_4_0";

            using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            using (var adapter = dxgiDevice.GetAdapter())
            {
                Factory = adapter.GetParent<IDXGIFactory2>();
                AdapterName = adapter.Description.Description?.Trim();
                if (adapter.CheckInterfaceSupport(typeof(IDXGIDevice), out long version))
                {
                    DriverVersion = $"{(version >> 48) & 0xFFFF}.{(version >> 32) & 0xFFFF}.{(version >> 16) & 0xFFFF}.{version & 0xFFFF}";
                }
            }

            byte[] vertexCode = ShaderCompiler.Compile(ReadShader(VertexShaderFile), VertexShaderFile, "main", vertexProfile, out string? vertexError)
                ?? throw new InvalidOperationException("vertex shader: " + vertexError);
            _vertexShader = device.CreateVertexShader(vertexCode);

            _shaderConstants = CreateConstantBuffer(PixelShaderSettings.SizeInBytes);
            _monitorConstants = CreateConstantBuffer(MonitorConstants.SizeInBytes);
            _filterConstants = CreateConstantBuffer(FilterConstants.SizeInBytes);
            _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
            _scissorState = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { ScissorEnable = true });

            // Terminal hands its shaders the rendered text as a texture, transparent where there is none. A wallpaper has
            // no text at all: one transparent pixel.
            _textLayer = device.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm, 1, 1, 1, 1, BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            _textLayerView = device.CreateShaderResourceView(_textLayer);
            var pixel = context.Map(_textLayer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            unsafe
            {
                *(uint*)pixel.DataPointer = 0;
            }
            context.Unmap(_textLayer, 0);

            byte[]? filterCode = ShaderCompiler.Compile(ReadShader(FilterShaderFile), FilterShaderFile, "main", PixelProfile, out string? filterError);
            if (filterCode != null)
            {
                FilterShader = device.CreatePixelShader(filterCode);
            }
            else
            {
                Log.Write("filter shader: " + filterError);
            }
        }
        catch
        {
            DisposeAfterFailedConstruction();
            throw;
        }
    }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    public IDXGIFactory2 Factory { get; }

    /// <summary>HLSL profile pixel shaders are compiled for ("ps_5_0", or "ps_4_0" on old hardware).</summary>
    public string PixelProfile { get; }

    /// <summary>null when the filter pass could not be compiled: frames are then drawn at native size without filters.</summary>
    public ID3D11PixelShader? FilterShader { get; }

    public string? AdapterName { get; }

    /// <summary>User-mode driver version of the adapter ("31.0.101.5186"), null when unknown.</summary>
    public string? DriverVersion { get; }

    /// <summary>No usable GPU: Windows' software rasteriser (WARP) draws, on the CPU.</summary>
    public bool IsSoftware { get; }

    public static GraphicsDevice Create()
    {
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        bool software = false;
        Result result = D3D11.D3D11CreateDevice(
            null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out ID3D11Device? device, out ID3D11DeviceContext? context);
        if (result.Failure)
        {
            software = true;
            D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out device, out context).CheckError();
            Log.Write($"no hardware graphics device (0x{result.Code:X8}): shaders run on the CPU (WARP)");
        }
        return new GraphicsDevice(device!, context!, software);
    }

    /// <summary>Releases what a constructor that failed half-way had already created (a device is a large object to leak).</summary>
    private void DisposeAfterFailedConstruction()
    {
        try
        {
            Dispose();
        }
        catch (SharpGenException e)
        {
            Log.Warn("release a half-built graphics device", e);
        }
    }

    /// <summary>
    /// true when the exception (or the device itself) says the device is gone and has to be rebuilt; any other
    /// exception is an error of the one thing that was being drawn.
    /// </summary>
    public bool IsLost(Exception? error)
    {
        if (error is SharpGenException graphics)
        {
            int code = graphics.HResult;
            if (code == Vortice.DXGI.ResultCode.DeviceRemoved.Code || code == Vortice.DXGI.ResultCode.DeviceReset.Code
                || code == Vortice.DXGI.ResultCode.DeviceHung.Code || code == Vortice.DXGI.ResultCode.DriverInternalError.Code)
            {
                return true;
            }
        }
        return Device.DeviceRemovedReason.Failure;
    }

    public static bool IsLostCode(int code) =>
        code == Vortice.DXGI.ResultCode.DeviceRemoved.Code || code == Vortice.DXGI.ResultCode.DeviceReset.Code
        || code == Vortice.DXGI.ResultCode.DeviceHung.Code;

    public ID3D11PixelShader CreatePixelShader(byte[] bytecode) => Device.CreatePixelShader(bytecode);

    /// <summary>Binds everything a shader draw needs; the caller draws (once, or tile by tile).</summary>
    public void BeginShaderPass(ID3D11PixelShader shader, ID3D11RenderTargetView target, in ShaderPass pass)
    {
        var settings = new PixelShaderSettings
        {
            Time = pass.Time,
            Scale = pass.DpiScale,
            ResolutionX = pass.ViewWidth,
            ResolutionY = pass.ViewHeight,
            BackgroundR = pass.Background[0],
            BackgroundG = pass.Background[1],
            BackgroundB = pass.Background[2],
            BackgroundA = 1f,
        };
        var mapped = Context.Map(_shaderConstants, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        unsafe
        {
            *(PixelShaderSettings*)mapped.DataPointer = settings;
        }
        Context.Unmap(_shaderConstants, 0);
        WriteMonitorConstants(pass.MonitorRects, pass.MonitorIndex);

        Context.OMSetRenderTargets(target);
        Context.RSSetViewport(pass.ViewX, pass.ViewY, pass.ViewWidth, pass.ViewHeight);
        Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Context.VSSetShader(_vertexShader);
        Context.PSSetShader(shader);
        Context.PSSetConstantBuffer(0, _shaderConstants);
        Context.PSSetConstantBuffer(1, _monitorConstants);
        Context.PSSetShaderResource(0, _textLayerView);
        Context.PSSetSampler(0, _sampler);
    }

    /// <summary>Draws the bound pass over the whole viewport.</summary>
    public void Draw() => Context.Draw(3, 0);

    /// <summary>Draws the bound pass inside a rectangle only (thumbnails and the benchmark draw in tiles).</summary>
    public void DrawTile(int x, int y, int width, int height)
    {
        Context.RSSetState(_scissorState);
        Context.RSSetScissorRect(x, y, width, height);
        Context.Draw(3, 0);
        Context.RSSetState(null);
    }

    /// <summary>Second pass: <paramref name="source"/> stretched over the target with the filters applied.</summary>
    public void DrawFilterPass(
        ID3D11RenderTargetView target, ID3D11ShaderResourceView source, int width, int height, in FilterConstants constants)
    {
        var mapped = Context.Map(_filterConstants, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        unsafe
        {
            *(FilterConstants*)mapped.DataPointer = constants;
        }
        Context.Unmap(_filterConstants, 0);

        Context.OMSetRenderTargets(target);
        Context.RSSetViewport(0, 0, width, height);
        Context.PSSetShader(FilterShader);
        Context.PSSetConstantBuffer(0, _filterConstants);
        Context.PSSetShaderResource(0, source);
        Context.Draw(3, 0);
        Context.PSSetShaderResource(0, _textLayerView);   // unbind the source before it is a target again
    }

    public void Dispose()
    {
        Context.ClearState();
        FilterShader?.Dispose();
        _scissorState?.Dispose();
        _sampler?.Dispose();
        _textLayerView?.Dispose();
        _textLayer?.Dispose();
        _filterConstants?.Dispose();
        _monitorConstants?.Dispose();
        _shaderConstants?.Dispose();
        _vertexShader?.Dispose();
        Factory?.Dispose();
        Context.Dispose();
        Device.Dispose();
    }

    private static string ReadShader(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ShaderResourcePrefix + name)
            ?? throw new InvalidOperationException("This build does not contain " + name + ".");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private ID3D11Buffer CreateConstantBuffer(int bytes) =>
        Device.CreateBuffer(new BufferDescription((uint)bytes, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

    private unsafe void WriteMonitorConstants(float[]? rects, int index)
    {
        var constants = default(MonitorConstants);
        if (rects != null && rects.Length >= 4)
        {
            int count = Math.Min(MonitorConstants.MaxMonitors, rects.Length / 4);
            for (int i = 0; i < count * 4; i++)
            {
                constants.Rects[i] = rects[i];
            }
            constants.Count = count;
            constants.Index = Math.Clamp(index, 0, count - 1);
        }
        else
        {
            constants.Rects[2] = 1;
            constants.Rects[3] = 1;
            constants.Count = 1;
        }
        var mapped = Context.Map(_monitorConstants, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        *(MonitorConstants*)mapped.DataPointer = constants;
        Context.Unmap(_monitorConstants, 0);
    }
}
