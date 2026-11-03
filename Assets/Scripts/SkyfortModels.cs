using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Runtime model kit: palette, cached materials and procedural meshes shared by every Skyfort object.
public static class SkyfortModels
{
    public static readonly Color Deck = new(0.05f, 0.075f, 0.13f);
    public static readonly Color DeckLine = new(0.1f, 0.22f, 0.34f);
    public static readonly Color Hull = new(0.12f, 0.24f, 0.36f);
    public static readonly Color Armor = new(0.32f, 0.38f, 0.48f);
    public static readonly Color Bone = new(0.78f, 0.86f, 0.95f);
    public static readonly Color Cyan = new(0.12f, 0.82f, 1f);
    public static readonly Color Gold = new(1f, 0.62f, 0.12f);
    public static readonly Color Red = new(1f, 0.14f, 0.22f);
    public static readonly Color Magenta = new(1f, 0.2f, 0.85f);
    public static readonly Color Lime = new(0.45f, 1f, 0.35f);
    public static readonly Color VoidColor = new(0.015f, 0.02f, 0.06f);
    public static readonly Color ShadowTint = new(0.005f, 0.008f, 0.015f);
    public static readonly Color Sandstone = new(0.62f, 0.5f, 0.32f);
    public static readonly Color DarkSandstone = new(0.42f, 0.33f, 0.21f);
    public static readonly Color CastleStone = new(0.4f, 0.42f, 0.48f);
    public static readonly Color CastleTrim = new(0.28f, 0.3f, 0.36f);
    public static readonly Color RoofTile = new(0.5f, 0.12f, 0.16f);

    private static readonly Dictionary<(Color, bool), Material> Materials = new();
    private static Mesh pyramidMesh;

