# Rift Runner — Unity 6 Feature Showcase

A small, playable 3D arcade game built to demonstrate Unity 6's `Awaitable` async API. Guide the space-suited runner around the arena, collect all eight energy crystals, and steer clear of the moving red sentinels before time runs out.

## Unity 6 feature

The round countdown and replay delay use Unity 6's built-in `Awaitable` API instead of coroutines. `Awaitable.WaitForSecondsAsync` handles timed transitions, and `Awaitable.NextFrameAsync` advances the final countdown step. The round ID prevents an old asynchronous countdown from starting a game after the player has restarted. Player movement uses Unity 6's `Rigidbody.linearVelocity` API for responsive, frame-rate-independent motion.

## Open and play

1. Open this folder in Unity Editor **6000.0 or later**.
2. Open `Assets/Scenes/RiftRunner.unity` and press **Play**.
3. Use **WASD** or the **arrow keys** to move.
4. Collect all eight gold crystals to win. Avoid the moving red sentinels; the run ends when the 60-second timer expires.
5. Press **R** at any time to restart the countdown. Runs also restart automatically after a win or timeout.

The arena, camera, lighting, character, crystals, sentinels, and perimeter scenery are created by the scene's `RiftRunner` component when Play mode starts. The space suit and sentinels use original low-poly mesh details built at runtime, while the crystals are custom faceted meshes. Perimeter beacon towers, glowing circuits, and scattered deck lights add depth around the arena. These are self-contained showcase models, not imported artist-authored assets; no external assets or packages are required.

## Sound

All sound effects and the ambient hum are synthesized in code by `RiftAudio` at startup (crystal chimes that rise in pitch, sentinel proximity warning, countdown beeps, win and time-up jingles), so no audio files are needed.

## WebGL build and GitHub Pages

Build locally and publish from the `docs` folder on `main`:

1. Install Unity **6000.0.23f1** (or newer 6000.x) with the **WebGL Build Support** module.
2. Open the project and choose **Build → WebGL to docs (GitHub Pages)**. This disables compression (GitHub Pages can't serve Unity's compressed builds) and writes the build to `docs/`.
3. Commit and push the `docs` folder to `main`.
4. In **Settings → Pages**, set **Source** to **Deploy from a branch**, then choose `main` and `/docs`.

The site is served at [https://jcallinan.github.io/Unity6VideoGames/](https://jcallinan.github.io/Unity6VideoGames/).

The `Build and deploy WebGL` workflow is an optional, manual-only alternative that builds in CI with GameCI. It needs the `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` repository secrets (see the [GameCI activation guide](https://game.ci/docs/github/activation/)) and a Pages source of **GitHub Actions**. Never commit a license file, password, or secret.
