using UnityEngine;
using static SkyfortModels;

// The player's strike fighter, built from primitives and faceted meshes whenever the component loads (in the Editor and
// at runtime, so the scene shows the real ship). It replaces whatever
// parts the scene's "Fighter model" object holds. Styling: a white swept-wing jet with a black radome tip and tail
// fins, a red roundel and wing chevrons, yellow patches, a dark glass canopy, wing-root intakes, underwing tanks and
// grey engine nacelles. The model points along +Z.
[ExecuteAlways]
public sealed class SkyfortFighter : MonoBehaviour
{
    private static readonly Color White = new(0.93f, 0.93f, 0.95f);
    private static readonly Color Grey = new(0.62f, 0.65f, 0.72f);
    private static readonly Color DarkGrey = new(0.28f, 0.3f, 0.36f);
    private static readonly Color Black = new(0.05f, 0.05f, 0.07f);
    private static readonly Color RedMark = new(0.85f, 0.1f, 0.1f);
    private static readonly Color Yellow = new(1f, 0.78f, 0.08f);
    private static readonly Color Glass = new(0.16f, 0.3f, 0.45f);

    private void OnEnable()
    {
        Build(transform);
    }

    public static void Build(Transform model)
    {
        for (var i = model.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(model.GetChild(i).gameObject);
        }

        // Fuselage: main body, a wider rear glove between the engines, and a dark dorsal deck.
        Beveled("Fuselage", model, Vector3.zero, new Vector3(0.62f, 0.36f, 2f), White, 0.07f);
        Beveled("Rear glove", model, new Vector3(0f, -0.04f, -0.6f), new Vector3(0.86f, 0.26f, 1f), White, 0.06f);
        Beveled("Dorsal deck", model, new Vector3(0f, 0.2f, -0.65f), new Vector3(0.5f, 0.04f, 0.8f), Black, 0.015f);
        Beveled("Belly strake", model, new Vector3(0f, -0.2f, 0.1f), new Vector3(0.3f, 0.06f, 1.2f), Grey, 0.02f);

        // Nose: white wedge, black band at its base, black radome tip and a pitot probe.
        Wedge("Nose", model, new Vector3(0f, 0f, 0.95f), new Vector3(0.46f, 0.85f, 0.3f), White);
        Beveled("Nose band", model, new Vector3(0f, 0f, 0.93f), new Vector3(0.65f, 0.37f, 0.06f), Black, 0.03f);
        Wedge("Radome tip", model, new Vector3(0f, 0f, 1.62f), new Vector3(0.14f, 0.23f, 0.09f), Black);
        Part(PrimitiveType.Cube, "Pitot probe", model, new Vector3(0f, 0f, 2f), new Vector3(0.02f, 0.02f, 0.34f), Grey);

        // Tandem canopy: dark glass bubble, a sloped windscreen, and a black frame.
        Beveled("Canopy", model, new Vector3(0f, 0.24f, 0.2f), new Vector3(0.34f, 0.2f, 0.95f), Glass, 0.07f);
        Wedge("Windscreen", model, new Vector3(0f, 0.24f, 0.65f), new Vector3(0.34f, 0.4f, 0.2f), Glass);
        foreach (var z in new[] { -0.2f, 0.2f, 0.62f })
        {
            Part(PrimitiveType.Cube, "Canopy bow", model, new Vector3(0f, 0.25f, z), new Vector3(0.36f, 0.22f, 0.025f), Black);
        }

        Part(PrimitiveType.Cube, "Canopy rail", model, new Vector3(0f, 0.355f, 0.2f), new Vector3(0.03f, 0.02f, 0.95f), Black);

        // Markings: a red-and-white roundel with a dark center, tally marks, and a yellow patch on each side.
        var wingMesh = CreateWingMesh(1.35f, 1.3f, 0.35f, 0.75f, 0.08f);
        var finMesh = CreateWingMesh(0.72f, 0.7f, 0.28f, 0.4f, 0.05f);
        var stabMesh = CreateWingMesh(0.5f, 0.5f, 0.2f, 0.3f, 0.04f);

        MeshPart("Right wing", model, new Vector3(0.28f, -0.05f, -0.3f), Vector3.zero, wingMesh, White, false);
        var leftWing = MeshPart("Left wing", model, new Vector3(-0.28f, -0.05f, -0.3f), Vector3.zero, wingMesh, White, false);
        leftWing.transform.localScale = new Vector3(-1f, 1f, 1f);

        foreach (var side in new[] { -1f, 1f })
        {
            // Side markings on the fuselage.
            Disc("Roundel red", model, new Vector3(side * 0.315f, 0.02f, 0.5f), 0.2f, RedMark);
            Disc("Roundel white", model, new Vector3(side * 0.318f, 0.02f, 0.5f), 0.13f, White);
            Disc("Roundel core", model, new Vector3(side * 0.321f, 0.02f, 0.5f), 0.06f, RedMark);
            Part(PrimitiveType.Cube, "Tally", model, new Vector3(side * 0.316f, 0.08f, 0.2f), new Vector3(0.01f, 0.07f, 0.025f), Black);
            Part(PrimitiveType.Cube, "Tally", model, new Vector3(side * 0.316f, 0.08f, 0.26f), new Vector3(0.01f, 0.07f, 0.025f), Black);
            Part(PrimitiveType.Cube, "Tally", model, new Vector3(side * 0.316f, 0.08f, 0.32f), new Vector3(0.01f, 0.07f, 0.025f), Black);
            Part(PrimitiveType.Cube, "Yellow patch", model, new Vector3(side * 0.43f, 0.0f, -0.95f), new Vector3(0.02f, 0.14f, 0.3f), Yellow);

            // Wing: black leading edge, red chevron and a yellow tip patch.
            Strip("Wing leading edge", model, new Vector3(side * 0.3f, 0f, 0.35f), new Vector3(side * 1.63f, 0f, -0.4f), 0.02f, 0.05f, Black);
            Strip("Wing chevron", model, new Vector3(side * 0.6f, 0.005f, -0.13f), new Vector3(side * 1.3f, 0.005f, -0.52f), 0.015f, 0.08f, RedMark);
            Strip("Wing chevron", model, new Vector3(side * 0.6f, 0.005f, -0.13f), new Vector3(side * 0.95f, 0.005f, 0.14f), 0.015f, 0.06f, RedMark);
            Part(PrimitiveType.Cube, "Wing tip patch", model, new Vector3(side * 1.5f, 0.005f, -0.7f), new Vector3(0.22f, 0.015f, 0.14f), Yellow);
            Part(PrimitiveType.Cube, "Wing pivot", model, new Vector3(side * 0.34f, 0.01f, -0.1f), new Vector3(0.22f, 0.05f, 0.3f), Grey);

            // Underwing pylon and drop tank, plus a wing cannon at the tip.
            Part(PrimitiveType.Cube, "Pylon", model, new Vector3(side * 0.78f, -0.11f, -0.1f), new Vector3(0.05f, 0.1f, 0.3f), DarkGrey);
            Beveled("Drop tank", model, new Vector3(side * 0.78f, -0.2f, -0.1f), new Vector3(0.17f, 0.15f, 0.85f), Grey, 0.05f);
            Wedge("Drop tank nose", model, new Vector3(side * 0.78f, -0.2f, 0.32f), new Vector3(0.14f, 0.2f, 0.12f), Grey);
            Part(PrimitiveType.Cube, "Wing cannon", model, new Vector3(side * 1.6f, -0.04f, -0.45f), new Vector3(0.07f, 0.07f, 0.5f), DarkGrey);

            // Intakes at the wing roots: white scoops with black mouths.
            Beveled("Intake", model, new Vector3(side * 0.4f, -0.1f, 0.15f), new Vector3(0.2f, 0.22f, 0.85f), White, 0.05f);
            Part(PrimitiveType.Cube, "Intake mouth", model, new Vector3(side * 0.4f, -0.1f, 0.585f), new Vector3(0.16f, 0.17f, 0.03f), Black);

            // Engines: grey nacelles, black nozzles with an afterburner glow, ringed.
            Beveled("Engine nacelle", model, new Vector3(side * 0.38f, -0.03f, -0.85f), new Vector3(0.3f, 0.3f, 0.8f), Grey, 0.08f);
            Beveled("Nozzle", model, new Vector3(side * 0.38f, -0.03f, -1.3f), new Vector3(0.25f, 0.25f, 0.12f), Black, 0.06f);
            Beveled("Afterburner", model, new Vector3(side * 0.38f, -0.03f, -1.37f), new Vector3(0.18f, 0.18f, 0.03f), Cyan, 0.05f, true);
            Ring("Nozzle ring", model, new Vector3(side * 0.38f, -0.03f, -1.25f), 0.14f, 0.025f, Cyan, true, new Vector3(90f, 0f, 0f));

            // Tailplanes (stabilators).
            MeshPart("Stabilator", model, new Vector3(side * 0.62f, -0.04f, -1.0f), Vector3.zero, stabMesh, DarkGrey, false)
                .transform.localScale = new Vector3(side, 1f, 1f);

            // Twin tail fins: black, leaning outward, with a yellow tip and a white skull-and-bones mark.
            MeshPart("Tail fin", model, new Vector3(side * 0.42f, 0.1f, -0.55f), new Vector3(0f, 0f, side > 0 ? 74f : 106f), finMesh, Black, false);
            Part(PrimitiveType.Cube, "Fin tip", model, new Vector3(side * 0.63f, 0.79f, -0.74f), new Vector3(0.05f, 0.05f, 0.3f), Yellow, false, new Vector3(0f, 0f, side * -16f));
            Disc("Skull", model, new Vector3(side * 0.585f, 0.52f, -0.64f), 0.2f, White, 0.03f);
            Part(PrimitiveType.Cube, "Skull eye", model, new Vector3(side * 0.6f, 0.55f, -0.69f), new Vector3(0.012f, 0.04f, 0.04f), Black);
            Part(PrimitiveType.Cube, "Skull eye", model, new Vector3(side * 0.6f, 0.55f, -0.59f), new Vector3(0.012f, 0.04f, 0.04f), Black);
            Part(PrimitiveType.Cube, "Crossbone", model, new Vector3(side * 0.6f, 0.42f, -0.64f), new Vector3(0.012f, 0.03f, 0.26f), White, false, new Vector3(35f, 0f, 0f));
            Part(PrimitiveType.Cube, "Crossbone", model, new Vector3(side * 0.6f, 0.42f, -0.64f), new Vector3(0.012f, 0.03f, 0.26f), White, false, new Vector3(-35f, 0f, 0f));

            // Wingtip navigation lights.
            Part(PrimitiveType.Cube, "Nav light", model, new Vector3(side * 1.64f, 0.0f, -0.55f), Vector3.one * 0.07f, side < 0 ? Red : Lime, true);
        }

        // The ground shadow is a separate object straight below the ship, so the model casts none of its own.
        DisableShadows(model);

        // In the Editor the parts are a preview rebuilt on load, so keep them out of the saved scene.
        if (!Application.isPlaying)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t != model)
                {
                    t.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
                }
            }
        }
    }

    // A four-sided wedge (the pyramid mesh turned to face +Z). Size is base width, length, base height.
    private static void Wedge(string name, Transform parent, Vector3 basePosition, Vector3 size, Color color)
    {
        Pyramid(name, parent, basePosition, size, color).transform.localEulerAngles = new Vector3(90f, 0f, 0f);
    }

    // A flat round marking standing against the fuselage side (its face points along X).
    private static void Disc(string name, Transform parent, Vector3 position, float diameter, Color color, float thickness = 0.008f)
    {
        Part(PrimitiveType.Cylinder, name, parent, position, new Vector3(diameter, thickness, diameter), color, false, new Vector3(0f, 0f, 90f));
    }

    // A thin flat bar laid between two points (used for stripes along the wings).
    private static void Strip(string name, Transform parent, Vector3 a, Vector3 b, float height, float width, Color color)
    {
        var direction = b - a;
        var bar = Part(PrimitiveType.Cube, name, parent, (a + b) * 0.5f, new Vector3(direction.magnitude, height, width), color);
        bar.transform.localRotation = Quaternion.FromToRotation(Vector3.right * Mathf.Sign(direction.x), direction);
    }
}