    // Unit square pyramid: base spans -0.5..0.5 on X/Z at y = 0, apex at y = 1. Flat-shaded faces.
    public static Mesh PyramidMesh()
    {
        if (pyramidMesh != null)
        {
            return pyramidMesh;
        }

        var apex = new Vector3(0f, 1f, 0f);
        var corners = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f)
        };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            var start = vertices.Count;
            vertices.Add(corners[i]);
            vertices.Add(apex);
            vertices.Add(corners[(i + 1) % 4]);
            triangles.AddRange(new[] { start, start + 1, start + 2 });
        }

        var baseStart = vertices.Count;
        vertices.AddRange(corners);
        triangles.AddRange(new[] { baseStart, baseStart + 1, baseStart + 2, baseStart, baseStart + 2, baseStart + 3 });

        pyramidMesh = new Mesh { name = "Pyramid mesh" };
        pyramidMesh.SetVertices(vertices);
        pyramidMesh.SetTriangles(triangles, 0);
        pyramidMesh.RecalculateNormals();
        pyramidMesh.RecalculateBounds();
        return pyramidMesh;
    }

    public static Renderer Pyramid(string name, Transform parent, Vector3 basePosition, Vector3 size, Color color, bool emissive = false)
    {
        var renderer = MeshPart(name, parent, basePosition, Vector3.zero, PyramidMesh(), color, emissive);
        renderer.transform.localScale = size;
        return renderer;
    }

    private static readonly Dictionary<(Vector3, float), Mesh> BevelMeshes = new();
    private static readonly Dictionary<(float, float), Mesh> RingMeshes = new();
    private static Material particleMaterial;

    // Forget cached meshes/materials. The Editor scene builder calls this so each rebuild saves fresh assets.
    public static void ResetCaches()
    {
        Materials.Clear();
        BevelMeshes.Clear();
        RingMeshes.Clear();
        pyramidMesh = null;
        particleMaterial = null;
    }

    public static Transform Group(string name, Transform parent, Vector3 localPosition, Vector3 localEuler = default)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        group.localPosition = localPosition;
        group.localEulerAngles = localEuler;
        return group;
    }

    public static Renderer Part(
        PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale,
        Color color, bool emissive = false, Vector3 localEuler = default)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        // Gameplay uses its own collision math, so primitives drop their colliders (also when built in the Editor).
        if (Application.isPlaying)
        {
            Object.Destroy(part.GetComponent<Collider>());
        }
        else
        {
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.transform.localEulerAngles = localEuler;
        var renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = Mat(color, emissive);
        if (emissive)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        return renderer;
    }

    public static Renderer Beveled(
        string name, Transform parent, Vector3 localPosition, Vector3 size, Color color, float bevel,
        bool emissive = false, Vector3 localEuler = default)
    {
        var key = (size, bevel);
        if (!BevelMeshes.TryGetValue(key, out var mesh) || mesh == null)
        {
            mesh = CreateBeveledBoxMesh(size, bevel);
            mesh.name = $"Bevel {size.x:0.##}x{size.y:0.##}x{size.z:0.##} r{bevel:0.##}";
            BevelMeshes[key] = mesh;
        }

        return MeshPart(name, parent, localPosition, localEuler, mesh, color, emissive);
    }

    public static Renderer Ring(
        string name, Transform parent, Vector3 localPosition, float radius, float thickness, Color color,
        bool emissive = true, Vector3 localEuler = default)
    {
        var key = (radius, thickness);
        if (!RingMeshes.TryGetValue(key, out var mesh) || mesh == null)
        {
            mesh = CreateRingMesh(radius, thickness);
            mesh.name = $"Ring r{radius:0.##} t{thickness:0.##}";
            RingMeshes[key] = mesh;
        }

        return MeshPart(name, parent, localPosition, localEuler, mesh, color, emissive);
    }

    public static Renderer MeshPart(
        string name, Transform parent, Vector3 localPosition, Vector3 localEuler, Mesh mesh, Color color, bool emissive)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localEulerAngles = localEuler;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Mat(color, emissive);
        if (emissive)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        return renderer;
    }

    public static Material Mat(Color color, bool emissive)
    {
        if (Materials.TryGetValue((color, emissive), out var cached) && cached != null)
        {
            return cached;
        }

        // Clone from Resources materials so the Standard shader (and its _EMISSION variant) ships in player builds;
        // Shader.Find alone returns null in a build when no asset references the shader.
        var template = Resources.Load<Material>(emissive ? "SkyfortEmissive" : "SkyfortLit");
        var material = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
        material.name = $"{(emissive ? "Glow" : "Lit")} {ColorUtility.ToHtmlStringRGB(color)}";
        material.color = color;
        material.enableInstancing = true;
        if (emissive)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 1.8f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        Materials[(color, emissive)] = material;
        return material;
    }

    // Sprites/Default is in the always-included shader list, so it is safe for runtime particles in builds.
    public static Material ParticleMaterial()
    {
        if (particleMaterial == null)
        {
            particleMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Skyfort particles" };
        }

        return particleMaterial;
    }

    public static void DisableShadows(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    public static Mesh CreateBeveledBoxMesh(Vector3 size, float bevel)
    {
        var half = size * 0.5f;
        var cut = Mathf.Clamp(bevel, 0.001f, Mathf.Min(size.x, size.y) * 0.45f);
        var outline = new[]
        {
            new Vector2(-half.x + cut, -half.y),
            new Vector2(half.x - cut, -half.y),
            new Vector2(half.x, -half.y + cut),
            new Vector2(half.x, half.y - cut),
            new Vector2(half.x - cut, half.y),
            new Vector2(-half.x + cut, half.y),
            new Vector2(-half.x, half.y - cut),
            new Vector2(-half.x, -half.y + cut)
        };
        var vertices = new List<Vector3>(50);
        var triangles = new List<int>(84);

        AddFlatFace(vertices, triangles, outline, half.z, false);
        AddFlatFace(vertices, triangles, outline, -half.z, true);

        for (var i = 0; i < outline.Length; i++)
        {
            var next = (i + 1) % outline.Length;
            var start = vertices.Count;
            vertices.Add(new Vector3(outline[i].x, outline[i].y, half.z));
            vertices.Add(new Vector3(outline[i].x, outline[i].y, -half.z));
            vertices.Add(new Vector3(outline[next].x, outline[next].y, -half.z));
            vertices.Add(new Vector3(outline[next].x, outline[next].y, half.z));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        var mesh = new Mesh { name = "Beveled armor mesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddFlatFace(List<Vector3> vertices, List<int> triangles, Vector2[] outline, float z, bool reverse)
    {
        var first = vertices.Count;
        vertices.Add(new Vector3(0f, 0f, z));
        foreach (var point in outline)
        {
            vertices.Add(new Vector3(point.x, point.y, z));
        }

        for (var i = 0; i < outline.Length; i++)
        {
            var current = first + 1 + i;
            var next = first + 1 + (i + 1) % outline.Length;
            triangles.Add(first);
            triangles.Add(reverse ? next : current);
            triangles.Add(reverse ? current : next);
        }
    }

    public static Mesh CreateRingMesh(float radius, float thickness)
    {
        const int radialSegments = 16;
        const int tubeSegments = 6;
        var vertices = new Vector3[radialSegments * tubeSegments];
        var triangles = new int[radialSegments * tubeSegments * 6];
        for (var radial = 0; radial < radialSegments; radial++)
        {
            var around = radial * Mathf.PI * 2f / radialSegments;
            for (var tube = 0; tube < tubeSegments; tube++)
            {
                var section = tube * Mathf.PI * 2f / tubeSegments;
                var ringRadius = radius + Mathf.Cos(section) * thickness;
                vertices[radial * tubeSegments + tube] = new Vector3(
                    Mathf.Cos(around) * ringRadius,
                    Mathf.Sin(section) * thickness,
                    Mathf.Sin(around) * ringRadius);

                var nextRadial = (radial + 1) % radialSegments;
                var nextTube = (tube + 1) % tubeSegments;
                var triangle = (radial * tubeSegments + tube) * 6;
                triangles[triangle] = radial * tubeSegments + tube;
                triangles[triangle + 1] = radial * tubeSegments + nextTube;
                triangles[triangle + 2] = nextRadial * tubeSegments + tube;
                triangles[triangle + 3] = radial * tubeSegments + nextTube;
                triangles[triangle + 4] = nextRadial * tubeSegments + nextTube;
                triangles[triangle + 5] = nextRadial * tubeSegments + tube;
            }
        }

        var mesh = new Mesh { name = "Segmented ring mesh", vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Swept-wing fighter silhouette, extruded and pointing along +Z.
    public static Mesh CreateWingMesh(float span, float root, float tip, float sweep, float thickness)
    {
        var outline = new[]
        {
            new Vector2(0f, root * 0.5f),
            new Vector2(span, root * 0.5f - sweep),
            new Vector2(span, root * 0.5f - sweep - tip),
            new Vector2(0f, -root * 0.5f)
        };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var y = thickness * 0.5f;
        foreach (var sign in new[] { 1f, -1f })
        {
            var start = vertices.Count;
            foreach (var p in outline)
            {
                vertices.Add(new Vector3(p.x, y * sign, p.y));
            }

            if (sign > 0f)
            {
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            else
            {
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
        }

        for (var i = 0; i < outline.Length; i++)
        {
            var next = (i + 1) % outline.Length;
            var start = vertices.Count;
            vertices.Add(new Vector3(outline[i].x, y, outline[i].y));
            vertices.Add(new Vector3(outline[next].x, y, outline[next].y));
            vertices.Add(new Vector3(outline[next].x, -y, outline[next].y));
            vertices.Add(new Vector3(outline[i].x, -y, outline[i].y));
            triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        var mesh = new Mesh { name = "Swept wing mesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh CreateCrystalMesh(float radius, float top, float bottom)
    {
        var mesh = new Mesh
        {
            name = "Faceted crystal mesh",
            vertices = new[]
            {
                new Vector3(0f, top, 0f),
                new Vector3(radius, 0.1f, 0f),
                new Vector3(0f, 0.1f, radius),
                new Vector3(-radius, 0.1f, 0f),
                new Vector3(0f, 0.1f, -radius),
                new Vector3(0f, -bottom, 0f)
            },
            triangles = new[]
            {
                0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4,
                5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1
            }
        };
        mesh.RecalculateNormals();
        return mesh;
    }
}
