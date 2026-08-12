# KSA-XR Agent Guide

## Purpose and project status

KSA-XR is an experimental OpenXR mod for Kitten Space Agency (KSA), loaded through StarMap. KSA is a pre-alpha game built on the custom BRUTAL framework, a thin C# layer over low-level native APIs such as Vulkan, GLFW, and ImGui. There is no conventional high-level engine abstraction to rely on.

Treat this project as reverse-engineering work against a moving, undocumented game API. Rendering changes are especially fragile. Prefer small, well-targeted Harmony patches and explicit Vulkan operations over broad attempts to replace the game's renderer.

The current proof of concept renders one desktop frame and two XR eye frames by running three complete KSA frames. It copies each eye render from `Program.MainViewport.OffscreenTarget.ColorImage` into an OpenXR-owned swapchain image. This is knowingly inefficient but currently produces correct stereo views.

## Collaboration rules

- Always re-read the current source before diagnosing or suggesting a change. This project changes frequently, and prior conversational context may describe code that has since been reverted or rewritten.
- Distinguish requests for analysis from requests for implementation. If the user says "show me", "suggest", "outline", or "do not patch", do not edit code.
- Work on one rendering/synchronization change at a time. Do not bundle speculative optimizations with a behavior fix.
- Prefer the smallest patch surface that preserves correct stereo cameras and rendering, even when it costs more GPU or CPU time.
- Do not propose an eye `Viewport` or direct `RenderViewport` call without first proving all required framebuffer, render pass, command-buffer, and image-lifetime assumptions. Previous direct calls failed inside `BeginRenderPass` with invalid Vulkan state.
- Do not suppress a renderer acquire or submit operation independently. Acquire, command recording, queue submission, semaphore/fence transitions, and presentation form one lifecycle. Partial skips have previously caused `NotReady`, freezes, and `VK_ERROR_DEVICE_LOST`.
- Preserve the normal desktop view. The desired window output is the latest normal game frame, including UI, while XR eye helper frames remain invisible on the desktop.
- Keep comments that explain why unusual patches exist. The code intentionally works around blank window frames and shader/culling behavior; these decisions are not self-evident.
- Never discard user changes in a dirty worktree. Inspect diffs before modifying files that are already changed.

## Repository map

- `Sources/KSA.XR/ModInit.cs`: StarMap entry points, Harmony installation, UI pulse, and the call to `OpenXR.OnFrame` after the game's frame.
- `Sources/KSA.XR/OpenXR.cs`: OpenXR instance/session lifecycle, runtime events, view location, eye swapchains, Vulkan copy resources, XR frame submission, and desktop composite cache.
- `Sources/KSA.XR/XrViewports.cs`: the render-state machine, camera/projection patches, fullscreen-composite suppression, ImGui suppression, and desktop presentation replay.
- `Sources/KSA.XR/VulkanInitPatches.cs`: injects OpenXR-required Vulkan extensions and captures the game's Vulkan instance, physical device, logical device, graphics queue, queue family, and queue index.
- `Sources/KSA.XR/DebugUI.cs`: starts/stops the XR session, controls render scale and symmetric FOV behavior, and displays diagnostic state.
- `KSA_XR.csproj`: authoritative `KSADir`, target framework, platform, game assembly references, and NuGet dependencies.
- `.tmp_ilscan/`: ignored scratch tooling used to inspect current game assemblies. It may be stale and should be regenerated or adjusted after a KSA update.
- `runtime_extensions.md`: one captured runtime extension list; useful as evidence, not as a universal runtime contract.

## Build and runtime environment

- Windows x64 only at present.
- Target framework: `net10.0-windows` with unsafe code enabled.
- The default game installation is taken from `<KSADir>` in `KSA_XR.csproj`, currently `C:\Program Files\Kitten Space Agency\`.
- Game references are loaded directly from `$(KSADir)`: `KSA.dll`, `Planet*.dll`, `Brutal*.dll`, and `Tomlet.dll`.
- NuGet dependencies include Evergine OpenXR bindings, Lib.Harmony, the native OpenXR loader, and StarMap.API.
- Normal compile check:

  ```powershell
  dotnet build .\KSA_XR.csproj -c Debug -p:Platform=x64
  ```

- A build may require network access for NuGet restore. Do not report code as compile-verified if restore or the build did not complete.
- `make_mod_junction.bat` creates an elevated junction from the game's `Content\KSA_XR` folder to `bin\x64\Debug\net10.0-windows`. It removes any existing path at that exact destination, so do not run it casually or non-interactively without the user's request.
- The `Debug through StarMap` launch profile starts `StarMap.exe`. Full verification requires the game, an active OpenXR runtime, and usually a headset; a successful compile is not a rendering test.

## Reverse-engineering workflow

KSA updates can change private methods, fields, overloads, generic constraints, image ownership, and render ordering. Never rely only on remembered signatures.

1. Read `KSADir` from the current project file.
2. Inspect the binaries currently installed there, especially `KSA.dll`, `Planet.Render.Core.dll`, and the relevant BRUTAL assemblies.
3. Use or update `.tmp_ilscan/` to inspect metadata, IL call sites, field layouts, overloads, and generic constraints. A decompiler may also be used when method control flow is needed.
4. Confirm the exact current target before writing a Harmony attribute or transpiler matcher.
5. Make transpilers fail visibly or log when their expected call pattern is not found. A silently unmatched transpiler is difficult to distinguish from a rendering bug.
6. Re-run inspection after every reported game update before changing patches.

At the time this file was written, the important window frame chain was:

```text
KSA.Program.OnFrame
  -> Core.Renderer.TryAcquireNextFrame
  -> KSA.Program.RenderGame(AcquiredFrame, double)
  -> Core.Renderer.TrySubmitFrame
     -> queue submission
     -> Core.Renderer.PresentFrame
        -> Queue.PresentKHR
