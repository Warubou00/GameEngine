# Project Instructions

## 1\. Project Overview

* Language: C++
* IDE: Visual Studio 2022
* Graphics API: DirectX 12
* Project Type: Custom Game Engine

### Main Architecture

* Core: Common foundation and utilities.
* RHI: Graphics API abstraction and GPU resource management.
* Renderer: Rendering pipeline and rendering features.
* Tools: Development and debugging tools.

## 2\. Architecture Rules

* Maintain the existing layered architecture.
* Lower-level modules must not depend on higher-level modules.
* Keep each class responsible for a clear purpose.
* Avoid unnecessary inheritance and abstraction.
* Prefer composition when it improves class responsibilities.
* Do not introduce circular dependencies.
* Preserve existing public interfaces unless explicitly approved.

## 3\. C++ Coding Standards

* Follow the existing naming conventions.
* Separate declarations and implementations into .h and .cpp.
* Use const whenever appropriate.
* Prefer references when null is not a valid state.
* Use RAII for resource management.
* Avoid unnecessary dynamic memory allocation.
* Avoid unnecessary copying of large objects.
* Use existing project utilities before introducing new implementations.

## 4\. Memory and Resource Management

* Use Microsoft::WRL::ComPtr for COM objects.
* Make resource ownership explicit.
* Ensure GPU resources remain alive while being used by the GPU.
* Respect GPU synchronization using fences.
* Avoid premature resource release.
* Follow the existing Descriptor allocation and management policies.
* Avoid unnecessary resource barriers.

## 5\. Development Workflow

1. Investigate only the relevant source files.
2. Identify affected classes and dependencies.
3. Understand existing implementation and design.
4. Propose an implementation plan.
5. Wait for approval before modifying source code.
6. Implement only the approved changes.
7. Build the affected projects.
8. Review the changes and report the results as a text file in the .claud/log folder.
9. When adding functions to or updating the game engine, you upgrade the game engine and package it.
10. Export the runtime environment to Editor/Launcher/Project inside the package folder, respectively.

## 6\. Code Modification Restrictions

* Do not modify unrelated files.
* Do not perform unnecessary refactoring.
* Do not change existing behavior without approval.
* Do not introduce new libraries without approval.
* Do not duplicate existing functionality.
* Do not overwrite user modifications.

## 7\. Context Management

* Do not scan the entire project unless requested.
* Do not read build outputs or generated files.
* Read only necessary source files.
* Prioritize class declarations and relevant implementations.
* Avoid repeating completed investigations.
* Keep reports concise and focused.
* If necessary, rewrite Docs/index.html, which is the reference file for developers. Also, update the Script Reference inside the Editor.

## 8\. Build and Verification

* Use the existing Visual Studio solution.
* Use the existing build configurations.
* Build affected projects after modifications.
* Report build errors and warnings.
* Do not claim successful verification without executing the build.

