
---
name: engine-development
description: Use when developing or modifying Engine-level systems, including scene management, entities, components, models, meshes, materials, resource management, animation, cameras, and engine managers.
---

# Engine Development

## 1. Responsibility

Engine provides high-level engine functionality built on top of RHI.

Responsibilities:
- Scene management
- Entity and Component management
- Model and Mesh management
- Material management
- Engine-level resource management
- Animation systems
- Camera systems
- Engine managers

Engine coordinates high-level systems and uses RHI for graphics hardware operations.

## 2. Dependency Rules

- Engine may depend on RHI.
- Engine must not directly depend on graphics API implementations.
- Use RHI interfaces for graphics-related operations.
- Keep Engine systems independent of DirectX-specific types and behavior.
- Do not introduce circular dependencies.
- Avoid hidden dependencies between unrelated Engine systems.

## 3. Class Design

- Keep each class focused on a clear responsibility.
- Prefer composition over unnecessary inheritance.
- Use inheritance when there is a meaningful "is-a" relationship or shared polymorphic behavior.
- Avoid oversized manager classes.
- Use interfaces only when they provide meaningful abstraction.
- Prefer explicit dependencies over hidden global dependencies.
- Keep ownership and object lifetimes clear.
- Follow existing naming conventions and coding style.

## 4. Resource Management

- Follow the existing Engine resource management architecture.
- Separate resource loading, resource ownership, and resource usage where appropriate.
- Reuse shared resources when appropriate.
- Avoid unnecessary resource duplication.
- Use RHI resources for GPU-side data.
- Do not directly create or manage DirectX-specific objects.
- Keep CPU-side resource data and GPU-side resource data clearly distinguished.
- Respect RHI resource lifetime and synchronization requirements.

## 5. RHI Integration

- Use RHI interfaces for graphics device operations.
- Use RHI abstractions for buffers, textures, descriptors, and command submission.
- Do not bypass RHI to access DirectX implementations.
- Do not duplicate functionality already provided by RHI.
- Keep API-specific decisions inside RHI.
- Ensure Engine functionality remains compatible with the supported RHI backends.

## 6. Performance

- Avoid unnecessary dynamic allocations.
- Avoid unnecessary copying of large objects and resource data.
- Reuse resources when appropriate.
- Avoid unnecessary virtual calls in performance-critical paths.
- Consider CPU and GPU synchronization costs.
- Prefer measurable optimizations over speculative complexity.
- Preserve correctness and maintainability when optimizing.

## 7. Development Workflow

1. Inspect the relevant Engine classes and related RHI interfaces.
2. Identify dependencies between Engine systems.
3. Check existing class responsibilities and ownership.
4. Review whether the required functionality already exists.
5. Propose a class design when the change affects architecture or public interfaces.
6. Implement the required changes while preserving existing behavior.
7. Build and verify the affected projects.

## 8. Restrictions

- Do not introduce graphics API dependencies into Engine.
- Do not directly access DirectX-specific implementations.
- Do not duplicate RHI functionality.
- Do not perform unrelated refactoring.
- Do not introduce unnecessary inheritance or abstraction.
- Do not change existing public interfaces without a clear reason.
