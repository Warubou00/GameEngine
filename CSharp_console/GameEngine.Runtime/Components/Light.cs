using System.Numerics;

namespace GameEngine;

// ==========================================================================
// ライト (Transform と一緒に付ける)
//   DirectionalLight : 太陽。Transform の向き (Forward = +Z) に光が進む。位置は関係ない
//   PointLight       : 電球。Transform の位置から Range まで届く
//   SpotLight        : 懐中電灯 (v2.2)。Transform の位置から向き (Forward = +Z) へ、円錐の形に Range まで届く
//   Intensity は「真上から当たった白い面がその明るさになる」単位 (Lambert / PBR 共通)
//   色は線形 (1, 1, 1 が白)
//   v2.2 : 点光源 / スポットライトに UE と同じ項目を足した
//     Source Radius (光源の大きさ。鏡面反射のハイライトが広がる) / Inverse Squared Falloff と Falloff Exponent (距離の減衰) /
//     Cast Shadows (影。近いライトから Project Settings の枚数まで) / Shadow Bias
//   ライトが多くても、画面の区画 (クラスター) ごとに届くライトだけを計算する (Project Settings > Rendering > Local Lights)
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
    [Range(0.0f, 100.0f), Tooltip("Brightness (a white surface facing the light gets this value at about 1 m)")]
    public float   Intensity;
    [Range(0.01f, 200.0f), Tooltip("Attenuation radius: the light reaches exactly 0 here")]
    public float   Range;

    // ---------- v2.2 (UE と同じ項目) ----------
    [Range(0.0f, 10.0f), Tooltip("Size of the light source. Larger values make highlights wider and softer")]
    public float   SourceRadius;
    [Tooltip("Physically based falloff (1 / distance squared). Off: (1 - (d / Range)^2) ^ Falloff Exponent")]
    public bool    UseInverseSquaredFalloff;
    [Range(0.1f, 16.0f), Tooltip("Falloff curve when Use Inverse Squared Falloff is off")]
    public float   FalloffExponent;
    [Tooltip("Cast shadows (6 shadow map faces; the nearest lights get shadows up to Project Settings > Local Lights > Max Shadow Slices)")]
    public bool    CastShadows;   // 既定は切る (v2.2 より前のシーンの見た目と重さを変えない)
    [Range(0.0f, 8.0f), Tooltip("Shadow normal offset in shadow map texels. Raise it if surfaces get stripes")]
    public float   ShadowBias;

    public PointLight()
    {
        Color     = Vector3.One;
        Intensity = 1.0f;
        Range     = 10.0f;
        SourceRadius = 0.0f;
        UseInverseSquaredFalloff = true;
        FalloffExponent = 8.0f;
        CastShadows = false;
        ShadowBias  = 1.5f;
    }
}

// v2.2 : スポットライト (UE の Spot Light と同じ項目)
//   円錐 : 中心から InnerConeAngle までは同じ明るさ、OuterConeAngle で 0 (間はなめらかに弱くなる)。角度は中心からの半分の角度 (度)
public struct SpotLight : IComponent
{
    public Vector3 Color;
    [Range(0.0f, 100.0f), Tooltip("Brightness (a white surface facing the light gets this value at about 1 m)")]
    public float   Intensity;
    [Range(0.01f, 200.0f), Tooltip("Attenuation radius: the light reaches exactly 0 here")]
    public float   Range;
    [Range(0.0f, 80.0f), Tooltip("Half angle (degrees) of the bright inner cone")]
    public float   InnerConeAngle;
    [Range(1.0f, 80.0f), Tooltip("Half angle (degrees) where the light fades to 0")]
    public float   OuterConeAngle;
    [Range(0.0f, 10.0f), Tooltip("Size of the light source. Larger values make highlights wider and softer")]
    public float   SourceRadius;
    [Tooltip("Physically based falloff (1 / distance squared). Off: (1 - (d / Range)^2) ^ Falloff Exponent")]
    public bool    UseInverseSquaredFalloff;
    [Range(0.1f, 16.0f), Tooltip("Falloff curve when Use Inverse Squared Falloff is off")]
    public float   FalloffExponent;
    [Tooltip("Cast shadows (1 shadow map; the nearest lights get shadows up to Project Settings > Local Lights > Max Shadow Slices)")]
    public bool    CastShadows;
    [Range(0.0f, 8.0f), Tooltip("Shadow normal offset in shadow map texels. Raise it if surfaces get stripes")]
    public float   ShadowBias;

    public SpotLight()
    {
        Color          = Vector3.One;
        Intensity      = 1.0f;
        Range          = 10.0f;
        InnerConeAngle = 0.0f;    // UE の既定と同じ
        OuterConeAngle = 44.0f;
        SourceRadius   = 0.0f;
        UseInverseSquaredFalloff = true;
        FalloffExponent = 8.0f;
        CastShadows    = true;
        ShadowBias     = 1.5f;
    }
}
