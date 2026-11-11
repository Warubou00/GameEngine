
---
name: rhi-development
description: Use when developing or modifying the Rendering Hardware Interface (RHI), including graphics device management, GPU resources, buffers, textures, descriptors, command submission, synchronization, and DirectX 12 implementation.
---

# RHI Development

## 1. Responsibility

RHI provides a low-level graphics hardware abstraction layer.

The current graphics API backend is DirectX 12.

Responsibilities:
- Graphics device and adapter management
- GPU resource creation and management
- Buffer and texture management
- Descriptor management
- Command list and command queue management
- Resource state transitions
- GPU synchronization
- Graphics API abstraction

RHI hides DirectX 12 implementation details from higher-level systems.

## 2. Dependency Rules

- RHI must not depend on Engine.
- RHI must not reference Engine-specific classes, structures, or interfaces.
- Keep DirectX 12 dependencies inside the RHI implementation.
- Separate API-independent interfaces from DirectX 12-specific implementations where appropriate.
- Higher-level systems must access graphics functionality through RHI interfaces.
- Avoid circular dependencies between modules.

## 3. Interface Design

- Keep public RHI interfaces independent of DirectX 12-specific types.
- Design interfaces around the requirements of the current engine.
- Avoid designing for unsupported or unused graphics APIs.
- Prefer simple interfaces with clear responsibilities.
- Avoid unnecessary virtual interfaces and abstraction layers.
- Preserve existing interfaces unless a change is required.

## 4. Resource Management

- Use RAII for resource lifetime management.
- Use ComPtr for COM object ownership.
- Do not assume every RHI resource must be managed by ComPtr.
- Make GPU resource ownership and lifetime explicit.
- Ensure resources remain alive while GPU commands are using them.
- Follow existing descriptor allocation and resource caching policies.
- Avoid unnecessary resource creation, destruction, and duplication.

## 5. DirectX 12 Implementation

- Follow the existing DirectX 12 architecture and coding conventions.
- Keep DirectX 12-specific implementation details out of API-independent interfaces.
- Use the existing resource and descriptor management systems.
- Respect GPU resource lifetime and synchronization requirements.
- Avoid introducing redundant wrappers or abstraction layers.
- Do not introduce DirectX 11 compatibility code.

## 6. Descriptor Management

- Follow the existing DescriptorHeapManager allocation policy.
- Respect descriptor heap types, offsets, and allocation limits.
- Keep descriptor allocation and release responsibilities explicit.
- Avoid unnecessary descriptor allocation and heap switching.
- Ensure descriptor handles remain valid while referenced by GPU commands.
- Follow existing descriptor caching policies.

## 7. Resource States and Synchronization

- Track resource states correctly.
- Issue ResourceBarriers when required for correctness.
- Avoid unnecessary resource state transitions.
- Use fences for GPU synchronization.
- Do not release resources before GPU execution completes.
- Do not sacrifice correctness for performance optimization.

## 8. Development Workflow

1. Inspect the relevant RHI interfaces and DirectX 12 implementations.
2. Identify the affected GPU resources and command operations.
3. Check resource ownership, descriptor allocation, and synchronization.
4. Review the impact on existing RHI interfaces.
5. Propose a design when the change affects architecture or public interfaces.
6. Implement the required changes while preserving existing behavior.
7. Build and verify the affected projects.

## 9. Restrictions

- Do not introduce dependencies on Engine.
- Do not introduce DirectX 11 compatibility code.
- Do not expose DirectX 12-specific types through common RHI interfaces.
- Do not bypass existing resource management systems.
- Do not perform unrelated refactoring.
- Do not introduce unnecessary abstraction layers.
- Do not modify higher-level system responsibilities.
