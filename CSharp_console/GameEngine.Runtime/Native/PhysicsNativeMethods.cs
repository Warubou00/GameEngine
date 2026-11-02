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
        public float      Padding0;
        public Vector3    Size;
        public float      Padding1;
        public Vector3    Position;
        public float      Padding2;
        public Quaternion Rotation;
        public Vector3    Scale;
        public float      Padding3;
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

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_SetTransforms(BodyTransform* items, uint count);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_SetBroadphase(uint mode);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_Step();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Physics_GetEvents(CollisionEvent* events, uint capacity);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Physics_GetStats(out PhysicsStats stats);
}