```

This is a baseline to verify, not a stable public API. `RenderCore.AcquiredFrame` has exposed frame resources and a command buffer. Window swapchain images live in renderer frame resources. The current presentation patch indexes them with `Renderer.SwapchainImageIndex`, not the frame-in-flight index; do not conflate these indexes.

## Known game rendering shape

The game renders the main viewport offscreen and later performs a fullscreen screen-space composite into the acquired window swapchain image. This distinction is what makes the current XR copy and desktop replay workaround possible.

Current assembly inspection has shown `Program.RenderGame` coordinating simulation-dependent render preparation, shadow passes, planet mesh/clutter updates, atmosphere LUT transitions, the main viewport, post-processing, fullscreen composition, and UI. The main scene includes stars, the sun, distant/static celestial bodies, planetary terrain, vessels and other meshes, rings/transparencies, atmosphere/cloud work, and ocean work. Do not treat this list as one monolithic render pass or assume all atmosphere rendering is compute-based: the atmosphere system mixes compute-generated/intermediate data with graphics rendering, and the exact split must be checked in the current assemblies.

`Program.RenderViewport` and related pass classes are useful entry points when remapping the pipeline, but their render-pass/framebuffer assumptions are coupled to game-owned targets. Relevant names observed across versions include `AtmosphereRenderer`, `PlanetRenderer`, `PlanetTransparenciesRenderer`, `CloudRenderer`, `OceanRenderer`, `SunRenderer`, `SunbloomRenderer`, `OrbitLinePass`, `GizmoPass`, `SingleToMultisamplePass`, and `ScreenspaceRenderer`. Re-discover their current call order rather than hard-coding this historical list.

## Current XR frame timeline

`ModInit.OnFrame` is a `[StarMapAfterOnFrame]` callback. Therefore, the game has already rendered/submitted its current frame when `OpenXR.OnFrame` processes it and advances the state machine. This ordering is essential.

The current cycle is:

```text
NormalGame game frame
  - Main camera uses the desktop projection.
  - Fullscreen composite and ImGui render normally.
  - PresentFrame patch captures the fully composited window image.
  - OpenXR.OnFrame ends the previous XR frame if complete.
  - It calls xrWaitFrame, xrBeginFrame, and locates the new views.
  - State advances to XR / Left.

XR / Left game frame
  - Camera patches apply the left-eye pose and symmetric projection.
  - The game renders the main viewport with that camera.
  - Desktop fullscreen composite and ImGui are skipped.
  - PresentFrame patch restores the cached NormalGame image to the window swapchain.
  - OpenXR.OnFrame acquires/waits for the left XR image, blits the main offscreen image,
    releases it, and records the left projection layer view.
  - State advances to XR / Right.

XR / Right game frame
  - Same process for the right eye.
  - State advances back to NormalGame.

Next NormalGame callback
  - OpenXR submits both recorded projection views with xrEndFrame.
  - A new OpenXR frame is begun and view poses are located.
