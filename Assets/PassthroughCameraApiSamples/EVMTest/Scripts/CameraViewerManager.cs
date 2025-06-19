// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.Samples;
using Unity.Mathematics;
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



        [SerializeField] private GameObject m_curveSystem1;
        [SerializeField] private GameObject m_curveSystem2;
        [SerializeField] private GameObject m_curveSystem3;
        [SerializeField] private GameObject m_curveSystem4;
        [SerializeField] private GameObject m_curveSystem5;
        [SerializeField] private GameObject m_curveSystem6;



        private float fps = 30.0f; // Sample rate of the camera
        private float amplificationFactor = 50.0f; // Amplification factor
        private int nLevels = 8; // Number of levels in the pyramid
        private float attenuationFactor = 1.0f; // Attenuation factor for the Cr and Cb channels, 1 means no attenuation



        // Heart rate related parameters
        // private float fl = 60.0f / 60.0f; // Frequency low, 60 beats per minute
        // private float fh = 100.0f / 60.0f; // Frequency high, 100 beats per minute



        // Breath rate related parameters
        private float fl = 20.0f / 60.0f; // Frequency low, 12 breaths per minute, 1 breath every 5 seconds
        private float fh = 40.0f / 60.0f; // Frequency high, 20 breaths per minute, 1 breath every 3 seconds



        // Butterworth coefficients
        private float[] lowA;
        private float[] lowB;
        private float[] highA;
        private float[] highB;



        // ROI Bounding Box, x, y, width, height
        private int4 roiBoundingBox;




        // EVM related fields (Compute shaders and RenderTextures)
        [SerializeField] private ComputeShader m_ycrcbComputeShader;
        [SerializeField] private ComputeShader m_rgbComputeShader;
        [SerializeField] private ComputeShader m_amplificationComputeShader;
        [SerializeField] private ComputeShader m_butterworthComputeShader;
        [SerializeField] private ComputeShader m_downsampleComputeShader;
        [SerializeField] private ComputeShader m_upsampleComputeShader;
        [SerializeField] private ComputeShader m_addComputeShader;
        [SerializeField] private ComputeShader m_drawROIComputeShader;
        [SerializeField] private ComputeShader m_sumROIComputeShader;
        private RenderTexture inputTexture;
        private RenderTexture[] gaussianPyramidTextures;
        private RenderTexture[] prevTextures;
        private RenderTexture[] lowpass1Textures;
        private RenderTexture[] lowpass2Textures;
        private RenderTexture[] filteredTextures;
        private RenderTexture[] upsampledTextures;
        private RenderTexture[] reconstructedTextures;
        private RenderTexture amplifiedTexture;
        private RenderTexture ycrcbTexture;
        private RenderTexture rgbTexture;
        private RenderTexture roiTexture;

        // Fixed update related fields
        private int lastWidth = 0;
        private int lastHeight = 0;
        private int actualFps = 0;
        private int actualNLevels = 0;
        private bool resetTexturesFlag = true; // Reset textures of EVM



        // Curve visualization related fields
        private ComputeBuffer ycrcbSumBuffer;
        private ComputeBuffer ycrcbCountBuffer;
        private ComputeBuffer rgbSumBuffer;
        private ComputeBuffer rgbCountBuffer;
        private const int signalQueueLength = 200;
        private Queue<uint> historyR = new Queue<uint>(signalQueueLength);
        private Queue<uint> historyG = new Queue<uint>(signalQueueLength);
        private Queue<uint> historyB = new Queue<uint>(signalQueueLength);
        private Queue<uint> historyY = new Queue<uint>(signalQueueLength);
        private Queue<uint> historyCr = new Queue<uint>(signalQueueLength);
        private Queue<uint> historyCb = new Queue<uint>(signalQueueLength);



        private IEnumerator Start()
        {
            while (m_webCamTextureManager.WebCamTexture == null)
            {
                yield return null;
            }
            m_titleText.text = $"EVM Test, Freq Low: {fl:F2}, Freq High: {fh:F2}, Ampli Factor: {amplificationFactor}, Levels: {nLevels}";
            // Set WebCamTexture GPU texture to the RawImage Ui element
            m_image.texture = m_webCamTextureManager.WebCamTexture;

            // Set frame rate of FixedUpdate
            Time.fixedDeltaTime = 1.0f / fps;
            // Set frame rate of WebCamTexture, not work
            // m_webCamTextureManager.WebCamTexture.requestedFPS = fps;
            // Set frame rate of Update, not work
            // Application.targetFrameRate = (int)fps;
            // m_titleText.text += $" Cam: {m_webCamTextureManager.WebCamTexture.requestedFPS}, Upd: {1.0f / Time.fixedDeltaTime:F2}, App: {Application.targetFrameRate}";

            resetTexturesFlag = true; // Reset textures of EVM
        }

        private void FixedUpdate()
        {
            var frame = m_webCamTextureManager.WebCamTexture;
            if (frame != null)
            {
                // Note: Chrominance attenuation is not implemented
                if (attenuationFactor < 0.99f)
                {
                    Debug.LogWarning("Chrominance attenuation is not implemented in this version.");
                }

                // EVM Step: set Butterworth coefficients according to current fps
                int currentFps = (int)(1.0f / Time.fixedDeltaTime);
                Debug.Log($"FixedUpdate called. Current FPS: {currentFps}, Actual FPS: {actualFps}, Target FPS: {fps}");
                if (currentFps != actualFps)
                {
                    actualFps = currentFps;
                    var (lowA_d, lowB_d) = ButterworthHelper.LowPass(1, fl / fps);
                    lowA = System.Array.ConvertAll(lowA_d, x => (float)x);
                    lowB = System.Array.ConvertAll(lowB_d, x => (float)x);
                    var (highA_d, highB_d) = ButterworthHelper.LowPass(1, fh / fps);
                    highA = System.Array.ConvertAll(highA_d, x => (float)x);
                    highB = System.Array.ConvertAll(highB_d, x => (float)x);

                    // Update the Butterworth compute shader with the new coefficients
                    m_butterworthComputeShader.SetFloats("LowA", lowA);
                    m_butterworthComputeShader.SetFloats("LowB", lowB);
                    m_butterworthComputeShader.SetFloats("HighA", highA);
                    m_butterworthComputeShader.SetFloats("HighB", highB);
                }

                // EVM Step: prepare RenderTextures for computing
                int width = frame.width;
                int height = frame.height;
                if (width != lastWidth || height != lastHeight)
                {
                    lastWidth = width;
                    lastHeight = height;
                    Debug.Log($"Creating new RenderTextures for EVM. Width={width}, Height={height}");

                    // Caution! Hard to handle the resize in upsample, it is better to resize the input frame before processing the whole EVM steps.
                    int validNLevels = 1;
                    int w = width;
                    int h = height;
                    while (w > 8 && h > 8 && validNLevels < nLevels)
                    {
                        // Break if width or height is not even
                        if (w % 2 != 0 || h % 2 != 0)
                        {
                            Debug.LogWarning($"Width or height is not even. Width={w}, Height={h}. Stopping pyramid levels at {validNLevels}.");
                            break;
                        }
                        w /= 2;
                        h /= 2;
                        validNLevels++;
                    }
                    actualNLevels = Mathf.Min(validNLevels, nLevels);
                    Debug.Log($"Desired number of levels: {nLevels}, Actual number of levels that runs without bugs: {actualNLevels}");

                    // Create RenderTextures for YCrCb conversion
                    inputTexture = CreateRenderTexture(width, height);
                    rgbTexture = CreateRenderTexture(width, height);

                    // Create RenderTextures for EVM
                    ReleaseRenderTextures4EVM();
                    gaussianPyramidTextures = new RenderTexture[actualNLevels];
                    prevTextures = new RenderTexture[actualNLevels];
                    lowpass1Textures = new RenderTexture[actualNLevels];
                    lowpass2Textures = new RenderTexture[actualNLevels];
                    filteredTextures = new RenderTexture[actualNLevels];
                    upsampledTextures = new RenderTexture[actualNLevels];
                    reconstructedTextures = new RenderTexture[actualNLevels];
                    for (int i = 0; i < actualNLevels; i++)
                    {
                        int levelWidth = width / (1 << i);
                        int levelHeight = height / (1 << i);
                        gaussianPyramidTextures[i] = CreateRenderTexture(levelWidth, levelHeight);
                        prevTextures[i] = CreateRenderTexture(levelWidth, levelHeight);
                        lowpass1Textures[i] = CreateRenderTexture(levelWidth, levelHeight);
                        lowpass2Textures[i] = CreateRenderTexture(levelWidth, levelHeight);
                        filteredTextures[i] = CreateRenderTexture(levelWidth, levelHeight);
                        upsampledTextures[i] = CreateRenderTexture(levelWidth, levelHeight);
                        reconstructedTextures[i] = CreateRenderTexture(levelWidth, levelHeight);
                    }
                    amplifiedTexture = CreateRenderTexture(width, height);
                    ycrcbTexture = CreateRenderTexture(width, height);

                    // Manually set ROI bounding box, for example, the center of the frame
                    roiBoundingBox = new int4(width / 2, height / 2, 100, 100);
                    roiTexture = CreateRenderTexture(width, height);
                    m_drawROIComputeShader.SetInts("roi", roiBoundingBox.x, roiBoundingBox.y, roiBoundingBox.z, roiBoundingBox.w);
                    m_drawROIComputeShader.SetInt("boxWidth", 10);
                    m_drawROIComputeShader.SetFloat("outsideAlpha", 1.0f);
                    m_sumROIComputeShader.SetInts("roi", roiBoundingBox.x, roiBoundingBox.y, roiBoundingBox.z, roiBoundingBox.w);

                    ycrcbSumBuffer = new ComputeBuffer(3, sizeof(uint));
                    ycrcbCountBuffer = new ComputeBuffer(1, sizeof(uint));
                    rgbSumBuffer = new ComputeBuffer(3, sizeof(uint));
                    rgbCountBuffer = new ComputeBuffer(1, sizeof(uint));
                }

                // EVM Step: Covert the current frame to YCrCb
                Graphics.Blit(frame, inputTexture);
                m_ycrcbComputeShader.SetTexture(0, "InputTexture", inputTexture);
                m_ycrcbComputeShader.SetTexture(0, "OutputTexture", gaussianPyramidTextures[0]);
                m_ycrcbComputeShader.Dispatch(0, width / 8, height / 8, 1);

                // EVM Step: Spatial decomposition, build Gaussian pyramid from the current frame
                // Graphics.Blit(frame, gaussianPyramidTextures[0]);
                for (int i = 1; i < actualNLevels; i++)
                {
                    m_downsampleComputeShader.SetTexture(0, "InputTexture", gaussianPyramidTextures[i - 1]);
                    m_downsampleComputeShader.SetTexture(0, "OutputTexture", gaussianPyramidTextures[i]);
                    m_downsampleComputeShader.Dispatch(0, gaussianPyramidTextures[i].width / 8, gaussianPyramidTextures[i].height / 8, 1);
                }

                // EVM Step: initialize prev, lowpass1, lowpass2
                if (resetTexturesFlag)
                {
                    Debug.LogWarning("Resetting textures for EVM.");
                    for (int i = 0; i < actualNLevels; i++)
                    {
                        Debug.Log($"Resetting textures for level {i}: {prevTextures[i].width}x{prevTextures[i].height}");
                        PerformCopy(gaussianPyramidTextures[i], prevTextures[i]);
                        PerformCopy(gaussianPyramidTextures[i], lowpass1Textures[i]);
                        PerformCopy(gaussianPyramidTextures[i], lowpass2Textures[i]);
                    }
                    resetTexturesFlag = false;
                }

                // EVM Step: Temproal filtering
                for (int i = 0; i < actualNLevels; i++)
                {
                    // Update lowpass1, lowpass2, and copy current frame to prev
                    Debug.Log($"Computing lowpass1 and lowpass2 for level {i}: {lowpass1Textures[i].width}x{lowpass1Textures[i].height}");
                    m_butterworthComputeShader.SetTexture(0, "InputTexture", gaussianPyramidTextures[i]);
                    m_butterworthComputeShader.SetTexture(0, "LowPass1Texture", lowpass1Textures[i]);
                    m_butterworthComputeShader.SetTexture(0, "LowPass2Texture", lowpass2Textures[i]);
                    m_butterworthComputeShader.SetTexture(0, "PrevTexture", prevTextures[i]);
                    m_butterworthComputeShader.Dispatch(0, gaussianPyramidTextures[i].width / 8, gaussianPyramidTextures[i].height / 8, 1);

                    // Obtain the filtered texture
                    PerformSubtract(lowpass1Textures[i], lowpass2Textures[i], filteredTextures[i]);
                }

                // EVM Step: Upsample and amplify the filtered textures
                PerformCopy(filteredTextures[actualNLevels - 1], reconstructedTextures[actualNLevels - 1]);
                for (int i = actualNLevels - 1; i > 0; i--)
                {
                    Debug.Log($"Upsampling and amplifying filtered texture for level {i}: {filteredTextures[i].width}x{filteredTextures[i].height}");
                    m_upsampleComputeShader.SetTexture(0, "InputTexture", reconstructedTextures[i]);
                    m_upsampleComputeShader.SetTexture(0, "OutputTexture", upsampledTextures[i - 1]);
                    m_upsampleComputeShader.Dispatch(0, reconstructedTextures[i].width / 8, reconstructedTextures[i].height / 8, 1);

                    PerformAdd(filteredTextures[i - 1], upsampledTextures[i - 1], reconstructedTextures[i - 1]);
                }

                // EVM Step: Amplify the final reconstructed texture
                PerformAmplification(reconstructedTextures[0], amplifiedTexture);
                PerformAdd(gaussianPyramidTextures[0], amplifiedTexture, ycrcbTexture);

                // EVM Step: Covert the final output to RGB
                m_rgbComputeShader.SetTexture(0, "InputTexture", ycrcbTexture);
                m_rgbComputeShader.SetTexture(0, "OutputTexture", rgbTexture);
                m_rgbComputeShader.Dispatch(0, width / 8, height / 8, 1);

                // Display
                // m_debugImage.texture = gaussianPyramidTextures[actualNLevels - 1];
                // m_debugImage2.texture = lowpass1Textures[actualNLevels - 1];
                // m_debugImage3.texture = lowpass2Textures[actualNLevels - 1];
                // m_debugImage4.texture = reconstructedTextures[0];
                // m_debugImage5.texture = amplifiedTexture;
                m_magnifiedImage.texture = rgbTexture;

                // Draw ROI
                m_drawROIComputeShader.SetTexture(0, "Source", inputTexture);
                m_drawROIComputeShader.SetTexture(0, "Result", roiTexture);
                m_drawROIComputeShader.Dispatch(0, width / 8, height / 8, 1);
                // m_debugImage6.texture = roiTexture;
                m_image.texture = roiTexture;

                // Sum ROI
                string DebugInfo = $"ROI (x={roiBoundingBox.x},y={roiBoundingBox.y},w={roiBoundingBox.z},h={roiBoundingBox.w})\n";
                uint[] rgbSumInit = new uint[3] { 0, 0, 0 };
                uint[] roiCountInit = new uint[1] { 0 };
                rgbSumBuffer.SetData(rgbSumInit);
                rgbCountBuffer.SetData(roiCountInit);
                m_sumROIComputeShader.SetTexture(0, "Source", rgbTexture);
                m_sumROIComputeShader.SetBuffer(0, "Sum", rgbSumBuffer);
                m_sumROIComputeShader.SetBuffer(0, "Count", rgbCountBuffer);
                m_sumROIComputeShader.Dispatch(0, width / 8, height / 8, 1);
                uint[] rgbSum = new uint[3];
                uint[] roiCount = new uint[1];
                rgbSumBuffer.GetData(rgbSum);
                rgbCountBuffer.GetData(roiCount);
                Debug.Log($"ROI RGB Sum in RGB: R={rgbSum[0] / roiCount[0]}, G={rgbSum[1] / roiCount[0]}, B={rgbSum[2] / roiCount[0]}");
                if (roiCount[0] > 0)
                {
                    DebugInfo += $"ROI RGB Sum in RGB: \nR={rgbSum[0] / roiCount[0]}, \nG={rgbSum[1] / roiCount[0]}, \nB={rgbSum[2] / roiCount[0]}\n";
                }
                else
                {
                    DebugInfo += "ROI RGB Sum in RGB: \nR=Placeholder, \nG=Placeholder, \nB=Placeholder\n";
                }
                m_debugText.text = DebugInfo;

                m_sumROIComputeShader.SetTexture(0, "Source", ycrcbTexture);
                m_sumROIComputeShader.SetBuffer(0, "Sum", ycrcbSumBuffer);
                m_sumROIComputeShader.SetBuffer(0, "Count", ycrcbCountBuffer);
                m_sumROIComputeShader.Dispatch(0, width / 8, height / 8, 1);
                uint[] ycrcbSum = new uint[3];
                uint[] ycrcbCount = new uint[1];
                ycrcbSumBuffer.GetData(ycrcbSum);
                ycrcbCountBuffer.GetData(ycrcbCount);
                Debug.Log($"ROI YCrCb Sum in YCrCb: Y={ycrcbSum[0] / ycrcbCount[0]}, Cr={ycrcbSum[1] / ycrcbCount[0]}, Cb={ycrcbSum[2] / ycrcbCount[0]}");
                if (ycrcbCount[0] > 0)
                {
                    DebugInfo += $"ROI YCrCb Sum in YCrCb: \nY={ycrcbSum[0] / ycrcbCount[0]}, \nCr={ycrcbSum[1] / ycrcbCount[0]}, \nCb={ycrcbSum[2] / ycrcbCount[0]}\n";
                }
                else
                {
                    DebugInfo += "ROI YCrCb Sum in YCrCb: \nY=Placeholder, \nCr=Placeholder, \nCb=Placeholder\n";
                }
                m_debugText.text = DebugInfo;


                DrawCurve(m_curveSystem1, historyY, ycrcbSum[0] / ycrcbCount[0], "Y");
                DrawCurve(m_curveSystem2, historyCr, ycrcbSum[1] / ycrcbCount[0], "Cr");
                DrawCurve(m_curveSystem3, historyCb, ycrcbSum[2] / ycrcbCount[0], "Cb");
                DrawCurve(m_curveSystem4, historyR, rgbSum[0] / roiCount[0], "R");
                DrawCurve(m_curveSystem5, historyG, rgbSum[1] / roiCount[0], "G");
                DrawCurve(m_curveSystem6, historyB, rgbSum[2] / roiCount[0], "B");
            }
        }

        private void PerformCopy(RenderTexture source, RenderTexture destination)
        {
            if (source == null || destination == null)
            {
                Debug.LogError("Source or destination RenderTexture is null.");
                return;
            }
            if (source.width != destination.width || source.height != destination.height)
            {
                Debug.LogError("Source and destination RenderTextures must have the same dimensions.");
                return;
            }
            m_amplificationComputeShader.SetFloat("AmplifyFactor", 1.0f);
            m_amplificationComputeShader.SetTexture(0, "InputTexture", source);
            m_amplificationComputeShader.SetTexture(0, "OutputTexture", destination);
            m_amplificationComputeShader.Dispatch(0, source.width / 8, source.height / 8, 1);
        }

        // Amplification: RGB: a * AmplifyFactor, Attenuation: 1.0f
        private void PerformAmplification(RenderTexture source, RenderTexture destination)
        {
            if (source == null || destination == null)
            {
                Debug.LogError("Source or destination RenderTexture is null.");
                return;
            }
            if (source.width != destination.width || source.height != destination.height)
            {
                Debug.LogError("Source and destination RenderTextures must have the same dimensions.");
                return;
            }
            m_amplificationComputeShader.SetFloat("AmplifyFactor", amplificationFactor);
            m_amplificationComputeShader.SetTexture(0, "InputTexture", source);
            m_amplificationComputeShader.SetTexture(0, "OutputTexture", destination);
            m_amplificationComputeShader.Dispatch(0, source.width / 8, source.height / 8, 1);
        }

        // RGB: 0.5a + 0.5b, Attenuation: 1.0f
        private void PerformAdd(RenderTexture a, RenderTexture b, RenderTexture output)
        {
            if (a == null || b == null || output == null)
            {
                Debug.LogError("One of the RenderTextures is null.");
                return;
            }
            if (a.width != b.width || a.height != b.height || a.width != output.width || a.height != output.height)
            {
                Debug.LogError("All RenderTextures must have the same dimensions.");
                return;
            }
            m_addComputeShader.SetFloat("ScaleA", 0.5f);
            m_addComputeShader.SetFloat("ScaleB", 0.5f);
            m_addComputeShader.SetTexture(0, "TextureA", a);
            m_addComputeShader.SetTexture(0, "TextureB", b);
            m_addComputeShader.SetTexture(0, "OutputTexture", output);
            m_addComputeShader.Dispatch(0, a.width / 8, a.height / 8, 1);
        }

        // RGB: a - b, Attenuation: 1.0f
        private void PerformSubtract(RenderTexture a, RenderTexture b, RenderTexture output)
        {
            if (a == null || b == null || output == null)
            {
                Debug.LogError("One of the RenderTextures is null.");
                return;
            }
            if (a.width != b.width || a.height != b.height || a.width != output.width || a.height != output.height)
            {
                Debug.LogError("All RenderTextures must have the same dimensions.");
                return;
            }
            m_addComputeShader.SetFloat("ScaleA", 1.0f);
            m_addComputeShader.SetFloat("ScaleB", -1.0f);
            m_addComputeShader.SetTexture(0, "TextureA", a);
            m_addComputeShader.SetTexture(0, "TextureB", b);
            m_addComputeShader.SetTexture(0, "OutputTexture", output);
            m_addComputeShader.Dispatch(0, a.width / 8, a.height / 8, 1);
        }

        private RenderTexture CreateRenderTexture(int width, int height)
        {
            RenderTexture rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
            rt.enableRandomWrite = true;
            rt.Create();
            return rt;
        }

        private RenderTexture ReleaseRenderTexture(RenderTexture rt)
        {
            if (rt != null)
            {
                if (RenderTexture.active == rt)
                    RenderTexture.active = null;
                rt.Release();
            }
            return null;
        }

        private RenderTexture[] ReleaseRenderTextures(RenderTexture[] rts)
        {
            if (rts != null)
            {
                for (int i = 0; i < rts.Length; i++)
                {
                    rts[i] = ReleaseRenderTexture(rts[i]);
                }
            }
            return null;
        }

        private ComputeBuffer ReleaseComputeBuffer(ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
            }
            return null;
        }

        private void DrawCurve(GameObject curveSystem, Queue<uint> history, uint newValue, string headerText)
        {
            // Update the history queue
            if (history.Count >= signalQueueLength)
            {
                history.Dequeue();
            }
            history.Enqueue(newValue);
            var historyArray = history.ToArray();
            var historyArrayFloat = Array.ConvertAll(historyArray, x => (float)x);
            var minValue = Mathf.Min(historyArrayFloat);
            var maxValue = Mathf.Max(historyArrayFloat);

            var maxTrans = curveSystem.transform.Find("UpperCurve").position;
            var minTrans = curveSystem.transform.Find("LowerCurve").position;
            Debug.Log($"Max: {maxTrans}, Min: {minTrans}");
            var vertex = maxTrans - minTrans;
            float scale = (newValue - minValue) / (maxValue - minValue);
            var newPos = minTrans + vertex * scale;
            curveSystem.transform.Find("DataCurve").position = newPos;
            Debug.Log($"DrawCurve: {curveSystem.name}, New Position: {newPos}, Scale: {scale}");
            string header = $"{headerText} Max: {maxValue}, Min: {minValue}, Current: {newValue}";
            curveSystem.transform.Find("HeaderText").GetComponent<Text>().text = header;
            Debug.Log(header);
        }

        private void ReleaseRenderTextures4EVM()
        {
            inputTexture = ReleaseRenderTexture(inputTexture);
            gaussianPyramidTextures = ReleaseRenderTextures(gaussianPyramidTextures);
            prevTextures = ReleaseRenderTextures(prevTextures);
            lowpass1Textures = ReleaseRenderTextures(lowpass1Textures);
            lowpass2Textures = ReleaseRenderTextures(lowpass2Textures);
            filteredTextures = ReleaseRenderTextures(filteredTextures);
            upsampledTextures = ReleaseRenderTextures(upsampledTextures);
            reconstructedTextures = ReleaseRenderTextures(reconstructedTextures);
            amplifiedTexture = ReleaseRenderTexture(amplifiedTexture);
            ycrcbTexture = ReleaseRenderTexture(ycrcbTexture);
            rgbTexture = ReleaseRenderTexture(rgbTexture);
            roiTexture = ReleaseRenderTexture(roiTexture);
            ycrcbSumBuffer = ReleaseComputeBuffer(ycrcbSumBuffer);
            ycrcbCountBuffer = ReleaseComputeBuffer(ycrcbCountBuffer);
            rgbSumBuffer = ReleaseComputeBuffer(rgbSumBuffer);
            rgbCountBuffer = ReleaseComputeBuffer(rgbCountBuffer);
        }

        private void OnDestroy()
        {
            Debug.Log("CameraViewerManager OnDestroy called. Releasing RenderTextures.");
            ReleaseRenderTextures4EVM();
        }
    }
}
