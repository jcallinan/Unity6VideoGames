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

The arena, camera, lighting, character, crystals, and sentinels are created by the scene's `RiftRunner` component when Play mode starts. The current character and props are custom Unity geometry placeholders; replace them with authored models when those assets are available. No external assets or packages are required.

## WebGL build and GitHub Pages

The `Build and deploy WebGL` workflow builds the game with Unity 6 and publishes it to [GitHub Pages](https://jcallinan.github.io/Unity6VideoGames/). It runs when changes reach `main` and can also be started manually from the Actions tab. The Pages site is available after its first successful deployment.

Before the first build, add these repository Actions secrets: `UNITY_LICENSE` (Unity Personal license file contents), `UNITY_EMAIL`, and `UNITY_PASSWORD`. See the [GameCI license activation guide](https://game.ci/docs/github/activation/) to prepare the license. The workflow configures Pages for GitHub Actions on deployment; never commit the license file, password, or secret.
