// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
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

        [SerializeField] private ComputeShader m_ycrcbComputeShader;
        [SerializeField] private ComputeShader m_rgbComputeShader;
        [SerializeField] private ComputeShader m_amplificationComputeShader;
        [SerializeField] private ComputeShader m_butterworthComputeShader;
        [SerializeField] private ComputeShader m_downsampleComputeShader;
        [SerializeField] private ComputeShader m_upsampleComputeShader;
        [SerializeField] private ComputeShader m_addComputeShader;
        [SerializeField] private ComputeShader m_drawROIComputeShader;
        [SerializeField] private ComputeShader m_sumROIComputeShader;

        [SerializeField] private RawImage m_debugImage;
        [SerializeField] private RawImage m_debugImage2;
        [SerializeField] private RawImage m_debugImage3;
        [SerializeField] private RawImage m_debugImage4;
        [SerializeField] private RawImage m_debugImage5;
        [SerializeField] private RawImage m_debugImage6;

        private float fl = 60.0f / 60.0f; // Frequency low, 60 beats per minute
        private float fh = 100.0f / 60.0f; // Frequency high, 100 beats per minute
        private float fps = 30.0f; // Sample rate of the camera
        private float amplificationFactor = 50.0f; // Amplification factor
        private int nLevels = 8; // Number of levels in the pyramid
        private float attenuationFactor = 1.0f; // Attenuation factor for the Cr and Cb channels, 1 means no attenuation

        // Butterworth coefficients
        private float[] lowA;
        private float[] lowB;
        private float[] highA;
        private float[] highB;

        // ROI Bounding Box, x, y, width, height
        private int4 roiBoundingBox;

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

            resetTexturesFlag = true; // Reset textures of EVM
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

                // Caution! The FixedUpdate reuse the textures of above tests, do not enable them at the same time.
                // For example, the upsampledTextures is 4 layers in test, but the EVM needs 8 layers as specified by nLevels.
            }
        }

        private int actualFps = 0;

        private RenderTexture[] gaussianPyramidTextures;
        private RenderTexture[] prevTextures;
        private RenderTexture[] lowpass1Textures;
        private RenderTexture[] lowpass2Textures;
        private RenderTexture[] filteredTextures;
        private RenderTexture[] workerTextures;
        private RenderTexture[] reconstructedTextures;
        private RenderTexture amplifiedTexture;
        private RenderTexture outputTexture;
        private RenderTexture roiTexture;

        private int actualNLevels = 0;

        private bool resetTexturesFlag = true; // Reset textures of EVM

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

        private ComputeBuffer ycrcbSumBuffer;
        private ComputeBuffer ycrcbCountBuffer;
        private ComputeBuffer rgbSumBuffer;
        private ComputeBuffer rgbCountBuffer;

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
                    ReleaseRenderTextures4TestYCrCbConversion();
                    renderTexture = CreateRenderTexture(width, height);
                    rgbTexture = CreateRenderTexture(width, height);

                    // Create RenderTextures for EVM
                    ReleaseRenderTextures4EVM();
                    gaussianPyramidTextures = new RenderTexture[actualNLevels];
                    prevTextures = new RenderTexture[actualNLevels];
                    lowpass1Textures = new RenderTexture[actualNLevels];
                    lowpass2Textures = new RenderTexture[actualNLevels];
                    filteredTextures = new RenderTexture[actualNLevels];
                    workerTextures = new RenderTexture[actualNLevels];
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
                        workerTextures[i] = CreateRenderTexture(levelWidth, levelHeight);
                        reconstructedTextures[i] = CreateRenderTexture(levelWidth, levelHeight);
                    }
                    amplifiedTexture = CreateRenderTexture(width, height);
                    outputTexture = CreateRenderTexture(width, height);

                    // Manually set ROI bounding box, for example, the center of the frame
                    roiBoundingBox = new int4(width / 2, height / 2, 200, 200);
                    roiTexture = CreateRenderTexture(width, height);
                    m_drawROIComputeShader.SetInts("roi", roiBoundingBox.x, roiBoundingBox.y, roiBoundingBox.z, roiBoundingBox.w);
                    m_sumROIComputeShader.SetInts("roi", roiBoundingBox.x, roiBoundingBox.y, roiBoundingBox.z, roiBoundingBox.w);

                    ycrcbSumBuffer = new ComputeBuffer(3, sizeof(uint));
                    ycrcbCountBuffer = new ComputeBuffer(1, sizeof(uint));
                    rgbSumBuffer = new ComputeBuffer(3, sizeof(uint));
                    rgbCountBuffer = new ComputeBuffer(1, sizeof(uint));
                }

                // EVM Step: Covert the current frame to YCrCb
                Graphics.Blit(frame, renderTexture);
                m_ycrcbComputeShader.SetTexture(0, "InputTexture", renderTexture);
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
                    m_upsampleComputeShader.SetTexture(0, "OutputTexture", workerTextures[i - 1]);
                    m_upsampleComputeShader.Dispatch(0, reconstructedTextures[i].width / 8, reconstructedTextures[i].height / 8, 1);

                    PerformAdd(filteredTextures[i - 1], workerTextures[i - 1], reconstructedTextures[i - 1]);
                }

                // EVM Step: Amplify the final reconstructed texture
                PerformAmplification(reconstructedTextures[0], amplifiedTexture);
                PerformAdd(gaussianPyramidTextures[0], amplifiedTexture, outputTexture);

                // EVM Step: Covert the final output to RGB
                m_rgbComputeShader.SetTexture(0, "InputTexture", outputTexture);
                m_rgbComputeShader.SetTexture(0, "OutputTexture", rgbTexture);
                m_rgbComputeShader.Dispatch(0, width / 8, height / 8, 1);

                // Display
                m_debugImage.texture = gaussianPyramidTextures[actualNLevels - 1];
                m_debugImage2.texture = lowpass1Textures[actualNLevels - 1];
                m_debugImage3.texture = lowpass2Textures[actualNLevels - 1];
                m_debugImage4.texture = reconstructedTextures[0];
                m_debugImage5.texture = amplifiedTexture;
                m_magnifiedImage.texture = rgbTexture;

                // Draw ROI
                m_drawROIComputeShader.SetTexture(0, "Source", renderTexture);
                m_drawROIComputeShader.SetTexture(0, "Result", roiTexture);
                m_drawROIComputeShader.Dispatch(0, width / 8, height / 8, 1);
                m_debugImage6.texture = roiTexture;

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

                m_sumROIComputeShader.SetTexture(0, "Source", outputTexture);
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
            }
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

        private void ReleaseRenderTextures4EVM()
        {
            gaussianPyramidTextures = ReleaseRenderTextures(gaussianPyramidTextures);
            prevTextures = ReleaseRenderTextures(prevTextures);
            lowpass1Textures = ReleaseRenderTextures(lowpass1Textures);
            lowpass2Textures = ReleaseRenderTextures(lowpass2Textures);
            filteredTextures = ReleaseRenderTextures(filteredTextures);
            workerTextures = ReleaseRenderTextures(workerTextures);
            reconstructedTextures = ReleaseRenderTextures(reconstructedTextures);
            amplifiedTexture = ReleaseRenderTexture(amplifiedTexture);
            outputTexture = ReleaseRenderTexture(outputTexture);
            roiTexture = ReleaseRenderTexture(roiTexture);
            ycrcbSumBuffer = ReleaseComputeBuffer(ycrcbSumBuffer);
            ycrcbCountBuffer = ReleaseComputeBuffer(ycrcbCountBuffer);
            rgbSumBuffer = ReleaseComputeBuffer(rgbSumBuffer);
            rgbCountBuffer = ReleaseComputeBuffer(rgbCountBuffer);
        }

        private void ReleaseRenderTextures4TestYCrCbConversion()
        {
            renderTexture = ReleaseRenderTexture(renderTexture);
            ycrcbTexture = ReleaseRenderTexture(ycrcbTexture);
            rgbTexture = ReleaseRenderTexture(rgbTexture);
        }

        private void ReleaseRenderTextures4TestPyramid()
        {
            downsampledTextures = ReleaseRenderTextures(downsampledTextures);
            upsampledTextures = ReleaseRenderTextures(upsampledTextures);
            addedTextures = ReleaseRenderTextures(addedTextures);
        }

        private void OnDestroy()
        {
            Debug.Log("CameraViewerManager OnDestroy called. Releasing RenderTextures.");
            ReleaseRenderTextures4TestYCrCbConversion();
            ReleaseRenderTextures4TestPyramid();
            ReleaseRenderTextures4EVM();
        }
    }
}
