using System.Numerics;

namespace GameEngine;

// ==========================================================================
// ライト (Transform と一緒に付ける)
//   DirectionalLight : 太陽。Transform の向き (Forward = +Z) に光が進む。位置は関係ない
//   PointLight       : 電球。Transform の位置から Range まで届く
//   Intensity は「真上から当たった白い面がその明るさになる」単位 (Lambert / PBR 共通)
//   色は線形 (1, 1, 1 が白)
// ==========================================================================
public struct DirectionalLight : IComponent
{
    public Vector3 Color;
    public float   Intensity;
    public bool    CastShadows;   // 影を落とす (最初の 1 つだけ。Graphics.Shadows で設定)

    public DirectionalLight()
    {
        Color       = Vector3.One;
        Intensity   = 1.0f;
        CastShadows = true;
    }
}

public struct PointLight : IComponent
{
    public Vector3 Color;
    public float   Intensity;
    public float   Range;

    public PointLight()
    {
        Color     = Vector3.One;
        Intensity = 1.0f;
        Range     = 10.0f;
    }
}
