# Skyfort Strike — Unity 6 Feature Showcase

An isometric, forward-scrolling altitude shooter. Your strike fighter flies itself through a fortress corridor; you steer, climb and dive to get past walls, gates and force fields, and you shoot ground targets at their own height. Destroy fuel tanks to keep flying, survive the open-space drone wave, and take on the Warden, a towering guardian mech, at the end of every level. Each level is faster than the last.

## Open and play

1. Open this folder in Unity Editor **6000.3 or later**.
2. Open `Assets/Scenes/SkyfortStrike.unity` and press **Play**.

| Action | Keys |
|---|---|
| Steer left / right | **A / D** or **← / →** |
| Climb / dive | **W / S** or **↑ / ↓** |
| Fire | **Space** (also **J**, **Z**, **Left Ctrl**) |
| Pause | **P** or **Esc** |
| Restart | **R**, or **Space / Enter** on the Game Over screen |

### How to fly

- **Read your height.** The ALT gauge (left), the ship's ground shadow (it shrinks as you climb) and the glowing rungs on the far rampart all show your altitude.
- **Walls.** Fly over low walls. Thread tall walls through their gold-framed gate.
- **Force fields** (pink beams) rise from the floor or hang from the top. Go over or under them.
- **Your laser fires at your current altitude.** Dive low to hit turrets (150), fuel tanks (300, refuels you) and radar dishes (1000, two hits).
- **Fuel** burns constantly, except in the Warden arena. When it runs out, you crash.
- **The Warden.** Hit its glowing red eye before it withdraws, and dodge its plasma spreads. Destroying it awards 5000 × level.

Everything (models, level streaming, audio and HUD) is generated at runtime by the scene's `SkyfortStrike` component. All models are original low-poly meshes built in code. The only assets are two small Standard-shader materials in `Assets/Resources`; runtime materials are cloned from them so the shader and its emission variant are included in player builds.

## Unity 6 features used

| Feature | Where |
|---|---|
| **UI Toolkit runtime data binding**: `[CreateProperty]`, `DataBinding`, `INotifyBindablePropertyChanged`, `IDataSourceViewHashProvider` | `SkyfortHud.cs`: the HUD is bound to `SkyfortHudState` and updates only when values change |
| **Binding converters** (`ConverterGroup` / `sourceToUiConverters`) | bool → `style.display` (boss bar) and bool → `style.color` (low-fuel warning) |
| **`[UxmlElement]` / `[UxmlAttribute]` custom controls** (source-generated) | `SkyfortGauge`, drawn with the `Painter2D` vector API |
| **`Awaitable.BackgroundThreadAsync` / `MainThreadAsync`** | `SkyfortAudio.LoadAsync` synthesizes every sound on a worker thread, then returns to the main thread to create the `AudioClip`s |
| **`Awaitable<T>`, `WaitForSecondsAsync`, `destroyCancellationToken`** | Countdown, respawn, boss death sequence and banners; all cancel safely when the object is destroyed |
| **`Object.InstantiateAsync`** | Batched asynchronous prewarm of the laser and enemy-bolt pools |
| **`Rigidbody.linearVelocity`** | Fighter movement |
| `FindAnyObjectByType`-era APIs, `RenderSettings`, GPU-instanced runtime materials | World setup |

## Sound

All sound effects and the engine drone are synthesized in code by `SkyfortAudio` (lasers, enemy shots, explosions, fuel and radar chimes, low-fuel alarm, boss siren and hits, fanfares), so no audio files are needed.

## WebGL build and GitHub Pages

Build locally and publish from the `docs` folder on `main`:

1. Install Unity **6000.3.x** with the **WebGL Build Support** module.
2. Open the project and choose **Build → WebGL to docs (GitHub Pages)**. This disables compression (GitHub Pages can't serve Unity's compressed builds) and writes the build to `docs/`.
3. Commit and push the `docs` folder to `main`.
4. In **Settings → Pages**, set **Source** to **Deploy from a branch**, then choose `main` and `/docs`.

The site is served at [https://jcallinan.github.io/Unity6VideoGames/](https://jcallinan.github.io/Unity6VideoGames/).

The `Build and deploy WebGL` workflow is an optional, manual-only alternative that builds in CI with GameCI. It needs the `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` repository secrets (see the [GameCI activation guide](https://game.ci/docs/github/activation/)) and a Pages source of **GitHub Actions**. Never commit a license file, password, or secret.
