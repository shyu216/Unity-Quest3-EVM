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

        private EvmMagnifier m_evmMagnifier;
        [SerializeField] private ComputeShader m_ycrcbComputeShader;
        [SerializeField] private ComputeShader m_rgbComputeShader;

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

            m_evmMagnifier = new EvmMagnifier();

            (double[] highA, double[] highB) = ButterworthHelper.LowPass((byte)1, 1.0 / 30);
            m_debugText.text = $"\nHigh A: \n{string.Join(", ", highA)},\n High B: \n{string.Join(", ", highB)}";

        }

        private RenderTexture _renderTexture;
        private RenderTexture _ycrcbTexture;
        private RenderTexture _rgbTexture;
        private int _lastWidth = 0;
        private int _lastHeight = 0;

        private Color32[] GetPixelsFromRenderTexture(RenderTexture rt)
        {
            // Backup the currently active RenderTexture
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            // Create a new Texture2D with the same size as the RenderTexture
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);

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

        private void Update()
        {
            var frame = m_webCamTextureManager.WebCamTexture;
            if (frame != null)
            {
                int width = frame.width;
                int height = frame.height;

                // Create new RenderTextures only if dimensions have changed
                if (_renderTexture == null || width != _lastWidth || height != _lastHeight)
                {
                    Debug.Log($"Creating new RenderTextures. Width={width}, Height={height}");
                    ReleaseRenderTextures();

                    // 修改：使用RGBAHalf格式
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
                m_signalText.text = pixelInfo;
                Debug.Log(pixelInfo);

                m_magnifiedImage.texture = _rgbTexture;
                m_debugImage.texture = _renderTexture;
                m_debugImage2.texture = _ycrcbTexture;
                m_debugImage3.texture = _rgbTexture;
            }
        }

        private void ReleaseRenderTextures()
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

        private void OnDestroy()
        {
            Debug.Log("CameraViewerManager OnDestroy called. Releasing RenderTextures.");
            ReleaseRenderTextures();
        }
    }
}
