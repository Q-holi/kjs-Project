using System.Collections.Generic;
using UnityEngine;

namespace Kjs
{
    /// <summary>Renders server state. Movement authority stays on the server.</summary>
    public sealed class WorldView : MonoBehaviour
    {
        private sealed class Avatar
        {
            public GameObject root;
            public Material material;
            public Vector3 target;
            public Quaternion rotation;
            public string name;
        }

        private readonly Dictionary<string, Avatar> avatars = new Dictionary<string, Avatar>();
        private readonly List<Material> sceneMaterials = new List<Material>();
        private Camera worldCamera;
        private Material baseMaterial;
        private string localId;
        private GUIStyle nameStyle;

        public int PlayerCount => avatars.Count;

        public void Initialize()
        {
            baseMaterial = Resources.Load<Material>("KjsSurface");
            if (baseMaterial == null)
                throw new MissingReferenceException("Resources/KjsSurface.mat is required.");

            var ground = CreateBox("Shared Field", new Vector3(0, -0.5f, 0), new Vector3(50, 1, 50),
                                   new Color(0.10f, 0.17f, 0.22f));
            ground.transform.SetParent(transform, true);
            for (int x = -20; x <= 20; x += 10)
            for (int z = -20; z <= 20; z += 10)
            {
                if (((x + z) / 10) % 2 == 0) continue;
                CreateBox("Field tile", new Vector3(x, 0.005f, z), new Vector3(9.9f, 0.01f, 9.9f),
                          new Color(0.13f, 0.22f, 0.26f));
            }
            var edge = new Color(0.13f, 0.57f, 0.61f);
            CreateBox("North boundary", new Vector3(0, 0.15f, 24.7f), new Vector3(49.8f, 0.3f, 0.3f), edge);
            CreateBox("South boundary", new Vector3(0, 0.15f, -24.7f), new Vector3(49.8f, 0.3f, 0.3f), edge);
            CreateBox("East boundary", new Vector3(24.7f, 0.15f, 0), new Vector3(0.3f, 0.3f, 49.8f), edge);
            CreateBox("West boundary", new Vector3(-24.7f, 0.15f, 0), new Vector3(0.3f, 0.3f, 49.8f), edge);

            var cameraObject = new GameObject("World Camera", typeof(Camera));
            cameraObject.transform.SetParent(transform);
            cameraObject.tag = "MainCamera";
            worldCamera = cameraObject.GetComponent<Camera>();
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
            worldCamera.orthographic = true;
            worldCamera.orthographicSize = 21;
            worldCamera.nearClipPlane = 0.1f;
            worldCamera.farClipPlane = 150;
            worldCamera.transform.position = new Vector3(0, 30, -24);
            worldCamera.transform.LookAt(Vector3.zero);

            var lightObject = new GameObject("Sun", typeof(Light));
            lightObject.transform.SetParent(transform);
            var sun = lightObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(55, -35, 0);
            RenderSettings.ambientLight = new Color(0.6f, 0.68f, 0.75f);
            RenderSettings.fog = false;
        }

        private GameObject CreateBox(string label, Vector3 position, Vector3 scale, Color color)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = label;
            box.transform.SetParent(transform, false);
            box.transform.position = position;
            box.transform.localScale = scale;
            var material = new Material(baseMaterial) { color = color };
            box.GetComponent<Renderer>().sharedMaterial = material;
            sceneMaterials.Add(material);
            return box;
        }

        public void SetLocalId(string id) => localId = id;

        public void Apply(PlayerSnapshot[] players)
        {
            if (players == null) return;
            var live = new HashSet<string>();
            foreach (var player in players)
            {
                if (string.IsNullOrEmpty(player.id) || !IsFinite(player.x) || !IsFinite(player.z)
                    || !IsFinite(player.yaw)) continue;
                live.Add(player.id);
                if (!avatars.TryGetValue(player.id, out var avatar))
                {
                    var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    body.name = "Player " + player.id;
                    body.transform.SetParent(transform, false);
                    body.transform.position = new Vector3(player.x, 1, player.z);
                    // No local physics authority; server currently models an empty plane.
                    Destroy(body.GetComponent<Collider>());
                    float hue = HashHue(player.id);
                    var material = new Material(baseMaterial)
                    {
                        color = player.id == localId ? new Color(0.23f, 0.95f, 0.84f)
                                                     : Color.HSVToRGB(hue, 0.65f, 0.95f)
                    };
                    body.GetComponent<Renderer>().sharedMaterial = material;
                    var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    nose.name = "Facing direction";
                    nose.transform.SetParent(body.transform, false);
                    nose.transform.localPosition = new Vector3(0, 0.45f, 0.43f);
                    nose.transform.localScale = Vector3.one * 0.25f;
                    nose.GetComponent<Renderer>().sharedMaterial = material;
                    Destroy(nose.GetComponent<Collider>());
                    avatar = new Avatar { root = body, material = material };
                    avatars.Add(player.id, avatar);
                }
                avatar.name = player.name;
                avatar.target = new Vector3(player.x, 1, player.z);
                avatar.rotation = Quaternion.Euler(0, player.yaw, 0);
            }
            var removed = new List<string>();
            foreach (var pair in avatars)
                if (!live.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var id in removed) Remove(id);
        }

        private static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        private static float HashHue(string value)
        {
            uint hash = 2166136261;
            foreach (char c in value) hash = unchecked((hash ^ c) * 16777619);
            return (hash % 1000) / 1000f;
        }

        public void Remove(string id)
        {
            if (string.IsNullOrEmpty(id) || !avatars.TryGetValue(id, out var avatar)) return;
            Destroy(avatar.root);
            Destroy(avatar.material);
            avatars.Remove(id);
        }

        public void Clear()
        {
            foreach (var avatar in avatars.Values)
            {
                Destroy(avatar.root);
                Destroy(avatar.material);
            }
            avatars.Clear();
            localId = null;
        }

        private void LateUpdate()
        {
            float blend = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            foreach (var avatar in avatars.Values)
            {
                avatar.root.transform.position = Vector3.Lerp(avatar.root.transform.position, avatar.target, blend);
                avatar.root.transform.rotation = Quaternion.Slerp(avatar.root.transform.rotation, avatar.rotation, blend);
            }
            // Fixed camera keeps world-axis WASD aligned with the view.
        }

        public void DrawNames(Font font)
        {
            if (worldCamera == null) return;
            if (nameStyle == null)
                nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
                    fontSize = 14, richText = false };
            nameStyle.font = font;
            foreach (var pair in avatars)
            {
                var avatar = pair.Value;
                Vector3 point = worldCamera.WorldToScreenPoint(avatar.root.transform.position + Vector3.up * 1.6f);
                if (point.z <= 0) continue;
                nameStyle.normal.textColor = pair.Key == localId ? new Color(0.4f, 1, 0.9f) : Color.white;
                GUI.Label(new Rect(point.x - 90, Screen.height - point.y - 12, 180, 24),
                          avatar.name + (pair.Key == localId ? " (You)" : ""), nameStyle);
            }
        }

        private void OnDestroy()
        {
            Clear();
            foreach (var material in sceneMaterials) Destroy(material);
        }
    }
}
