using System.Numerics;
using System.Runtime.InteropServices;

namespace GameEngine.Native;

// ==========================================================================
// Physics.dll (衝突判定) の P/Invoke。C++ 側は Physics/Source/physicsDll.cpp
//   構造体の並びは Physics/Source/common.h と同じにすること
//   (NativeMethods.cs / AudioNativeMethods.cs と同じ書き方 : Cdecl、ハンドルは uint)
// ==========================================================================
internal static unsafe class PhysicsNativeMethods
{
    private const string Dll = "Physics.dll";

    public const uint InvalidHandle = 0xFFFFFFFFu;

    // 96byte
    [StructLayout(LayoutKind.Sequential)]
    public struct BodyDesc
    {
        public uint       Shape;         // ColliderShape
        public uint       Layer;
        public uint       CollideMask;
        public uint       IsStatic;
        public Vector3    Center;
        public uint       Mesh;          // CollisionMesh のハンドル (Shape = Mesh の時)
        public Vector3    Size;
        public float      Friction;      // 以前は詰め物 (P1)
        public Vector3    Position;
        public float      Restitution;   // 以前は詰め物 (P1)
        public Quaternion Rotation;
        public Vector3    Scale;
        public uint       Flags;         // 1 : トリガー (以前は詰め物。P1)
    }

    // ---------- 剛体 (P1。C++ の common.h と同じ並び) ----------

    // 32byte
    [StructLayout(LayoutKind.Sequential)]
    public struct BodyDynamics
    {
        public uint  Motion;          // 0 : 剛体なし / 1 : Dynamic / 2 : Kinematic
        public float Mass;
        public float LinearDamping;
        public float AngularDamping;
        public float GravityScale;
        public uint  Locks;           // RigidbodyConstraints
        public float InertiaScale;
        public uint  Flags;           // 1 : 眠らない (P2。以前は詰め物)

        public readonly bool Same(in BodyDynamics o) =>
            Motion == o.Motion && Mass == o.Mass && LinearDamping == o.LinearDamping && AngularDamping == o.AngularDamping &&
            GravityScale == o.GravityScale && Locks == o.Locks && InertiaScale == o.InertiaScale && Flags == o.Flags;
    }

    public const uint CommandForce           = 1u << 0;
    public const uint CommandImpulse         = 1u << 1;
    public const uint CommandSetVelocity     = 1u << 2;
    public const uint CommandSetAngular      = 1u << 3;
    public const uint CommandKinematicTarget = 1u << 4;
    public const uint CommandWake            = 1u << 5;   // P2
    public const uint CommandSleep           = 1u << 6;

    // 144byte
    [StructLayout(LayoutKind.Sequential)]
    public struct BodyCommand
    {
        public uint       Handle;
        public uint       Flags;
        public uint       Padding0;
        public uint       Padding1;
        public Vector3    Force;           public float Padding2;
        public Vector3    Torque;          public float Padding3;
        public Vector3    Impulse;         public float Padding4;
        public Vector3    AngularImpulse;  public float Padding5;
        public Vector3    Velocity;        public float Padding6;
        public Vector3    AngularVelocity; public float Padding7;
        public Vector3    TargetPosition;  public float Padding8;
        public Quaternion TargetRotation;
    }

    // 96byte
    [StructLayout(LayoutKind.Sequential)]
    public struct BodyState
    {
        public uint       Handle;
        public uint       Flags;           // 1 : 眠っている (P2)
        public uint       Padding0;
        public uint       Padding1;
        public Vector3    Position;        public float Padding2;
        public Quaternion Rotation;
        public Vector3    Velocity;        public float Padding3;
        public Vector3    AngularVelocity; public float Padding4;
        public Vector3    CenterOfMass;    public float Padding5;
    }

    // 80byte (以前は 64byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct SolverSettings
    {
        public Vector3 Gravity;
        public uint    SubSteps;
        public float   ContactHertz;
        public float   ContactDampingRatio;
        public float   ContactPushMaxVelocity;
        public float   RestitutionThreshold;
        public float   SpeculativeDistance;
        public float   JointHertz;
        public float   JointDampingRatio;
        public float   MaxLinearSpeed;
        public uint    Iterations;
        public uint    RelaxIterations;
        public uint    EnableSleep;            // P2
        public float   TimeToSleep;
        public float   SleepLinearVelocity;
        public float   SleepAngularVelocity;
        public uint    Padding0;
        public uint    Padding1;
    }

