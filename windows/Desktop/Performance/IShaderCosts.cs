namespace Wallpepper.Desktop.Performance;

/// <summary>What the performance fitter needs to know about shaders, and the one thing it may change about them.</summary>
internal interface IShaderCosts
{
    /// <summary>
    /// GPU milliseconds per 1080p frame, measured in a burst at full GPU clock, with the shader's light settings or its
    /// normal ones. 0 when it was never measured.
    /// </summary>
    double KnownCost(string path, bool light);

    /// <summary>The shader has "@light" settings and the user has not asked for full quality.</summary>
    bool CanUseLight(string path);

    /// <summary>The shader runs with its light settings right now.</summary>
    bool IsLight(string path);

    /// <summary>Switch a shader to its light settings (it is recompiled).</summary>
    void UseLight(string path);
}
