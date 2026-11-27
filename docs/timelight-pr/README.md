# Timelight world-map embedding proposal

Expose Timelight's existing live terrain render to other MeitouClient mods as a UI texture. NUE can draw the texture behind the native world map's towns, squads and controls, while Meitou continues to handle pan, zoom and map clicks.

The public contract and request state are implemented in [Embedding.cs](Embedding.cs). This file is intended for the upstream Timelight PR. It is authored independently of Timelight's renderer and is outside NUE's build.

## Upstream wiring

1. Call `Embedding.SetRendererReady(true)` once the render hooks are successfully installed. Call it with `false` when they are removed or the renderer is unavailable. The embedding API must be shipped in a Timelight build that loads on MeitouClient 0.24.0.
2. After processing pending settings in the frame hook, check `Embedding.EmbeddedMode`. In this mode skip Timelight's corner overlay and its overlay marks. Render only when `Embedding.PendingFrame` has a request. A user can retain their usual Timelight settings when no consumer owns the embedding mode. Allow the terrain lighting hook to capture main-view lighting while an embedded request is pending, even if the minimized HUD map was off.
3. Send the requested world rectangle and pixel dimensions through the existing top-down renderer. Take the center from the midpoint, set both horizontal spans from the requested bounds, use north-up orientation, and look straight down. Ship the full rectangular render without the minimap's frame, dots or labels. Reuse Timelight's own exposure and color conversion with a texture target, so Meitou's UI samples display-ready pixels. If the requested region is beyond streamed terrain, invalidate the frame and let the host map show through.
4. After the render pass has finished and the texture is safe for UI sampling, call `Embedding.Publish(request, texture, sampled, flipV)`. Publish the exact request used for the render. The producer owns the texture and sampler. Keep them valid through in-flight UI frames and call `InvalidateFrame()` before retiring or replacing a published target. If the renderer writes to a texture the UI can still sample, use a separate output target.

## Coordinate contract

`WorldBounds` is `(minX, minZ, maxX, maxZ)` in Meitou world units. The image's top-left represents `(minX, minZ)` and its bottom-right represents `(maxX, maxZ)`. `flipV` accounts for backend texture orientation. Calls run on Meitou's render thread. Request and availability checks return immediately. Consumers borrow complete frames, which may come from a prior request.

NUE claims the embedding mode only if the API is present and ready. When the Map tab closes it clears the request. While waiting for a frame, it keeps the native map visible. During panning it draws any completed frame only over the world area that frame actually represents. Releasing embedding mode restores Timelight's normal overlay behavior.

## PR acceptance checks

- Without an embedding consumer, Timelight's minimap and big-map hotkeys behave as before.
- With NUE active, opening the Map tab shows the live image only in the interactive map. Markers stay in place when panning and zooming, and right-click map orders still work.
- Closing the map stops embedded rendering. Wide zoom, distant terrain, and renderer reset use the native map until a usable frame is ready.
- Test texture orientation and frame lifetime on each supported renderer backend.