```

Do not move `XrViewports.RenderFinished()` out of the end of `OpenXR.OnFrame` without reconstructing this timeline. Earlier placements produced one correct eye and one eye using the desktop camera.

## Camera and projection behavior

- Only `Program.MainViewport.BaseCamera` is modified for XR.
- `Camera.OnFrame` applies the current eye pose to the camera and then recomputes projection.
- `Camera.UpdateProjection` is patched after the game method to replace the main camera's projection and inverse projection during XR passes.
- The projection is reverse-Z and uses OpenXR signed angular FOV values: left/down are negative, right/up are positive.
- The current implementation computes a shared symmetric FOV from both eyes in tangent space, then converts it back to signed radians. This exists because multiple KSA shaders and/or culling paths break with asymmetric frusta.
- The same symmetric FOV is normally submitted in the OpenXR projection layer. `DisableSymetricFOV` is a debug option that submits the runtime FOV instead.
- Changes to camera update order can produce correct-looking matrices at one point in the frame but stale matrices during actual render recording. Verify behavior in the headset, not just logged values.

## Window presentation workaround

The two XR helper frames still traverse the game's window swapchain. Suppressing only the fullscreen composite makes those window frames blank, so `RendererPresentFramePatch` deliberately preserves the desktop image:

- During `NormalGame`, immediately before presentation, copy the fully composited swapchain image (including UI) into a persistent device-local image.
- During both XR passes, immediately before presentation, blit that cached image into the currently acquired window swapchain image.
- `EnsureDesktopCompositeBuffer` recreates the cached image when window extent or color format changes.
- The helper tracks the cached image layout and restores the window image to `PresentSrcKHR`.

Keep the capture after the screen/UI composite and the replay before `PresentKHR`. Moving capture to the main viewport's offscreen image would omit UI. Skipping presentation instead has historically caused stale acquisition state, black frames, `NotReady`, freezes, or device loss.

## Vulkan rules

- `OpenXR` borrows the Vulkan instance/device/queue from KSA; it does not own those game objects.
- The XR session creates and owns a dedicated resettable command pool, one primary copy command buffer, and one fence on the game's graphics queue family.
- Current copies are intentionally synchronous: reset fence, record one-time commands, submit to the shared graphics queue, and wait for the fence. Optimize only after correctness is retained.
- OpenXR swapchain images may be touched only after successful `xrAcquireSwapchainImage` and `xrWaitSwapchainImage`, and all GPU use must complete before `xrReleaseSwapchainImage`.
- Every image operation must use the actual current layout, correct access masks and pipeline stages, correct queue family ownership, and image usage flags that permit transfer.
- The OpenXR eye swapchains are created with color-attachment and transfer-destination usage. They are currently blit destinations, not BRUTAL render targets.
- Window swapchain images are separate from `Program.MainViewport.OffscreenTarget.ColorImage`. The former are presented to `SurfaceKHR`; the latter contain the eye render copied to OpenXR.
- Never invoke a game render pass using an arbitrary command buffer. Render passes, framebuffers, command pools, and frame resources must belong to compatible device/queue/render-pass state.
- Destroy or recreate the desktop composite image only when its submitted work is complete. Session teardown currently waits for device idle before destroying XR-owned Vulkan resources.
- Native failures such as access violations in `BeginRenderPass` and `VK_ERROR_DEVICE_LOST` usually indicate invalid Vulkan state or synchronization; they cannot be safely handled as ordinary C# exceptions.

## Harmony patching guidance

- Prefer typed targets when the target is public and stable. Use string/reflection targets for private game methods only after verifying their current signature.
- Harmony prefix returning `false` skips the whole original method. Use this only when all side effects of that method are understood.
- Use transpilers for granular call-site replacement where the surrounding method must continue running. Preserve labels and exception blocks when replacing instructions.
- Generic replacement methods must reproduce the original generic constraints. `ScreenspaceRenderer.Render<T>` currently requires `where T : unmanaged`.
- Be cautious patching BRUTAL generic Vulkan wrapper methods directly; Harmony/MonoMod has previously failed while importing their generic parameters. Patching a non-generic caller or replacing a specific call site is usually safer.
- Reflection `MethodInfo.Invoke` must receive the exact current argument count and compatible wrapper types. It is not a substitute for understanding command-buffer validity.
- The fullscreen-composite transpiler intentionally replaces `ScreenspaceRenderer.Render<T>` calls in `Program.RenderGame` with a state-aware wrapper. ImGui draw-data rendering is separately skipped during XR passes.
- Keep patch classes narrowly named and document both the mechanism and the behavioral reason.

## Verification checklist

For ordinary changes:

- Build the project and report whether restore and compilation actually succeeded.
- Inspect the diff for accidental changes to user code or generated files.
- If Harmony targets changed, verify each target against the installed assemblies.

For rendering or synchronization changes, ask the user to validate at least:

- XR session starts without Harmony patch exceptions.
- Both eyes have the correct, independently tracked pose and projection.
- Headset images update continuously.
- Desktop retains the latest normal game view and UI without black frames, XR-eye flashes, trails, or duplicated moving objects.
- Window resize/minimize/restore does not use an old-sized desktop composite image.
- Ending and restarting the XR session restores the normal camera and render state.
- No `NotReady` loop, semaphore/fence stall, access violation, or `VK_ERROR_DEVICE_LOST` appears in logs.

Do not describe a Vulkan rendering change as verified based only on compilation. Runtime behavior in both the HMD and desktop window is the acceptance test.
