using System;
using SpriteMaster.Types;

namespace SpriteMaster.Resample.Scalers;

internal interface IScaler {
    Config CreateConfig(
        Vector2B wrapped,
        bool hasAlpha,
        bool gammaCorrected
    );

    IScalerInfo Info { get; }

    uint MinScale { get; }
    uint MaxScale { get; }
    uint ClampScale(uint scale);

    Span<Color16> Apply(
        Config configuration,
        uint scaleMultiplier,
        ReadOnlySpan<Color16> sourceData,
        Vector2I sourceSize,
        Span<Color16> targetData,
        Vector2I targetSize
    );

    internal static IScalerInfo DefaultInfo => DefaultScaler.ScalerInfo.Instance;

    internal static IScaler Default => new DefaultScaler.Scaler.ScalerInterface();

    internal static IScalerInfo GetScalerInfo(Scaler scaler) => xBRZ.ScalerInfo.Instance;

    internal static IScalerInfo? CurrentInfo => GetScalerInfo(SMConfig.Resample.Scaler);

    internal static IScaler Current => xBRZ.Scaler.ScalerInterface.Instance;
}
