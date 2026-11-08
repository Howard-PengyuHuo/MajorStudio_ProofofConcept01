using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CarryOn.Taylor
{
    // Re-render the existing overlay UI meshes, excluding the subtitle itself.
    // This includes draggable documents without screen readback or recursive subtitle capture.
    [DefaultExecutionOrder(1100)]
    public sealed class SubtitleUIBlur : MonoBehaviour
    {
        [SerializeField] private RectTransform desktop;
        [SerializeField] private RawImage background;
        [SerializeField] private Shader blurShader;
        [SerializeField, Range(2, 8)] private int downsample = 4;
        [SerializeField, Range(0, 8)] private float blurRadius = 2;
        [SerializeField, Range(5, 60)] private float refreshRate = 30;
        private readonly List<Graphic> graphics = new List<Graphic>();
        private sealed class MeshCopy
        {
            public Mesh mesh;
            public bool dirty = true;
            public UnityEngine.Events.UnityAction onDirty;
        }
        private readonly Dictionary<Graphic, MeshCopy> copies = new Dictionary<Graphic, MeshCopy>();
        private readonly Vector3[] corners = new Vector3[4];
        private RenderTexture capture, temporary, blurred;
        private Material horizontal, vertical;
        private CommandBuffer commands;
        private float nextRefresh;
        private MaterialPropertyBlock properties;
        private CanvasGroup visibility;
        private int lastSignature;
        private Rect lastRegion;
        private bool hasCapture;
        public int CaptureCount { get; private set; }

        private void OnEnable()
        {
            nextRefresh = 0;
            hasCapture = false;
            visibility = GetComponent<CanvasGroup>();
            properties ??= new MaterialPropertyBlock();
            if (blurShader == null) return;
            horizontal = new Material(blurShader) { hideFlags = HideFlags.HideAndDontSave };
            vertical = new Material(blurShader) { hideFlags = HideFlags.HideAndDontSave };
            commands = new CommandBuffer { name = "Hacker subtitle backdrop" };
        }

        private void LateUpdate()
        {
            if (desktop == null || background == null || commands == null) return;
            if (visibility != null && visibility.alpha <= 0) return;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 1f / refreshRate;
            background.rectTransform.GetWorldCorners(corners);
            // Only render the subtitle area, plus a blur margin to avoid hard edge sampling.
            float margin = Mathf.Max(8, blurRadius * downsample * 4);
            var region = Rect.MinMaxRect(corners[0].x - margin, corners[0].y - margin,
                corners[2].x + margin, corners[2].y + margin);
            background.uvRect = new Rect(margin / region.width, margin / region.height,
                (corners[2].x - corners[0].x) / region.width, (corners[2].y - corners[0].y) / region.height);
            desktop.GetComponentsInChildren(false, graphics);
            graphics.Sort((a, b) => a.depth.CompareTo(b.depth));
            int signature = downsample;
            bool dirty = false;
            unchecked
            {
                signature = signature * 31 + blurRadius.GetHashCode();
                foreach (var graphic in graphics)
                {
                    if (!graphic.isActiveAndEnabled || graphic.transform.IsChildOf(transform)) continue;
                    signature = signature * 31 + graphic.GetHashCode();
                    signature = signature * 31 + graphic.depth;
                    signature = signature * 31 + graphic.transform.localToWorldMatrix.GetHashCode();
                    signature = signature * 31 + graphic.color.GetHashCode();
                    signature = signature * 31 + graphic.canvasRenderer.GetInheritedAlpha().GetHashCode();
                    signature = signature * 31 + graphic.canvasRenderer.GetColor().GetHashCode();
                    signature = signature * 31 + graphic.mainTexture.GetHashCode();
                    if (copies.TryGetValue(graphic, out var copy)) dirty |= copy.dirty;
                    else dirty |= graphic.canvasRenderer.GetMesh()?.vertexCount > 0;
                }
            }
            if (hasCapture && !dirty && signature == lastSignature && region == lastRegion) return;
            EnsureTextures(region);
            commands.Clear();
            commands.SetRenderTarget(capture);
            commands.SetViewport(new Rect(0, 0, capture.width, capture.height));
            commands.ClearRenderTarget(true, true, new Color(.1f, .1f, .1f, 1));
            var projection = Matrix4x4.Ortho(region.xMin, region.xMax, region.yMin, region.yMax, -10000, 10000);
            var gpuProjection = GL.GetGPUProjectionMatrix(projection, true);
            foreach (var graphic in graphics)
            {
                if (!graphic.isActiveAndEnabled || graphic.transform.IsChildOf(transform) || graphic.canvasRenderer.cull) continue;
                var sourceMesh = graphic.canvasRenderer.GetMesh();
                if (sourceMesh == null || sourceMesh.vertexCount == 0) continue;
                if (!copies.TryGetValue(graphic, out var copy))
                {
                    copy = new MeshCopy();
                    copy.onDirty = () => copy.dirty = true;
                    graphic.RegisterDirtyVerticesCallback(copy.onDirty);
                    copies.Add(graphic, copy);
                }
                // CanvasRenderer meshes use Unity's internal UI buffers; copy them for DrawMesh.
                if (copy.dirty || copy.mesh == null || copy.mesh.vertexCount != sourceMesh.vertexCount)
                {
                    if (copy.mesh != null) Destroy(copy.mesh);
                    copy.mesh = Instantiate(sourceMesh);
                    copy.mesh.hideFlags = HideFlags.HideAndDontSave;
                    copy.dirty = false;
                }
                properties.Clear();
                properties.SetTexture("_MainTex", graphic.mainTexture);
                var tint = graphic.canvasRenderer.GetColor();
                tint.a *= graphic.canvasRenderer.GetInheritedAlpha();
                properties.SetColor("_Color", tint);
                properties.SetMatrix("_CaptureMVP", gpuProjection * graphic.transform.localToWorldMatrix);
                properties.SetFloat("_IsSDF", graphic is TMPro.TMP_Text ? 1 : 0);
                commands.DrawMesh(copy.mesh, Matrix4x4.identity, horizontal, 0, 1, properties);
            }
            horizontal.SetVector("_Direction", new Vector4(blurRadius / capture.width, 0, 0, 0));
            vertical.SetVector("_Direction", new Vector4(0, blurRadius / capture.height, 0, 0));
            commands.Blit(capture, temporary, horizontal, 0);
            commands.Blit(temporary, blurred, vertical, 0);
            Graphics.ExecuteCommandBuffer(commands);
            background.texture = blurred;
            lastSignature = signature;
            lastRegion = region;
            hasCapture = true;
            CaptureCount++;
        }

        private void EnsureTextures(Rect region)
        {
            int width = Mathf.Max(1, Mathf.CeilToInt(region.width / downsample));
            int height = Mathf.Max(1, Mathf.CeilToInt(region.height / downsample));
            if (capture != null && capture.width == width && capture.height == height) return;
            ReleaseTextures();
            capture = CreateTexture(width, height, 24);
            temporary = CreateTexture(width, height, 0);
            blurred = CreateTexture(width, height, 0);
        }

        private static RenderTexture CreateTexture(int width, int height, int depth)
        {
            var texture = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            texture.Create();
            return texture;
        }

        private void ReleaseTextures()
        {
            if (background != null) background.texture = null;
            foreach (var texture in new[] { capture, temporary, blurred })
                if (texture != null) { texture.Release(); Destroy(texture); }
            capture = temporary = blurred = null;
        }

        private void OnDisable()
        {
            ReleaseTextures();
            commands?.Release();
            commands = null;
            foreach (var item in copies)
            {
                if (item.Key != null) item.Key.UnregisterDirtyVerticesCallback(item.Value.onDirty);
                if (item.Value.mesh != null) Destroy(item.Value.mesh);
            }
            copies.Clear();
            if (horizontal != null) Destroy(horizontal);
            if (vertical != null) Destroy(vertical);
        }
    }
}
