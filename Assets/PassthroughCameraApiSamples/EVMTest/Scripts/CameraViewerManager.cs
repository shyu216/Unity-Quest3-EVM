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

        private IEnumerator Start()
        {
            while (m_webCamTextureManager.WebCamTexture == null)
            {
                yield return null;
            }
            m_titleText.text = "EVM Test";
            // Set WebCamTexture GPU texture to the RawImage Ui element
            m_image.texture = m_webCamTextureManager.WebCamTexture;
        }

        private RenderTexture _renderTexture;
        private RenderTexture _ycrcbTexture;
        private RenderTexture _rgbTexture;
        private int _lastWidth = 0;
        private int _lastHeight = 0;

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
            if (_renderTexture == null || width != _lastWidth || height != _lastHeight)
            {
                Debug.Log($"Creating new RenderTextures. Width={width}, Height={height}");
                ReleaseRenderTextures4TestYCrCbConversion();

                // Caution! The Cr and Cb could be negative.
                _renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                _renderTexture.enableRandomWrite = true;
                _renderTexture.Create();

                _ycrcbTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                _ycrcbTexture.enableRandomWrite = true;
                _ycrcbTexture.Create();

                _rgbTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                _rgbTexture.enableRandomWrite = true;
                _rgbTexture.Create();

                _lastWidth = width;
                _lastHeight = height;
            }

            Graphics.Blit(frame, _renderTexture);

            // Convert RGB to YCrCb
            m_ycrcbComputeShader.SetTexture(0, "InputTexture", _renderTexture);
            m_ycrcbComputeShader.SetTexture(0, "OutputTexture", _ycrcbTexture);
            m_ycrcbComputeShader.Dispatch(0, width / 8, height / 8, 1);

            // Convert YCrCb back to RGB
            m_rgbComputeShader.SetTexture(0, "InputTexture", _ycrcbTexture);
            m_rgbComputeShader.SetTexture(0, "OutputTexture", _rgbTexture);
            m_rgbComputeShader.Dispatch(0, width / 8, height / 8, 1);

            Debug.Log($"CameraViewerManager Update called. Width={width}, Height={height}, Dim={frame.dimension}, sRGB={frame.isDataSRGB}");
            Color32[] pixels = GetPixelsFromRenderTexture(_renderTexture);
            Color32[] rgbPixels = GetPixelsFromRenderTexture(_rgbTexture);
            Color32[] ycrcbPixels = GetPixelsFromRenderTexture(_ycrcbTexture);

            string pixelInfo = $"Pixels: {pixels.Length},\n";
            for (int i = 0; i < Mathf.Min(10, pixels.Length); i++)
            {
                pixelInfo += $"[{i * 100}]={pixels[i * 100].r},{pixels[i * 100].g},{pixels[i * 100].b},{pixels[i * 100].a}," +
                    $" YCrCb={ycrcbPixels[i * 100].r},{ycrcbPixels[i * 100].g},{ycrcbPixels[i * 100].b},{ycrcbPixels[i * 100].a}," +
                    $" RGB={rgbPixels[i * 100].r},{rgbPixels[i * 100].g},{rgbPixels[i * 100].b},{rgbPixels[i * 100].a}\n";
            }
            Debug.Log(pixelInfo);

            m_debugImage.texture = _renderTexture;
            m_debugImage2.texture = _ycrcbTexture;
            m_debugImage3.texture = _rgbTexture;
        }

        void TestPyramid(WebCamTexture frame)
        {

            int width = frame.width;
            int height = frame.height;

            if (downsampledTextures == null || upsampledTextures == null || addedTextures == null ||
                _renderTexture == null || width != _lastWidth || height != _lastHeight)
            {
                Debug.Log($"Creating new RenderTextures for pyramid. Width={width}, Height={height}");
                _renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
                _renderTexture.enableRandomWrite = true;
                _renderTexture.Create();
                _lastWidth = width;
                _lastHeight = height;
                ReleaseRenderTextures4TestPyramid();
                downsampledTextures = new RenderTexture[4];
                upsampledTextures = new RenderTexture[4];
                addedTextures = new RenderTexture[1];
            }
            Graphics.Blit(frame, _renderTexture);

            // Downsample the RGB texture 4 times
            for (int i = 0; i < 4; i++)
            {
                Debug.Log($"Creating downsampled texture {i} with width={width / (1 << (i + 1))}, height={height / (1 << (i + 1))}");
                downsampledTextures[i] = new RenderTexture(width / (1 << (i + 1)), height / (1 << (i + 1)), 0, RenderTextureFormat.ARGB32);
                downsampledTextures[i].enableRandomWrite = true;
                downsampledTextures[i].Create();

                if (i == 0)
                {
                    m_downsampleComputeShader.SetTexture(0, "InputTexture", _renderTexture);
                }
                else
                {
                    m_downsampleComputeShader.SetTexture(0, "InputTexture", downsampledTextures[i - 1]);
                }
                m_downsampleComputeShader.SetTexture(0, "OutputTexture", downsampledTextures[i]);
                m_downsampleComputeShader.Dispatch(0, downsampledTextures[i].width / 8, downsampledTextures[i].height / 8, 1);
            }

            Debug.Log($"Downsampled textures created: {downsampledTextures.Length}");
            m_debugImage.texture = downsampledTextures[0];
            m_debugImage2.texture = downsampledTextures[1];

            // Upsample the downsampled textures
            for (int i = 3; i >= 0; i--)
            {
                Debug.Log($"Creating upsampled texture {i} with width={width / (1 << i)}, height={height / (1 << i)}");
                upsampledTextures[i] = new RenderTexture(width / (1 << i), height / (1 << i), 0, RenderTextureFormat.ARGB32);
                upsampledTextures[i].enableRandomWrite = true;
                upsampledTextures[i].Create();

                if (i == 3)
                {
                    m_upsampleComputeShader.SetTexture(0, "InputTexture", downsampledTextures[i]);
                }
                else
                {
                    m_upsampleComputeShader.SetTexture(0, "InputTexture", upsampledTextures[i + 1]);
                }
                m_upsampleComputeShader.SetTexture(0, "OutputTexture", upsampledTextures[i]);
                m_upsampleComputeShader.Dispatch(0, downsampledTextures[i].width / 8, downsampledTextures[i].height / 8, 1);
            }

            Debug.Log($"Upsampled textures created: {upsampledTextures.Length}");
            m_debugImage3.texture = upsampledTextures[0];
            m_debugImage4.texture = upsampledTextures[1];

            // Add the downsampled and upsampled textures
            m_addComputeShader.SetFloat("ScaleA", 1);
            m_addComputeShader.SetFloat("ScaleB", -1);

            addedTextures[0] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            addedTextures[0].enableRandomWrite = true;
            addedTextures[0].Create();
            m_addComputeShader.SetTexture(0, "TextureA", _rgbTexture);
            m_addComputeShader.SetTexture(0, "TextureB", upsampledTextures[0]);
            m_addComputeShader.SetTexture(0, "OutputTexture", addedTextures[0]);
            m_addComputeShader.Dispatch(0, width / 8, height / 8, 1);

            Debug.Log($"Added textures created: {addedTextures.Length}");
            m_debugImage5.texture = addedTextures[0];
        }


        private void Update()
        {
            var frame = m_webCamTextureManager.WebCamTexture;
            if (frame != null)
            {
                // 01 Test compute shader conversion between RGB and YCrCb
                // TestYCrCbConversion(frame);

                // 02 Test downsampling and upsampling pyramid
                TestPyramid(frame);

            }
        }

        private void ReleaseRenderTextures4TestYCrCbConversion()
        {
            if (_renderTexture != null)
            {
                if (RenderTexture.active == _renderTexture)
                    RenderTexture.active = null;
                _renderTexture.Release();
                _renderTexture = null;
            }
            if (_ycrcbTexture != null)
            {
                if (RenderTexture.active == _ycrcbTexture)
                    RenderTexture.active = null;
                _ycrcbTexture.Release();
                _ycrcbTexture = null;
            }
            if (_rgbTexture != null)
            {
                if (RenderTexture.active == _rgbTexture)
                    RenderTexture.active = null;
                _rgbTexture.Release();
                _rgbTexture = null;
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
