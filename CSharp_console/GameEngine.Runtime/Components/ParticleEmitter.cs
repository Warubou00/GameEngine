using System.Numerics;

namespace GameEngine;

// 出す場所の形 (C++ の RHI::ParticleSpawnShape と同じ番号)
public enum ParticleShape : uint
{
    Point  = 0,   // エミッターの位置から
    Sphere = 1,   // 半径 ShapeRadius の球の中
    Shell  = 2,   // 半径 ShapeRadius の球の表面
    Box    = 3,   // 半辺 ShapeRadius の立方体の中
    Disc   = 4,   // 半径 ShapeRadius の円 (出す向きに垂直な面)
}

// 見た目の形 (C++ の RHI::ParticleSprite と同じ番号)
public enum ParticleSprite : uint
{
    SoftCircle = 0,   // ふちがぼやけた丸 (煙 / 光)
    Spark      = 1,   // 芯の明るい点 (火花)
    Ring       = 2,   // 輪
}

public enum ParticleBlend : uint
{
    Alpha    = 0,   // 半透明 (煙など。並べ替え (Graphics.Particles.Sorting) で前後が正しくなる)
    Additive = 1,   // 加算 (光る物。並び順に関係なく正しく重なる)
}

// ==========================================================================
// GPU パーティクルのエミッター (Transform と一緒に付ける)
//   パーティクルの発生 / 動き / 生死 / 描画はすべて GPU で行う (1 個 1 個のデータは CPU に無い)。
//   CPU (ParticleSystem) は毎フレーム、このエミッターの位置と「このフレームに出す数」を送るだけ
//   出す向き = Transform の回転 × Direction (既定は真上)。動かすとその間に散らして出す
//   外す / エンティティを削除すると新しく出さなくなり、出た物は寿命まで残る
//   (資料 MH:W p.87 : エミッターの領域は、配下のパーティクルが消えるまで使い回さない)
// ==========================================================================
public struct ParticleEmitter : IComponent
{
    public bool    Emitting;        // false で新しく出さない (出た物は寿命まで残る)
    public float   Rate;            // 1 秒に出す数
    public int     Burst;           // 次のフレームに一度に出す数 (出したら 0 に戻る。花火など)

    public ParticleShape Shape;
    public float   ShapeRadius;     // 形の大きさ (Point 以外)
    public Vector3 Direction;       // 出す向き (Transform の回転で回す。既定は +Y)
    public float   ConeAngle;       // 向きのばらつき (円錐の半角。度。180 で全方向)
    public float   SpeedMin;        // 出す速さ (m/s)
    public float   SpeedMax;
    public float   LifeMin;         // 寿命 (秒)
    public float   LifeMax;

    public Vector3 Gravity;         // 加速度 (m/s^2。既定は地球の重力)
    public float   Drag;            // 空気抵抗 (1 秒あたりに速度が e^-Drag 倍)
    public float   Turbulence;      // 乱れ (ふらふら揺れる強さ。m/s^2)

    public Vector4 ColorStart;      // 生まれた時の色 (線形。1 を超えると光る) とアルファ
    public Vector4 ColorEnd;        // 死ぬ時の色
    public float   SizeStart;       // 大きさ (四角の半分の幅。m)
    public float   SizeEnd;
    public ParticleSprite Sprite;
    public ParticleBlend  Blend;
    public float   AngularSpeed;    // 回転の速さの最大 (度 / 秒。向きはランダム)
    public float   StretchScale;    // 0 より大きいと速度の向きに伸ばす (速さ × これ 秒分。火花 / 雨)

    public bool    Collide;         // シーン (前に描いた不透明な物の深度) で跳ね返る
    public float   Restitution;     // 跳ね返りの強さ (0〜1)

    // ParticleSystem が使う (C++ のエミッターの番号 + 1。0 = まだ無い)
    internal int     SlotPlusOne;
    internal float   SpawnRemainder;   // Rate × 時間 の端数 (次のフレームに持ち越す)
    internal Vector3 LastPosition;
    internal bool    HasLastPosition;

    // 瞬間移動させた時に呼ぶ (前の位置との間に散らして出さないように。次のフレームは今の位置だけから出す)
    public void ResetTrail() => HasLastPosition = false;

    public ParticleEmitter()
    {
        Emitting     = true;
        Rate         = 100.0f;
        Shape        = ParticleShape.Point;
        Direction    = Vector3.UnitY;
        ConeAngle    = 20.0f;
        SpeedMin     = 2.0f;
        SpeedMax     = 4.0f;
        LifeMin      = 1.0f;
        LifeMax      = 2.0f;
        Gravity      = new Vector3(0.0f, -9.8f, 0.0f);
        ColorStart   = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        ColorEnd     = new Vector4(1.0f, 1.0f, 1.0f, 0.0f);
        SizeStart    = 0.05f;
        SizeEnd      = 0.05f;
        Sprite       = ParticleSprite.SoftCircle;
        Blend        = ParticleBlend.Additive;
        Restitution  = 0.4f;
    }
}
