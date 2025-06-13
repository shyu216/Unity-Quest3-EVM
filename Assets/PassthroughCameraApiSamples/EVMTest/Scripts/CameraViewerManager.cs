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

        private IEnumerator Start()
        {
            while (m_webCamTextureManager.WebCamTexture == null)
            {
                yield return null;
            }
            m_titleText.text = "EVM Test";
            m_debugText.text += "\nWebCamTexture Object ready and playing.";
            // Set WebCamTexture GPU texture to the RawImage Ui element
            m_image.texture = m_webCamTextureManager.WebCamTexture;

            m_evmMagnifier = new EvmMagnifier();

            (double[] highA, double[] highB) = ButterworthHelper.LowPass((byte)1, 1.0 / 30);
            m_debugText.text += $"\nHigh A: \n{string.Join(", ", highA)},\n High B: \n{string.Join(", ", highB)}";
        }

        private RenderTexture ConvertToYCrCb(RenderTexture rgbTexture)
        {
            var renderTexture = new RenderTexture(rgbTexture.width, rgbTexture.height, 0);
            renderTexture.enableRandomWrite = true;
            renderTexture.Create();

            m_ycrcbComputeShader.SetTexture(0, "InputTexture", rgbTexture);
            m_ycrcbComputeShader.SetTexture(0, "OutputTexture", renderTexture);
            m_ycrcbComputeShader.Dispatch(0, rgbTexture.width / 8, rgbTexture.height / 8, 1);

            return renderTexture;
        }

        private RenderTexture ConvertToRGB(RenderTexture ycrcbTexture)
        {
            var renderTexture = new RenderTexture(ycrcbTexture.width, ycrcbTexture.height, 0);
            renderTexture.enableRandomWrite = true;
            renderTexture.Create();

            m_rgbComputeShader.SetTexture(0, "InputTexture", ycrcbTexture);
            m_rgbComputeShader.SetTexture(0, "OutputTexture", renderTexture);
            m_rgbComputeShader.Dispatch(0, ycrcbTexture.width / 8, ycrcbTexture.height / 8, 1);

            return renderTexture;
        }

        private void Update()
        {
            var frame = m_webCamTextureManager.WebCamTexture;
            if (frame != null)
            {
                Debug.Log($"WebCamTexture: Width={frame.width}, Height={frame.height}, FPS={frame.requestedFPS}");
                var renderTexture = new RenderTexture(frame.width, frame.height, 0);
                renderTexture.enableRandomWrite = true;
                renderTexture.Create();
                Graphics.Blit(frame, renderTexture);
                Debug.Log($"RenderTexture: Width={renderTexture.width}, Height={renderTexture.height}");

                var ycrcbTexture = ConvertToYCrCb(renderTexture);
                var rgbTexture = ConvertToRGB(ycrcbTexture);

                m_magnifiedImage.texture = rgbTexture;
            }
        }
    }
}