    // 64byte
    [StructLayout(LayoutKind.Sequential)]
    public struct JointDesc
    {
        public uint    Type;
        public uint    BodyA;
        public uint    BodyB;          // InvalidHandle ならワールド
        public uint    Flags;          // 1 : 繋いだ体どうしも押し戻す
        public Vector3 AnchorA;
        public float   Length;
        public Vector3 AnchorB;
        public float   MinLength;
        public float   MaxLength;
        public float   Hertz;
        public float   DampingRatio;
        public float   BreakForce;
    }

    // 16byte
    [StructLayout(LayoutKind.Sequential)]
    public struct JointInfo
    {
        public uint  Broken;
        public float Force;
        public float Length;
        public float Padding;
    }

    // 48byte
    [StructLayout(LayoutKind.Sequential)]
    public struct BodyTransform
    {
        public uint       Handle;
        public Vector3    Position;
        public Quaternion Rotation;
        public Vector3    Scale;
        public float      Padding;
    }

    // 16byte
    [StructLayout(LayoutKind.Sequential)]
    public struct CollisionEvent
    {
        public uint BodyA;
        public uint BodyB;
        public uint Type;   // 0 : Enter / 1 : Exit
        public uint Padding;
    }

    // 32byte
    [StructLayout(LayoutKind.Sequential)]
    public struct Ray
    {
        public Vector3 Origin;
        public float   MaxDistance;
        public Vector3 Direction;
        public uint    LayerMask;
    }

    // 48byte
    [StructLayout(LayoutKind.Sequential)]
    public struct RaycastHit
    {
        public uint    Body;
        public uint    Hit;
        public float   Distance;
        public float   Padding0;
        public Vector3 Point;
        public float   Padding1;
        public Vector3 Normal;
        public float   Padding2;
    }

    // 48byte。法線は BodyA → BodyB
    [StructLayout(LayoutKind.Sequential)]
    public struct ContactPoint
    {
        public uint    BodyA;
        public uint    BodyB;
        public float   Depth;
        public float   Padding0;
        public Vector3 Position;
        public float   Padding1;
        public Vector3 Normal;
        public float   Padding2;
    }

    // 80byte (以前は 64byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct PhysicsStats
    {
        public uint  Bodies;
        public uint  MovedBodies;
        public uint  Candidates;
        public uint  Overlaps;
        public float BroadphaseMs;
        public float NarrowphaseMs;
        public float StepMs;
        public uint  Broadphase;
        public float SyncMs;
        public float EventsMs;
        public uint  Workers;
        public float TreeMs;
        public float SolverMs;
        public uint  RigidBodies;
        public uint  ContactConstraints;
        public uint  Joints;
        public uint  AwakeBodies;     // P2
        public uint  Islands;
        public float IslandMs;
        public uint  Padding;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Physics_Initialize();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_Shutdown();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_CreateBody(ref BodyDesc desc);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_DestroyBody(uint body);

    // 体 / ジョイントを全部消して、作った直後と同じにする (設定とメッシュは残す。Engine.BeginPlay)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_ResetBodies();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_SetTransforms(BodyTransform* items, uint count);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_SetBroadphase(uint mode);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_Step();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_SetLayerCollision(uint a, uint b, [MarshalAs(UnmanagedType.I1)] bool collide);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_GetLayerCollisionMask(uint layer);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_SetOption(uint option, uint value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_GetEvents(CollisionEvent* events, uint capacity);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_GetStats(out PhysicsStats stats);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_CreateMesh(float* positions, uint vertexCount, uint* indices, uint indexCount);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_DestroyMesh(uint mesh);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Physics_Raycast(ref Ray ray, out RaycastHit hit);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_RaycastBatch(Ray* rays, uint count, RaycastHit* hits);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_OverlapSphere(Vector3* center, float radius, uint layerMask, uint* bodies, uint capacity);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_OverlapBox(Vector3* center, Vector3* halfExtents, Quaternion* rotation, uint layerMask, uint* bodies, uint capacity);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_GetContacts(ContactPoint* contacts, uint capacity);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_SetBodyDynamics(uint body, ref BodyDynamics dynamics);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_ApplyCommands(BodyCommand* commands, uint count);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_Simulate(float dt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_GetBodyStates(BodyState* states, uint capacity);

    // 起きている剛体 + 最後の刻みで眠った剛体だけ (P2)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_GetActiveBodyStates(BodyState* states, uint capacity);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_SetSolverSettings(ref SolverSettings settings);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_GetSolverSettings(out SolverSettings settings);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_CreateJoint(ref JointDesc desc);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_DestroyJoint(uint joint);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Physics_GetJointInfo(uint joint, out JointInfo info);
}
