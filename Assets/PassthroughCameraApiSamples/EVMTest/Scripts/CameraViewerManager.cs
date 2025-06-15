// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.EVMTest
{
    [MetaCodeSample("PassthroughCameraApiSamples-EVMTest")]
    public class CameraViewerManager : MonoBehaviour
    {
        // Create a field to attach the reference to the WebCamTextureManager prefab
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        [SerializeField] private Text m_debugText;
        [SerializeField] private RawImage m_image;

        [SerializeField] private Text m_titleText;
        [SerializeField] private RawImage m_magnifiedImage;

        [SerializeField] private ComputeShader m_ycrcbComputeShader;
        [SerializeField] private ComputeShader m_rgbComputeShader;
        [SerializeField] private ComputeShader m_amplificationComputeShader;
        [SerializeField] private ComputeShader m_butterworthComputeShader;
        [SerializeField] private ComputeShader m_downsampleComputeShader;
        [SerializeField] private ComputeShader m_upsampleComputeShader;
        [SerializeField] private ComputeShader m_addComputeShader;

        [SerializeField] private Text m_signalText;
        [SerializeField] private RawImage m_debugImage;
        [SerializeField] private RawImage m_debugImage2;
        [SerializeField] private RawImage m_debugImage3;
        [SerializeField] private RawImage m_debugImage4;
        [SerializeField] private RawImage m_debugImage5;

        public float fl = 60.0f / 60.0f; // Frequency low, 60 beats per minute
        public float fh = 100.0f / 60.0f; // Frequency high, 100 beats per minute
        public float fps = 30.0f; // Sample rate of the camera
        public float amplificationFactor = 50.0f; // Amplification factor
        public int nLevels = 4; // Number of levels in the pyramid
        public float attenuationFactor = 1.0f; // Attenuation factor for the Cr and Cb channels, 1 means no attenuation

        // Butterworth coefficients
        private float[] lowA;
        private float[] lowB;
        private float[] highA;
        private float[] highB;

        private IEnumerator Start()
        {
            while (m_webCamTextureManager.WebCamTexture == null)
            {
                yield return null;
            }
            m_titleText.text = "EVM Test";
            // Set WebCamTexture GPU texture to the RawImage Ui element
            m_image.texture = m_webCamTextureManager.WebCamTexture;

            // Set frame rate of FixedUpdate
            Time.fixedDeltaTime = 1.0f / fps;
            // Set frame rate of WebCamTexture, not work
            // m_webCamTextureManager.WebCamTexture.requestedFPS = fps;
            // Set frame rate of Update, not work
            // Application.targetFrameRate = (int)fps;
            // m_titleText.text += $" Cam: {m_webCamTextureManager.WebCamTexture.requestedFPS}, Upd: {1.0f / Time.fixedDeltaTime:F2}, App: {Application.targetFrameRate}";
        }

        private RenderTexture renderTexture;
        private RenderTexture ycrcbTexture;
        private RenderTexture rgbTexture;
        private int lastWidth = 0;
        private int lastHeight = 0;

        private RenderTexture[] downsampledTextures;
        private RenderTexture[] upsampledTextures;
        private RenderTexture[] addedTextures;

        private Color32[] GetPixelsFromRenderTexture(RenderTexture rt)
        {
            // Backup the currently active RenderTexture
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            // Create a new Texture2D with the same size as the RenderTexture
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);

            // Read the RenderTexture contents into the Texture2D
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            // Restore the previously active RenderTexture
            RenderTexture.active = prev;

            // Get the pixel data
            Color32[] pixels = tex.GetPixels32();

            // Clean up the temporary Texture2D
            Destroy(tex);

            return pixels;
        }

        private void TestYCrCbConversion(WebCamTexture frame)
        {
            int width = frame.width;
            int height = frame.height;

            // Create new RenderTextures only if dimensions have changed
            if (renderTexture == null || width != lastWidth || height != lastHeight)
            {
                Debug.Log($"Creating new RenderTextures. Width={width}, Height={height}");
                ReleaseRenderTextures4TestYCrCbConversion();

                // Caution! The Cr and Cb could be negative.
                renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                renderTexture.enableRandomWrite = true;
                renderTexture.Create();

                ycrcbTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                ycrcbTexture.enableRandomWrite = true;
                ycrcbTexture.Create();

                rgbTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                rgbTexture.enableRandomWrite = true;
                rgbTexture.Create();

                lastWidth = width;
                lastHeight = height;
            }

            Graphics.Blit(frame, renderTexture);

            // Convert RGB to YCrCb
            m_ycrcbComputeShader.SetTexture(0, "InputTexture", renderTexture);
            m_ycrcbComputeShader.SetTexture(0, "OutputTexture", ycrcbTexture);
            m_ycrcbComputeShader.Dispatch(0, width / 8, height / 8, 1);

            // Convert YCrCb back to RGB
            m_rgbComputeShader.SetTexture(0, "InputTexture", ycrcbTexture);
            m_rgbComputeShader.SetTexture(0, "OutputTexture", rgbTexture);
            m_rgbComputeShader.Dispatch(0, width / 8, height / 8, 1);

            Debug.Log($"CameraViewerManager Update called. Width={width}, Height={height}, Dim={frame.dimension}, sRGB={frame.isDataSRGB}");
            Color32[] pixels = GetPixelsFromRenderTexture(renderTexture);
            Color32[] rgbPixels = GetPixelsFromRenderTexture(rgbTexture);
            Color32[] ycrcbPixels = GetPixelsFromRenderTexture(ycrcbTexture);

            string pixelInfo = $"Pixels: {pixels.Length},\n";
            for (int i = 0; i < Mathf.Min(10, pixels.Length); i++)
            {
                pixelInfo += $"[{i * 100}]={pixels[i * 100].r},{pixels[i * 100].g},{pixels[i * 100].b},{pixels[i * 100].a}," +
                    $" YCrCb={ycrcbPixels[i * 100].r},{ycrcbPixels[i * 100].g},{ycrcbPixels[i * 100].b},{ycrcbPixels[i * 100].a}," +
                    $" RGB={rgbPixels[i * 100].r},{rgbPixels[i * 100].g},{rgbPixels[i * 100].b},{rgbPixels[i * 100].a}\n";
            }
            Debug.Log(pixelInfo);

            m_debugImage.texture = renderTexture;
            m_debugImage2.texture = ycrcbTexture;
            m_debugImage3.texture = rgbTexture;
        }

        private void TestPyramid(WebCamTexture frame)
        {

            int width = frame.width;
            int height = frame.height;

            if (downsampledTextures == null || upsampledTextures == null || addedTextures == null ||
                renderTexture == null || width != lastWidth || height != lastHeight)
            {
                Debug.Log($"Creating new RenderTextures for pyramid. Width={width}, Height={height}");
                renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                renderTexture.enableRandomWrite = true;
                renderTexture.Create();
                lastWidth = width;
                lastHeight = height;
                ReleaseRenderTextures4TestPyramid();
                downsampledTextures = new RenderTexture[4];
                upsampledTextures = new RenderTexture[4];
                addedTextures = new RenderTexture[4];
                for (int i = 0; i < 4; i++)
                {
                    downsampledTextures[i] = new RenderTexture(width / (1 << (i + 1)), height / (1 << (i + 1)), 0, RenderTextureFormat.ARGBHalf);
                    downsampledTextures[i].enableRandomWrite = true;
                    downsampledTextures[i].Create();
                    upsampledTextures[i] = new RenderTexture(width / (1 << i), height / (1 << i), 0, RenderTextureFormat.ARGBHalf);
                    upsampledTextures[i].enableRandomWrite = true;
                    upsampledTextures[i].Create();
                    addedTextures[i] = new RenderTexture(width / (1 << i), height / (1 << i), 0, RenderTextureFormat.ARGBHalf);
                    addedTextures[i].enableRandomWrite = true;
                    addedTextures[i].Create();
                }
            }
            Graphics.Blit(frame, renderTexture);

            // Downsample the RGB texture 4 times, namely Gaussian pyramid
            for (int i = 0; i < 4; i++)
            {
                Debug.Log($"Creating downsampled texture {i} with width={width / (1 << (i + 1))}, height={height / (1 << (i + 1))}");
                if (i == 0)
                {
                    m_downsampleComputeShader.SetTexture(0, "InputTexture", renderTexture);
                }
                else
                {
                    m_downsampleComputeShader.SetTexture(0, "InputTexture", downsampledTextures[i - 1]);
                }
                m_downsampleComputeShader.SetTexture(0, "OutputTexture", downsampledTextures[i]);
                m_downsampleComputeShader.Dispatch(0, downsampledTextures[i].width / 8, downsampledTextures[i].height / 8, 1);
            }

            Debug.Log($"Downsampled textures created: {downsampledTextures.Length}");
            m_debugImage4.texture = downsampledTextures[0];
            m_debugImage5.texture = downsampledTextures[3];

            // Upsample the downsampled textures
            for (int i = 3; i >= 0; i--)
            {
                Debug.Log($"Creating upsampled texture {i} with width={width / (1 << i)}, height={height / (1 << i)}");

                m_upsampleComputeShader.SetTexture(0, "InputTexture", downsampledTextures[i]);
                m_upsampleComputeShader.SetTexture(0, "OutputTexture", upsampledTextures[i]);
                m_upsampleComputeShader.Dispatch(0, downsampledTextures[i].width / 8, downsampledTextures[i].height / 8, 1);
            }

            Debug.Log($"Upsampled textures created: {upsampledTextures.Length}");
            m_debugImage2.texture = upsampledTextures[0];
            m_debugImage3.texture = upsampledTextures[3];

            // Add the downsampled and upsampled textures, namely Laplacian pyramid
            m_addComputeShader.SetFloat("ScaleA", 1.0f);
            m_addComputeShader.SetFloat("ScaleB", -1.0f);
            for (int i = 0; i < 4; i++)
            {
                if (i == 0)
                {
                    Debug.Log($"Texture A: {renderTexture.width}x{renderTexture.height}, " +
                              $"Texture B: {upsampledTextures[0].width}x{upsampledTextures[0].height}, " +
                              $"Output Texture: {addedTextures[0].width}x{addedTextures[0].height}");
                    m_addComputeShader.SetTexture(0, "TextureA", renderTexture);
                }
                else
                {
                    Debug.Log($"Texture A: {downsampledTextures[i - 1].width}x{downsampledTextures[i - 1].height}, " +
                              $"Texture B: {upsampledTextures[i].width}x{upsampledTextures[i].height}, " +
                              $"Output Texture: {addedTextures[i].width}x{addedTextures[i].height}");
                    m_downsampleComputeShader.SetTexture(0, "TextureA", downsampledTextures[i - 1]);
                }
                m_addComputeShader.SetTexture(0, "TextureB", upsampledTextures[i]);
                m_addComputeShader.SetTexture(0, "OutputTexture", addedTextures[i]);
                m_addComputeShader.Dispatch(0, addedTextures[i].width / 8, addedTextures[i].height / 8, 1);
            }

            Debug.Log($"Added textures created: {addedTextures.Length}");
            m_debugImage.texture = addedTextures[0];
        }

        private float deltaTime = 0.0f;
        private void TestFrameRate(WebCamTexture frame)
        {
            deltaTime += Time.deltaTime;
            if (frame != null)
            {
                float currentFps = 1.0f / deltaTime;
                m_debugText.text = $"Target FPS: {fps}\n" +
                                    $"Current FPS: {currentFps:F2}\n";
                Debug.Log($"Current FPS: {currentFps:F2}");
                deltaTime = 0.0f;
            }
            else
            {
                Debug.LogWarning("WebCamTexture is null.");
            }
        }

        private void Update()
        {
            var frame = m_webCamTextureManager.WebCamTexture;
            if (frame != null)
            {
                // 01 Test compute shader conversion between RGB and YCrCb
                // TestYCrCbConversion(frame);

                // 02 Test downsampling and upsampling pyramid
                // TestPyramid(frame);

                // 03 Test frame rate
                // TestFrameRate(frame);
            }
        }

        private int fixedFrameCount = 0;

        private void FixedUpdate()
        {
            var frame = m_webCamTextureManager.WebCamTexture;
            if (frame != null)
            {
                fixedFrameCount++;
                Debug.Log($"Fixed Frame rate: {fixedFrameCount / Time.fixedTime}");
            }
        }

        private void ReleaseRenderTextures4TestYCrCbConversion()
        {
            if (renderTexture != null)
            {
                if (RenderTexture.active == renderTexture)
                    RenderTexture.active = null;
                renderTexture.Release();
                renderTexture = null;
            }
            if (ycrcbTexture != null)
            {
                if (RenderTexture.active == ycrcbTexture)
                    RenderTexture.active = null;
                ycrcbTexture.Release();
                ycrcbTexture = null;
            }
            if (rgbTexture != null)
            {
                if (RenderTexture.active == rgbTexture)
                    RenderTexture.active = null;
                rgbTexture.Release();
                rgbTexture = null;
            }
        }

        private void ReleaseRenderTextures4TestPyramid()
        {

            if (downsampledTextures != null)
            {
                foreach (var rt in downsampledTextures)
                {
                    if (rt != null)
                    {
                        if (RenderTexture.active == rt)
                            RenderTexture.active = null;
                        rt.Release();
                    }
                }
                downsampledTextures = null;
            }

            if (upsampledTextures != null)
            {
                foreach (var rt in upsampledTextures)
                {
                    if (rt != null)
                    {
                        if (RenderTexture.active == rt)
                            RenderTexture.active = null;
                        rt.Release();
                    }
                }
                upsampledTextures = null;
            }

            if (addedTextures != null)
            {
                foreach (var rt in addedTextures)
                {
                    if (rt != null)
                    {
                        if (RenderTexture.active == rt)
                            RenderTexture.active = null;
                        rt.Release();
                    }
                }
                addedTextures = null;
            }
        }

        private void OnDestroy()
        {
            Debug.Log("CameraViewerManager OnDestroy called. Releasing RenderTextures.");
            ReleaseRenderTextures4TestYCrCbConversion();
            ReleaseRenderTextures4TestPyramid();
        }
    }
}
